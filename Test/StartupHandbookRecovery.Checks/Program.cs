using BetterGenshinImpact.Service;

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine("PASS " + name);
}

using var gate = new SemaphoreSlim(1, 1);
var keys = 0;
Check(!await StartupHandbookRecovery.TryCloseAsync(gate, () => true, () => false, () => keys++, default)
      && keys == 0, "unknown screen does not send input");
Check(!await StartupHandbookRecovery.TryCloseAsync(gate, () => false, () => true, () => keys++, default)
      && keys == 0, "inactive game does not send input");
await gate.WaitAsync();
Check(!await StartupHandbookRecovery.TryCloseAsync(gate, () => true, () => true, () => keys++, default)
      && keys == 0, "running task owns input; startup does not interfere");
gate.Release();

using (var stopped = new CancellationTokenSource())
{
    stopped.Cancel();
    Check(!await StartupHandbookRecovery.TryCloseAsync(gate, () => true, () => true, () => keys++, stopped.Token)
          && keys == 0, "stopped startup does not send input");
}

var enabled = true;
Check(!await StartupHandbookRecovery.TryCloseAsync(gate, () => enabled,
          () => { enabled = false; return true; }, () => keys++, default)
      && keys == 0 && gate.CurrentCount == 1, "stop during recognition prevents input and releases lock");

try
{
    await StartupHandbookRecovery.TryCloseAsync(gate, () => true,
        () => throw new InvalidOperationException("recognition failed"), () => keys++, default);
    throw new Exception("expected recognition failure");
}
catch (InvalidOperationException ex) when (ex.Message == "recognition failed") { }
Check(keys == 0 && gate.CurrentCount == 1, "recognition failure releases input lock");

using (var stopDuringWait = new CancellationTokenSource())
{
    var operation = StartupHandbookRecovery.TryCloseAsync(gate, () => true, () => true,
        () => keys++, stopDuringWait.Token);
    Check(keys == 1 && !operation.IsCompleted && gate.CurrentCount == 0,
        "one close input is awaited while holding input lock");
    Check(!await StartupHandbookRecovery.TryCloseAsync(gate, () => true, () => true, () => keys++, default),
        "concurrent startup cannot send overlapping input");
    stopDuringWait.Cancel();
    try { await operation; throw new Exception("expected cancellation"); }
    catch (OperationCanceledException) { }
    Check(keys == 1 && gate.CurrentCount == 1, "cancellation completes recovery and releases lock");
}

Check(await StartupHandbookRecovery.TryCloseAsync(gate, () => true, () => true, () => keys++, default)
      && keys == 2 && gate.CurrentCount == 1, "recognized book sends one close input and completes");
