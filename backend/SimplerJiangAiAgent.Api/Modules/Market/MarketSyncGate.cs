using System.Threading;

namespace SimplerJiangAiAgent.Api.Modules.Market;

/// <summary>
/// V048-S2 #78: 单实例非阻塞门闸，避免 /api/market/sync 并发拖死。
/// 已在跑时立即返回 false，调用方据此返 409 / 429，而不是 30s 阻塞排队。
/// </summary>
public sealed class MarketSyncGate
{
    private int _running;
    private DateTimeOffset _startedAt;

    public bool TryEnter()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return false;
        }
        _startedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public void Exit()
    {
        Interlocked.Exchange(ref _running, 0);
    }

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    public DateTimeOffset StartedAt => _startedAt;
}
