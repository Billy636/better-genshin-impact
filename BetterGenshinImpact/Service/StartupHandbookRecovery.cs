using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service;

/// <summary>启动阶段的单次书本关闭操作；与正式任务共用输入锁，不启动后台恢复任务。</summary>
internal static class StartupHandbookRecovery
{
    internal static async Task<bool> TryCloseAsync(
        SemaphoreSlim taskSemaphore,
        Func<bool> canRecover,
        Func<bool> isHandbookOpen,
        Action closeHandbook,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || !canRecover()) return false;
        if (!await taskSemaphore.WaitAsync(0, cancellationToken)) return false;
        try
        {
            if (cancellationToken.IsCancellationRequested || !canRecover() || !isHandbookOpen()) return false;
            if (cancellationToken.IsCancellationRequested || !canRecover()) return false;
            closeHandbook();
            // 等待输入和截图生效；由调用方下一次截图确认，绝不把按键发送当作恢复成功。
            await Task.Delay(900, cancellationToken);
            return true;
        }
        finally
        {
            taskSemaphore.Release();
        }
    }
}
