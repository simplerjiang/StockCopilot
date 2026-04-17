using Microsoft.Extensions.Caching.Memory;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Market.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class RealtimeSectorBoardServiceTests
{
    [Fact]
    public async Task GetPageAsync_ReturnsRealtimeRowsSortedByRequestedField()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var client = new FakeSectorRotationClient
        {
            Rows =
            [
                new EastmoneySectorBoardRow(SectorBoardTypes.Concept, "BK1", "机器人", 3.2m, 5m, 2m, 1m, 1m, 1m, 30m, 1.2m, 2, "{}"),
                new EastmoneySectorBoardRow(SectorBoardTypes.Concept, "BK2", "算力", 2.1m, 8m, 3m, 2m, 2m, 1m, 28m, 1.1m, 1, "{}")
            ]
        };

        var service = new RealtimeSectorBoardService(cache, client, new FakeTimeProvider(new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.Zero)));
        var result = await service.GetPageAsync(SectorBoardTypes.Concept, 10, "flow");

        Assert.Equal(SectorBoardTypes.Concept, result.BoardType);
        Assert.Equal("flow", result.Sort);
        Assert.Equal("BK2", result.Items[0].SectorCode);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task GetPageAsync_ReturnsStaleCacheWhenRefreshFails()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var client = new FakeSectorRotationClient
        {
            Rows = [new EastmoneySectorBoardRow(SectorBoardTypes.Concept, "BK1", "机器人", 3.2m, 5m, 2m, 1m, 1m, 1m, 30m, 1.2m, 1, "{}")]
        };
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 3, 19, 8, 0, 0, TimeSpan.Zero));
        var service = new RealtimeSectorBoardService(cache, client, timeProvider);

        var first = await service.GetPageAsync(SectorBoardTypes.Concept, 10, "rank");
        client.ThrowOnCall = true;
        timeProvider.SetUtcNow(new DateTimeOffset(2026, 3, 19, 8, 0, 20, TimeSpan.Zero));

        var second = await service.GetPageAsync(SectorBoardTypes.Concept, 10, "rank");

        Assert.Single(second.Items);
        Assert.Equal(first.Items[0].SectorCode, second.Items[0].SectorCode);
        Assert.Equal(2, client.CallCount);
    }

    private sealed class FakeSectorRotationClient : IEastmoneySectorRotationClient
    {
        public IReadOnlyList<EastmoneySectorBoardRow> Rows { get; set; } = Array.Empty<EastmoneySectorBoardRow>();
        public bool ThrowOnCall { get; set; }
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<EastmoneySectorBoardRow>> GetBoardRankingsAsync(string boardType, int take, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ThrowOnCall)
            {
                throw new HttpRequestException("boom");
            }

            return Task.FromResult(Rows.Take(take).ToArray() as IReadOnlyList<EastmoneySectorBoardRow>);
        }

        public Task<IReadOnlyList<EastmoneySectorLeaderRow>> GetSectorLeadersAsync(string sectorCode, int take, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<EastmoneySectorLeaderRow>>(Array.Empty<EastmoneySectorLeaderRow>());
        }

        public Task<EastmoneyMarketBreadthSnapshot> GetMarketBreadthAsync(int take, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new EastmoneyMarketBreadthSnapshot(0, 0, 0, 0m));
        }

        public Task<decimal> GetTotalMarketTurnoverAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0m);
        }

        public Task<int> GetLimitUpCountAsync(DateOnly tradingDate, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> GetLimitDownCountAsync(DateOnly tradingDate, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> GetBrokenBoardCountAsync(DateOnly tradingDate, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> GetMaxLimitUpStreakAsync(DateOnly tradingDate, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }
    }
}