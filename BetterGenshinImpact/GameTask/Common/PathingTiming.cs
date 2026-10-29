using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.Common;

/// <summary>可选的地图追踪计时；只观察已有调用，不产生游戏输入或额外识别。</summary>
internal sealed class PathingTiming : IDisposable
{
    private static readonly AsyncLocal<PathingTiming?> Current = new();
    private static readonly AsyncLocal<string?> GroupName = new();
    private static readonly object FileLock = new();
    private readonly object _gate = new();
    private readonly PathingTiming? _parent;
    private readonly string _root;
    private readonly string _route;
    private readonly string? _group = GroupName.Value;
    private readonly Func<string> _outcome;
    private readonly Action<Exception> _reportError;
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly List<object> _spans = new();
    private readonly Dictionary<string, long> _totals = new();
    private readonly Dictionary<string, (int Count, long Ticks)> _details = new();
    private readonly Dictionary<string, int> _counts = new();
    private long _last;
    private string _phase = "Other";
    private int _waypoint = -1;
    private int _segment = -1;
    private int _attempt;
    private bool _closed;

    private PathingTiming(string route, string root, Func<string> outcome, Action<Exception> reportError)
    {
        _parent = Current.Value;
        _route = route;
        _root = root;
        _outcome = outcome;
        _reportError = reportError;
        _last = _started;
    }

    /// <summary>每条路线仅检查一次开关文件，默认关闭；输出失败不得中断任务。</summary>
    internal static PathingTiming? TryStart(string route, string root, Func<string> outcome, Action<Exception> reportError)
    {
        try
        {
            if (!File.Exists(Path.Combine(root, "User", "pathing-timing.enabled"))) return null;
            var timing = new PathingTiming(route, root, outcome, reportError);
            Current.Value = timing;
            return timing;
        }
        catch (Exception ex)
        {
            try { reportError(ex); } catch { /* 观测故障不得传播到任务。 */ }
            return null;
        }
    }

    /// <summary>沿现有异步调用链关联配置组，不依赖全局当前组变量。</summary>
    internal static IDisposable Group(string? name)
    {
        var previous = GroupName.Value;
        GroupName.Value = name;
        return new Scope(() => GroupName.Value = previous);
    }

    /// <summary>记录重试和点位上下文；所有主阶段使用同一单调时钟，互不重复累计。</summary>
    internal static void SetWaypoint(int waypoint, int segment, int attempt)
    {
        var timing = Current.Value;
        if (timing == null) return;
        lock (timing._gate)
        {
            if (timing._closed) return;
            timing.Flush(Stopwatch.GetTimestamp());
            timing._waypoint = waypoint;
            timing._segment = segment;
            timing._attempt = attempt;
        }
    }

    /// <summary>嵌套拾取从父战斗时间中扣除；释放后恢复父阶段。</summary>
    internal static IDisposable? Phase(string phase)
    {
        var timing = Current.Value;
        if (timing == null) return null;
        lock (timing._gate)
        {
            if (timing._closed) return null;
            var previous = timing._phase;
            timing.Switch(phase);
            return new Scope(() =>
            {
                lock (timing._gate)
                {
                    if (!timing._closed) timing.Switch(previous);
                }
            });
        }
    }

    /// <summary>子操作耗时包含在主阶段内，不能再与主阶段相加。</summary>
    internal static IDisposable? Detail(string name)
    {
        var timing = Current.Value;
        if (timing == null) return null;
        var started = Stopwatch.GetTimestamp();
        return new Scope(() =>
        {
            lock (timing._gate)
            {
                if (timing._closed) return;
                timing._details.TryGetValue(name, out var value);
                timing._details[name] = (value.Count + 1, value.Ticks + Stopwatch.GetTimestamp() - started);
            }
        });
    }

    /// <summary>仅计数已有检测或恢复事件，不执行额外检测。</summary>
    internal static void Count(string name)
    {
        var timing = Current.Value;
        if (timing == null) return;
        lock (timing._gate)
        {
            if (timing._closed) return;
            timing._counts.TryGetValue(name, out var count);
            timing._counts[name] = count + 1;
        }
    }

    private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private void Switch(string phase)
    {
        if (_phase == phase) return;
        Flush(Stopwatch.GetTimestamp());
        _phase = phase;
    }

    private void Flush(long now)
    {
        var elapsed = now - _last;
        _totals.TryGetValue(_phase, out var total);
        _totals[_phase] = total + elapsed;
        _spans.Add(new
        {
            phase = _phase, startMs = Milliseconds(_last - _started), durationMs = Milliseconds(elapsed),
            waypoint = _waypoint, segment = _segment, attempt = _attempt
        });
        _last = now;
    }

    /// <summary>包括取消和异常退出；每条路线只写一次独立 JSONL，写入失败仅报告警告。</summary>
    public void Dispose()
    {
        try
        {
            object record;
            lock (_gate)
            {
                if (_closed) return;
                var ended = Stopwatch.GetTimestamp();
                Flush(ended);
                _closed = true;
                record = new
                {
                    schemaVersion = 1, runId = Guid.NewGuid().ToString("N"), route = _route, group = _group,
                    startedUtc = _startedUtc, processId = Environment.ProcessId, outcome = _outcome(),
                    build = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                    totalMs = Milliseconds(ended - _started),
                    phasesMs = _totals.ToDictionary(x => x.Key, x => Milliseconds(x.Value)),
                    detailsIncludedInPhases = _details.ToDictionary(x => x.Key, x => new { count = x.Value.Count, totalMs = Milliseconds(x.Value.Ticks) }),
                    counts = _counts, spans = _spans
                };
            }
            var json = JsonConvert.SerializeObject(record);
            lock (FileLock)
            {
                var directory = Path.Combine(_root, "log", "pathing-timing");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, $"timing-{DateTime.Now:yyyyMMdd}-{Environment.ProcessId}.jsonl");
                File.AppendAllText(path, json + Environment.NewLine, new UTF8Encoding(false));
            }
        }
        catch (Exception ex)
        {
            try { _reportError(ex); } catch { /* 保留原有任务结果。 */ }
        }
        finally
        {
            if (ReferenceEquals(Current.Value, this)) Current.Value = _parent;
        }
    }

    /// <summary>保证计时作用域仅释放一次。</summary>
    private sealed class Scope(Action close) : IDisposable
    {
        private Action? _close = close;
        public void Dispose() => Interlocked.Exchange(ref _close, null)?.Invoke();
    }
}
