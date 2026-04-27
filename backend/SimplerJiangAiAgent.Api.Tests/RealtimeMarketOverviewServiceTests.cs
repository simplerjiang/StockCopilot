using Microsoft.Extensions.Caching.Memory;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Market.Services;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;
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
        var service = new RealtimeMarketOverviewService(new MemoryCache(new MemoryCacheOptions()), fakeClient, timeProvider: timeProvider);

        var first = await service.GetBatchQuotesAsync(["sh600000"]);
        fakeClient.ThrowOnQuotes = true;
        timeProvider.Advance(TimeSpan.FromSeconds(6));

        var second = await service.GetBatchQuotesAsync(["sh600000"]);

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(first[0].Symbol, second[0].Symbol);
        Assert.Equal(2, fakeClient.BatchQuoteCallCount);
    }

    [Fact]
    public async Task GetOverviewAsync_ShouldFillMissingQuotesFromFallbackSource()
    {
        var fakeClient = new FakeRealtimeClient();
        var fakeStockDataService = new FakeStockDataService
        {
            Quotes =
            {
                ["sh000001"] = new StockQuoteDto("sh000001", "上证指数", 3957.05m, -49.50m, -1.24m, 1.39m, 17.01m, 4022.70m, 3955.71m, -0.30m, new DateTime(2026, 3, 20, 16, 14, 15), Array.Empty<StockNewsDto>(), Array.Empty<StockIndicatorDto>(), 0m, 0.95m),
                ["hsi"] = new StockQuoteDto("hsi", "恒生指数", 25277.32m, -223.26m, -0.88m, 1.73m, 0m, 25563.88m, 25121.46m, -1.38m, new DateTime(2026, 3, 20, 18, 31, 47), Array.Empty<StockNewsDto>(), Array.Empty<StockIndicatorDto>(), 0m, 0m)
            }
        };
        var service = new RealtimeMarketOverviewService(new MemoryCache(new MemoryCacheOptions()), fakeClient, fakeStockDataService);

        var result = await service.GetOverviewAsync(["sh000001", "hsi"]);

        Assert.Equal(2, result.Indices.Count);
        Assert.Equal("sh000001", result.Indices[0].Symbol);
        Assert.Equal("hsi", result.Indices[1].Symbol);
        Assert.Equal(["sh000001", "hsi"], fakeStockDataService.RequestedSymbols);
    }

    [Fact]
    public async Task GetOverviewAsync_ShouldMarkConstantZeroNorthboundSeriesUnavailable()
    {
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 3, 19, 2, 0, 0, TimeSpan.Zero));
        var service = new RealtimeMarketOverviewService(new MemoryCache(new MemoryCacheOptions()), new FakeRealtimeClient
        {
            Northbound = CreateNorthbound([
                new NorthboundFlowPointDto(new DateTime(2026, 3, 19, 1, 30, 0, DateTimeKind.Utc), 0m, 520m, 0m, 420m, 0m),
                new NorthboundFlowPointDto(new DateTime(2026, 3, 19, 3, 0, 0, DateTimeKind.Utc), 0m, 520m, 0m, 420m, 0m),
                new NorthboundFlowPointDto(new DateTime(2026, 3, 19, 5, 0, 0, DateTimeKind.Utc), 0m, 520m, 0m, 420m, 0m),
                new NorthboundFlowPointDto(new DateTime(2026, 3, 19, 7, 0, 0, DateTimeKind.Utc), 0m, 520m, 0m, 420m, 0m)
            ])
        }, timeProvider: timeProvider);

        var result = await service.GetOverviewAsync();

        Assert.True(result.IsStale);
        Assert.NotNull(result.NorthboundFlow);
        Assert.True(result.NorthboundFlow!.IsStale);
        Assert.Equal("unavailable", result.NorthboundFlow.Status);
    }

    [Fact]
    public async Task GetOverviewAsync_ShouldKeepChangingNorthboundSeriesFreshDuringTradingHours()
    {
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 3, 19, 2, 0, 0, TimeSpan.Zero));
        var service = new RealtimeMarketOverviewService(new MemoryCache(new MemoryCacheOptions()), new FakeRealtimeClient
        {
            Northbound = CreateNorthbound([
                new NorthboundFlowPointDto(new DateTime(2026, 3, 19, 1, 30, 0, DateTimeKind.Utc), 0m, 520m, 0m, 420m, 0m),
                new NorthboundFlowPointDto(new DateTime(2026, 3, 19, 3, 0, 0, DateTimeKind.Utc), 2m, 518m, -1m, 421m, 1m),
                new NorthboundFlowPointDto(new DateTime(2026, 3, 19, 5, 0, 0, DateTimeKind.Utc), 3m, 517m, 1m, 419m, 4m)
            ])
        }, timeProvider: timeProvider);

        var result = await service.GetOverviewAsync();

        Assert.False(result.IsStale);
        Assert.NotNull(result.NorthboundFlow);
        Assert.False(result.NorthboundFlow!.IsStale);
        Assert.Equal("ok", result.NorthboundFlow.Status);
    }

    private static NorthboundFlowSnapshotDto CreateNorthbound(IReadOnlyList<NorthboundFlowPointDto> points)
    {
        var latest = points[^1];
        return new NorthboundFlowSnapshotDto(
            latest.Timestamp,
            "03-19",
            "亿元",
            latest.ShanghaiNetInflow,
            latest.ShanghaiBalance,
            latest.ShenzhenNetInflow,
            latest.ShenzhenBalance,
            latest.TotalNetInflow,
            points);
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

    private sealed class FakeStockDataService : IStockDataService
    {
        public Dictionary<string, StockQuoteDto> Quotes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> RequestedSymbols { get; } = new();

        public Task<StockQuoteDto> GetQuoteAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            RequestedSymbols.Add(symbol);
            if (Quotes.TryGetValue(symbol, out var quote))
            {
                return Task.FromResult(quote);
            }

            throw new InvalidOperationException($"Missing fake quote for {symbol}");
        }

        public Task<MarketIndexDto> GetMarketIndexAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<KLinePointDto>> GetKLineAsync(string symbol, string interval, int count, string? source = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<MinuteLinePointDto>> GetMinuteLineAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<IntradayMessageDto>> GetIntradayMessagesAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}