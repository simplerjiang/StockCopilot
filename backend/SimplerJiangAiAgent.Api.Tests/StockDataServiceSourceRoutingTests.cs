using Microsoft.Extensions.Caching.Memory;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StockDataServiceSourceRoutingTests
{
    [Fact]
    public async Task GetQuoteAsync_UsesDefaultCrawler_WhenNoExplicitSource()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var tencent = new FakeSource(TencentName);
        var service = CreateService(cache, new[] { tencent });

        var result = await service.GetQuoteAsync("sh600000");

        Assert.NotNull(result);
        Assert.Equal("聚合", result.Name);
    }

    [Fact]
    public async Task GetQuoteAsync_HonorsExplicitSource()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var tencent = new FakeSource(TencentName)
        {
            Quote = CreateQuote("sh600000", "腾讯行情", 10m)
        };
        var eastmoney = new FakeSource(EastmoneyName)
        {
            Quote = CreateQuote("sh600000", "东方财富行情", 11m)
        };

        var service = CreateService(cache, new[] { tencent, eastmoney });
        var result = await service.GetQuoteAsync("sh600000", TencentName);

        Assert.NotNull(result);
        Assert.Equal("腾讯行情", result.Name);
        Assert.Equal(10m, result.Price);
        Assert.Equal(1, tencent.QuoteCallCount);
        Assert.Equal(0, eastmoney.QuoteCallCount);
    }

    [Fact]
    public async Task GetQuoteAsync_ThrowsUnsupportedStockSource_WhenExplicitSourceIsUnknown()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, new[] { new FakeSource(TencentName) });

        var ex = await Assert.ThrowsAsync<UnsupportedStockSourceException>(() => service.GetQuoteAsync("sh600000", "火星"));

        Assert.Equal("火星", ex.SourceName);
    }

    [Fact]
    public async Task GetMinuteLineAsync_UsesEastmoneyFirst_WhenNoExplicitSource()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var tencent = new FakeSource(TencentName)
        {
            MinuteLines = new[] { new MinuteLinePointDto(new DateOnly(2026, 3, 18), new TimeSpan(9, 30, 0), 10m, 10m, 100m) }
        };
        var eastmoney = new FakeSource(EastmoneyName)
        {
            MinuteLines = new[] { new MinuteLinePointDto(new DateOnly(2026, 3, 18), new TimeSpan(9, 30, 0), 11m, 11m, 200m) }
        };

        var service = CreateService(cache, new[] { tencent, eastmoney });
        var result = await service.GetMinuteLineAsync("sh600000");

        var item = Assert.Single(result);
        Assert.Equal(11m, item.Price);
        Assert.Equal(0, tencent.MinuteCallCount);
        Assert.Equal(1, eastmoney.MinuteCallCount);
    }

    [Fact]
    public async Task GetMinuteLineAsync_FallsBackToTencent_WhenEastmoneyReturnsEmpty()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var tencent = new FakeSource(TencentName)
        {
            MinuteLines = new[] { new MinuteLinePointDto(new DateOnly(2026, 3, 18), new TimeSpan(9, 31, 0), 11m, 11m, 200m) }
        };
        var eastmoney = new FakeSource(EastmoneyName);

        var service = CreateService(cache, new[] { tencent, eastmoney });
        var result = await service.GetMinuteLineAsync("sh600000");

        var item = Assert.Single(result);
        Assert.Equal(11m, item.Price);
        Assert.Equal(1, eastmoney.MinuteCallCount);
        Assert.Equal(1, tencent.MinuteCallCount);
    }

    [Fact]
    public async Task GetKLineAsync_UsesEastmoneyFirst_WhenNoExplicitSource()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var tencent = new FakeSource(TencentName)
        {
            KLines = new[] { new KLinePointDto(new DateTime(2026, 3, 17), 10m, 10m, 10m, 10m, 100m) }
        };
        var eastmoney = new FakeSource(EastmoneyName)
        {
            KLines = new[] { new KLinePointDto(new DateTime(2026, 3, 18), 11m, 11m, 11m, 11m, 200m) }
        };

        var service = CreateService(cache, new[] { tencent, eastmoney });
        var result = await service.GetKLineAsync("sh600000", "day", 60);

        var item = Assert.Single(result);
        Assert.Equal(new DateTime(2026, 3, 18), item.Date);
        Assert.Equal(0, tencent.KLineCallCount);
        Assert.Equal(1, eastmoney.KLineCallCount);
    }

    [Fact]
    public async Task GetKLineAsync_FallsBackToTencent_WhenEastmoneyReturnsEmpty()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var tencent = new FakeSource(TencentName)
        {
            KLines = new[] { new KLinePointDto(new DateTime(2026, 3, 17), 10m, 10m, 10m, 10m, 100m) }
        };
        var eastmoney = new FakeSource(EastmoneyName);

        var service = CreateService(cache, new[] { tencent, eastmoney });
        var result = await service.GetKLineAsync("sh600000", "day", 60);

        var item = Assert.Single(result);
        Assert.Equal(new DateTime(2026, 3, 17), item.Date);
        Assert.Equal(1, tencent.KLineCallCount);
        Assert.Equal(1, eastmoney.KLineCallCount);
    }

    [Fact]
    public async Task GetKLineAsync_HonorsExplicitSource()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var tencent = new FakeSource(TencentName)
        {
            KLines = new[] { new KLinePointDto(new DateTime(2026, 3, 17), 10m, 10m, 10m, 10m, 100m) }
        };
        var eastmoney = new FakeSource(EastmoneyName)
        {
            KLines = new[] { new KLinePointDto(new DateTime(2026, 3, 18), 11m, 11m, 11m, 11m, 200m) }
        };

        var service = CreateService(cache, new[] { tencent, eastmoney });
        var result = await service.GetKLineAsync("sh600000", "day", 60, TencentName);

        var item = Assert.Single(result);
        Assert.Equal(new DateTime(2026, 3, 17), item.Date);
        Assert.Equal(1, tencent.KLineCallCount);
        Assert.Equal(0, eastmoney.KLineCallCount);
    }

    [Fact]
    public async Task GetKLineAsync_FiltersZeroHighLowRowsAndKeepsZeroVolumeValidRows()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var eastmoney = new FakeSource(EastmoneyName)
        {
            KLines = new[]
            {
                new KLinePointDto(new DateTime(2026, 3, 17), 10m, 10m, 0m, 0m, 100m),
                new KLinePointDto(new DateTime(2026, 3, 18), 11m, 11m, 12m, 8m, 0m)
            }
        };

        var service = CreateService(cache, new[] { eastmoney });
        var result = await service.GetKLineAsync("sh600000", "day", 60, EastmoneyName);

        var item = Assert.Single(result);
        Assert.Equal(new DateTime(2026, 3, 18), item.Date);
        Assert.Equal(12m, item.High);
        Assert.Equal(8m, item.Low);
        Assert.Equal(0m, item.Volume);
    }

    [Fact]
    public async Task GetKLineAsync_ReturnsEmptyWhenExplicitSourceRowsAreAllZeroHighLow()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var eastmoney = new FakeSource(EastmoneyName)
        {
            KLines = new[]
            {
                new KLinePointDto(new DateTime(2026, 3, 17), 10m, 10m, 0m, 0m, 100m),
                new KLinePointDto(new DateTime(2026, 3, 18), 11m, 11m, 0m, 0m, 0m)
            }
        };

        var service = CreateService(cache, new[] { eastmoney });
        var result = await service.GetKLineAsync("sh600000", "day", 60, EastmoneyName);

        Assert.Empty(result);
        Assert.Equal(1, eastmoney.KLineCallCount);
    }

    [Fact]
    public async Task GetIntradayMessagesResultAsync_SkipsOrdinaryStockPaths_ForIndexSymbols()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var eastmoney = new FakeSource(EastmoneyName)
        {
            Messages = new[] { new IntradayMessageDto("平安银行公告", "东方财富公告", DateTime.UtcNow, null) }
        };
        var ingestion = new FakeLocalFactIngestionService();
        var queryTool = new FakeQueryLocalFactDatabaseTool();
        var service = CreateService(cache, new[] { eastmoney }, ingestion, queryTool);

        var result = await service.GetIntradayMessagesResultAsync("sh000001");

        Assert.Empty(result.Messages);
        Assert.True(result.Degraded);
        Assert.Contains("指数", result.Warning, StringComparison.Ordinal);
        Assert.Equal(0, ingestion.EnsureFreshCallCount);
        Assert.Equal(0, queryTool.QueryLevelCallCount);
        Assert.Equal(0, eastmoney.MessageCallCount);
    }

    private static StockDataService CreateService(IMemoryCache cache, IEnumerable<IStockCrawlerSource> sources)
    {
        return CreateService(cache, sources, new FakeLocalFactIngestionService(), new FakeQueryLocalFactDatabaseTool());
    }

    private static StockDataService CreateService(
        IMemoryCache cache,
        IEnumerable<IStockCrawlerSource> sources,
        ILocalFactIngestionService ingestionService,
        IQueryLocalFactDatabaseTool queryTool)
    {
        return new StockDataService(
            cache,
            new FakeDefaultCrawler(),
            ingestionService,
            queryTool,
            sources);
    }

    private const string TencentName = "腾讯";
    private const string EastmoneyName = "东方财富";

    private static StockQuoteDto CreateQuote(string symbol, string name, decimal price)
    {
        return new StockQuoteDto(symbol, name, price, 0m, 0m, 0m, 0m, price, price, 0m, DateTime.UtcNow, Array.Empty<StockNewsDto>(), Array.Empty<StockIndicatorDto>());
    }

    private sealed class FakeSource : IStockCrawlerSource
    {
        public FakeSource(string sourceName)
        {
            SourceName = sourceName;
        }

        public string SourceName { get; }
        public IReadOnlyList<KLinePointDto> KLines { get; set; } = Array.Empty<KLinePointDto>();
        public IReadOnlyList<MinuteLinePointDto> MinuteLines { get; set; } = Array.Empty<MinuteLinePointDto>();
        public IReadOnlyList<IntradayMessageDto> Messages { get; set; } = Array.Empty<IntradayMessageDto>();
        public StockQuoteDto? Quote { get; set; }
        public int QuoteCallCount { get; private set; }
        public int KLineCallCount { get; private set; }
        public int MinuteCallCount { get; private set; }
        public int MessageCallCount { get; private set; }

        public Task<StockQuoteDto?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            QuoteCallCount++;
            return Task.FromResult<StockQuoteDto?>(Quote ?? CreateQuote(symbol, SourceName, 1m));
        }

        public Task<MarketIndexDto> GetMarketIndexAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MarketIndexDto(symbol, SourceName, 0m, 0m, 0m, DateTime.UtcNow));
        }

        public Task<IReadOnlyList<KLinePointDto>> GetKLineAsync(string symbol, string interval, int count, CancellationToken cancellationToken = default)
        {
            KLineCallCount++;
            return Task.FromResult(KLines);
        }

        public Task<IReadOnlyList<MinuteLinePointDto>> GetMinuteLineAsync(string symbol, CancellationToken cancellationToken = default)
        {
            MinuteCallCount++;
            return Task.FromResult(MinuteLines);
        }

        public Task<IReadOnlyList<IntradayMessageDto>> GetIntradayMessagesAsync(string symbol, CancellationToken cancellationToken = default)
        {
            MessageCallCount++;
            return Task.FromResult(Messages);
        }
    }

    private sealed class FakeDefaultCrawler : IStockCrawler
    {
        public string SourceName => "聚合";

        public Task<StockQuoteDto?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<StockQuoteDto?>(CreateQuote(symbol, SourceName, 1m));
        }

        public Task<MarketIndexDto> GetMarketIndexAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MarketIndexDto(symbol, SourceName, 0m, 0m, 0m, DateTime.UtcNow));
        }

        public Task<IReadOnlyList<KLinePointDto>> GetKLineAsync(string symbol, string interval, int count, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<KLinePointDto>>(Array.Empty<KLinePointDto>());
        }

        public Task<IReadOnlyList<MinuteLinePointDto>> GetMinuteLineAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<MinuteLinePointDto>>(Array.Empty<MinuteLinePointDto>());
        }

        public Task<IReadOnlyList<IntradayMessageDto>> GetIntradayMessagesAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<IntradayMessageDto>>(Array.Empty<IntradayMessageDto>());
        }
    }

    private sealed class FakeLocalFactIngestionService : ILocalFactIngestionService
    {
        public int EnsureFreshCallCount { get; private set; }

        public Task SyncAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnsureMarketFreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnsureFreshAsync(string symbol, CancellationToken cancellationToken = default)
        {
            EnsureFreshCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeQueryLocalFactDatabaseTool : IQueryLocalFactDatabaseTool
    {
        public int QueryLevelCallCount { get; private set; }

        public Task<LocalFactPackageDto> QueryAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LocalFactPackageDto(symbol, null, null, Array.Empty<LocalNewsItemDto>(), Array.Empty<LocalNewsItemDto>(), Array.Empty<LocalNewsItemDto>(), null, Array.Empty<LocalFundamentalFactDto>()));
        }

        public Task<LocalNewsBucketDto> QueryLevelAsync(string symbol, string level, CancellationToken cancellationToken = default)
        {
            QueryLevelCallCount++;
            return Task.FromResult(new LocalNewsBucketDto(symbol, level, null, Array.Empty<LocalNewsItemDto>()));
        }

        public Task<LocalNewsBucketDto> QueryMarketAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LocalNewsBucketDto(string.Empty, "market", null, Array.Empty<LocalNewsItemDto>()));
        }

        public Task<LocalNewsArchivePageDto> QueryArchiveAsync(string? keyword, string? level, string? sentiment, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LocalNewsArchivePageDto(page, pageSize, 0, keyword, level, sentiment, Array.Empty<LocalNewsArchiveItemDto>()));
        }
    }
}