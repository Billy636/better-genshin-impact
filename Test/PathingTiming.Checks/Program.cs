using BetterGenshinImpact.GameTask.Common;
using Newtonsoft.Json.Linq;

var testRoot = Path.Combine(Path.GetTempPath(), "Lycia-PathingTiming-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var passed = 0;

string Root(string name, bool enabled = true)
{
    var root = Path.Combine(testRoot, name);
    Directory.CreateDirectory(Path.Combine(root, "User"));
    if (enabled) File.WriteAllText(Path.Combine(root, "User", "pathing-timing.enabled"), "");
    return root;
}

JObject Read(string root) => JObject.Parse(File.ReadAllLines(Directory.GetFiles(Path.Combine(root, "log", "pathing-timing"), "*.jsonl").Single()).Single());
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    passed++;
    Console.WriteLine("PASS " + message);
}

var disabled = Root("disabled", false);
Check(PathingTiming.TryStart("disabled", disabled, () => "completed", _ => { }) == null, "disabled by default");
using (PathingTiming.Phase("Fight")) PathingTiming.Count("disabled_count");
Check(!Directory.Exists(Path.Combine(disabled, "log")), "disabled recording creates no logs");

var nested = Root("nested");
using var group = PathingTiming.Group("test-group");
using (PathingTiming.TryStart("nested-route", nested, () => "completed", ex => throw ex))
{
    PathingTiming.SetWaypoint(3, 1, 2);
    using (PathingTiming.Phase("Fight"))
    {
        await Task.Delay(15);
        using (PathingTiming.Detail("fight_end_check")) await Task.Delay(10);
        using (PathingTiming.Phase("Pickup"))
        using (PathingTiming.Phase("Pickup")) await Task.Delay(15);
        PathingTiming.Count("after_pickup");
        await Task.Delay(10);
    }
}
var record = Read(nested);
Check((string)record["group"]! == "test-group", "configuration group follows the route context");
var sum = ((JObject)record["phasesMs"]!).Properties().Sum(x => (double)x.Value);
Check(Math.Abs(sum - (double)record["totalMs"]!) < 0.001, "nested pickup is excluded from fight; phases sum to route wall time");
Check((double)record["phasesMs"]!["Pickup"]! >= 10 && (double)record["phasesMs"]!["Fight"]! >= 25, "nested scopes restore fight after pickup");
Check((int)record["detailsIncludedInPhases"]!["fight_end_check"]!["count"]! == 1 && (int)record["counts"]!["after_pickup"]! == 1, "detail timing and event counts survive awaits");
Check(((JArray)record["spans"]!).OfType<JObject>().Any(x => (int)x["waypoint"]! == 3 && (int)x["attempt"]! == 2), "waypoint and retry context retained");

await Task.WhenAll(Enumerable.Range(0, 4).Select(async i =>
{
    var root = Root("parallel-" + i);
    using (PathingTiming.TryStart("route-" + i, root, () => "completed", _ => { }))
    {
        using var phase = PathingTiming.Phase("Move");
        await Task.Run(() => PathingTiming.Count("child-" + i));
        await Task.Delay(5);
    }
    var data = Read(root);
    if ((string)data["route"]! != "route-" + i || ((JObject)data["counts"]!).Count != 1)
        throw new Exception("parallel route context crossed");
}));
Check(true, "parallel routes and child tasks keep separate contexts");

var cancelled = Root("cancelled");
try
{
    using var timing = PathingTiming.TryStart("cancelled", cancelled, () => "cancelled", _ => { });
    using var phase = PathingTiming.Phase("Teleport");
    throw new OperationCanceledException();
}
catch (OperationCanceledException) { }
Check((string)Read(cancelled)["outcome"]! == "cancelled", "cancellation writes partial timing without changing cancellation");

var failing = Root("failing");
File.WriteAllText(Path.Combine(failing, "log"), "block output directory");
var reported = false;
using (PathingTiming.TryStart("write-failure", failing, () => "completed", _ => { reported = true; throw new Exception("logger also failed"); }))
{
    using var phase = PathingTiming.Phase("Move");
}
Check(reported, "disk and error-reporter failures do not escape into gameplay");
Check(PathingTiming.Phase("Fight") == null, "route context is cleared after failure");
Console.WriteLine($"{passed} checks passed; artifacts: {testRoot}");
