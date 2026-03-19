using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public class StockSyncServiceTests
{
    [Fact]
    public async Task SyncOnceAsync_ShouldPersistSnapshots()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new AppDbContext(options);
        var crawler = new FakeCrawler();
        var syncOptions = Options.Create(new StockSyncOptions
        {
            MarketIndexSymbol = "sh000001",
            Symbols = new List<string> { "600000" }
        });

        var service = new StockSyncService(dbContext, crawler, syncOptions);
        await service.SyncOnceAsync();

        Assert.Single(dbContext.MarketIndexSnapshots);
        Assert.Single(dbContext.StockQuoteSnapshots);
        Assert.Single(dbContext.KLinePoints);
        Assert.Single(dbContext.MinuteLinePoints);
        Assert.Single(dbContext.IntradayMessages);

        var stored = dbContext.StockQuoteSnapshots.First();
        Assert.Equal("sh600000", stored.Symbol);
        Assert.Single(dbContext.StockCompanyProfiles);
        Assert.Equal("银行", dbContext.StockCompanyProfiles.First().SectorName);
    }

    [Fact]
    public async Task SaveDetailAsync_ShouldPersistBasicDetailOnly()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new AppDbContext(options);
        var crawler = new FakeCrawler();
        var syncOptions = Options.Create(new StockSyncOptions());

        var service = new StockSyncService(dbContext, crawler, syncOptions);
        var detail = new StockDetailDto(
            new StockQuoteDto("sh600000", "示例", 1m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, DateTime.UtcNow, Array.Empty<StockNewsDto>(), Array.Empty<StockIndicatorDto>(), 100000000m, 1.1m, 8888, "银行"),
            new List<KLinePointDto> { new(DateTime.UtcNow.Date, 1m, 1m, 1m, 1m, 100m) },
            new List<MinuteLinePointDto> { new(DateOnly.FromDateTime(DateTime.Today), new TimeSpan(9, 30, 0), 1m, 1m, 10m) },
            new List<IntradayMessageDto> { new("消息", "来源", DateTime.UtcNow, null) }
        );

        await service.SaveDetailAsync(detail, "day");

        Assert.Single(dbContext.StockQuoteSnapshots);
        Assert.Single(dbContext.StockCompanyProfiles);
        Assert.Single(dbContext.IntradayMessages);
        Assert.Empty(dbContext.KLinePoints);
        Assert.Empty(dbContext.MinuteLinePoints);
    }

    [Fact]
    public async Task SaveDetailAsync_ShouldIgnoreChartSeriesWhenPersistingRealtimePayload()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new AppDbContext(options);
        var crawler = new FakeCrawler();
        var syncOptions = Options.Create(new StockSyncOptions());

        var service = new StockSyncService(dbContext, crawler, syncOptions);
        var detail = new StockDetailDto(
            new StockQuoteDto("sh600000", "示例", 1m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, DateTime.UtcNow, Array.Empty<StockNewsDto>(), Array.Empty<StockIndicatorDto>(), 100000000m, 1.1m, 8888, "银行"),
            Array.Empty<KLinePointDto>(),
            new List<MinuteLinePointDto> { new(DateOnly.FromDateTime(DateTime.Today), new TimeSpan(9, 30, 0), 1m, 1m, 10m) },
            new List<IntradayMessageDto> { new("消息", "来源", DateTime.UtcNow, null) }
        );

        await service.SaveDetailAsync(detail, "day");

        Assert.Single(dbContext.StockQuoteSnapshots);
        Assert.Empty(dbContext.KLinePoints);
        Assert.Empty(dbContext.MinuteLinePoints);
        Assert.Single(dbContext.IntradayMessages);
    }

    private sealed class FakeCrawler : IStockCrawler
    {
        public string SourceName => "Fake";

        public Task<StockQuoteDto> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StockQuoteDto(
                symbol,
                "示例",
                1m,
                0.1m,
                1m,
                0m,
                0m,
                1m,
                1m,
                0m,
                DateTime.UtcNow,
                Array.Empty<StockNewsDto>(),
                Array.Empty<StockIndicatorDto>(),
                123456789m,
                1.2m,
                1200,
                "银行"
            ));
        }

        public Task<MarketIndexDto> GetMarketIndexAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MarketIndexDto(symbol, "上证", 1m, 0.1m, 1m, DateTime.UtcNow));
        }

        public Task<IReadOnlyList<KLinePointDto>> GetKLineAsync(string symbol, string interval, int count, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<KLinePointDto> list = new[]
            {
                new KLinePointDto(DateTime.UtcNow.Date, 1m, 1m, 1m, 1m, 100m)
            };
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<MinuteLinePointDto>> GetMinuteLineAsync(string symbol, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<MinuteLinePointDto> list = new[]
            {
                new MinuteLinePointDto(DateOnly.FromDateTime(DateTime.Today), new TimeSpan(9, 30, 0), 1m, 1m, 10m)
            };
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<IntradayMessageDto>> GetIntradayMessagesAsync(string symbol, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<IntradayMessageDto> list = new[]
            {
                new IntradayMessageDto("消息", "来源", DateTime.UtcNow, null)
            };
            return Task.FromResult(list);
        }
    }
}
