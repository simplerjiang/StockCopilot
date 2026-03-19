using Microsoft.Extensions.Caching.Memory;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Market.Services;
using System;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class RealtimeMarketOverviewServiceTests
{
    [Fact]
    public async Task GetOverviewAsync_ShouldReturnPartialPayload_WhenOneSourceFails()
    {
        var service = new RealtimeMarketOverviewService(new MemoryCache(new MemoryCacheOptions()), new FakeRealtimeClient
        {
            Quotes = [new BatchStockQuoteDto("sh000001", "上证指数", 4000m, -10m, -0.25m, 4010m, 3990m, 1.2m, 0m, 900000000000m, 0.9m, new DateTime(2026, 3, 19, 15, 0, 0))],
            Breadth = new MarketBreadthDistributionDto(new DateOnly(2026, 3, 19), 2200, 1800, 200, 80, 10, Array.Empty<MarketBreadthBucketDto>()),
            ThrowOnMainFlow = true,
            Northbound = new NorthboundFlowSnapshotDto(new DateTime(2026, 3, 19, 15, 0, 0), "03-19", "亿元", 12m, 520m, 8m, 420m, 20m, Array.Empty<NorthboundFlowPointDto>())
        });

        var result = await service.GetOverviewAsync();

        Assert.Single(result.Indices);
        Assert.Null(result.MainCapitalFlow);
        Assert.NotNull(result.NorthboundFlow);
        Assert.NotNull(result.Breadth);
    }

    [Fact]
    public async Task GetBatchQuotesAsync_ShouldNormalizeAndDeduplicateSymbols()
    {
        var fakeClient = new FakeRealtimeClient
        {
            Quotes = [new BatchStockQuoteDto("sh600000", "浦发银行", 10m, 0m, 0m, 10m, 10m, 0m, 0m, 1m, 0m, new DateTime(2026, 3, 19, 15, 0, 0))]
        };
        var service = new RealtimeMarketOverviewService(new MemoryCache(new MemoryCacheOptions()), fakeClient);

        var result = await service.GetBatchQuotesAsync(["600000", "sh600000", " 600000 "]);

        Assert.Single(result);
        Assert.Single(fakeClient.RequestedSymbols);
        Assert.Equal("sh600000", fakeClient.RequestedSymbols[0]);
    }

    [Fact]
    public async Task GetBatchQuotesAsync_ShouldReturnStaleCache_WhenRefreshFails()
    {
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 3, 19, 7, 0, 0, TimeSpan.Zero));
        var fakeClient = new FakeRealtimeClient
        {
            Quotes = [new BatchStockQuoteDto("sh600000", "浦发银行", 10m, 0m, 0m, 10m, 10m, 0m, 0m, 1m, 0m, new DateTime(2026, 3, 19, 15, 0, 0))]
        };
        var service = new RealtimeMarketOverviewService(new MemoryCache(new MemoryCacheOptions()), fakeClient, timeProvider);

        var first = await service.GetBatchQuotesAsync(["sh600000"]);
        fakeClient.ThrowOnQuotes = true;
        timeProvider.Advance(TimeSpan.FromSeconds(6));

        var second = await service.GetBatchQuotesAsync(["sh600000"]);

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(first[0].Symbol, second[0].Symbol);
        Assert.Equal(2, fakeClient.BatchQuoteCallCount);
    }

    private sealed class FakeRealtimeClient : IEastmoneyRealtimeMarketClient
    {
        public IReadOnlyList<string> RequestedSymbols { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<BatchStockQuoteDto> Quotes { get; init; } = Array.Empty<BatchStockQuoteDto>();
        public MarketCapitalFlowSnapshotDto? MainFlow { get; init; }
        public NorthboundFlowSnapshotDto? Northbound { get; init; }
        public MarketBreadthDistributionDto? Breadth { get; init; }
        public bool ThrowOnMainFlow { get; init; }
        public bool ThrowOnQuotes { get; set; }
        public int BatchQuoteCallCount { get; private set; }

        public Task<IReadOnlyList<BatchStockQuoteDto>> GetBatchQuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken = default)
        {
            RequestedSymbols = symbols;
            BatchQuoteCallCount += 1;
            if (ThrowOnQuotes)
            {
                throw new HttpRequestException("quote boom");
            }

            return Task.FromResult(Quotes);
        }

        public Task<MarketCapitalFlowSnapshotDto?> GetMainCapitalFlowAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnMainFlow)
            {
                throw new HttpRequestException("boom");
            }

            return Task.FromResult(MainFlow);
        }

        public Task<NorthboundFlowSnapshotDto?> GetNorthboundFlowAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Northbound);
        }

        public Task<MarketBreadthDistributionDto?> GetBreadthDistributionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Breadth);
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public TestTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
    }
}