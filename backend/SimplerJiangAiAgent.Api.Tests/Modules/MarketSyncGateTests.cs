using SimplerJiangAiAgent.Api.Modules.Market;

namespace SimplerJiangAiAgent.Api.Tests;

/// <summary>
/// V048-S2 #78: 单元测试单实例非阻塞门闸的语义
/// </summary>
public sealed class MarketSyncGateTests
{
    [Fact]
    public void TryEnter_FirstCall_ReturnsTrue()
    {
        var gate = new MarketSyncGate();
        Assert.True(gate.TryEnter());
        Assert.True(gate.IsRunning);
    }

    [Fact]
    public void TryEnter_WhileRunning_ReturnsFalseImmediately()
    {
        var gate = new MarketSyncGate();
        Assert.True(gate.TryEnter());

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var second = gate.TryEnter();
        sw.Stop();

        Assert.False(second);
        // 必须立即返回，不阻塞排队
        Assert.True(sw.ElapsedMilliseconds < 100, $"TryEnter blocked for {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Exit_ReleasesGate_AllowsReentry()
    {
        var gate = new MarketSyncGate();
        Assert.True(gate.TryEnter());
        gate.Exit();
        Assert.False(gate.IsRunning);
        Assert.True(gate.TryEnter());
    }

    [Fact]
    public async Task TryEnter_HighConcurrency_ExactlyOneWins()
    {
        var gate = new MarketSyncGate();
        var winners = 0;
        var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            if (gate.TryEnter())
            {
                Interlocked.Increment(ref winners);
            }
        })).ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(1, winners);
    }
}
