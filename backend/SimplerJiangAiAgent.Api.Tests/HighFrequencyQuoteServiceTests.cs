using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public class HighFrequencyQuoteServiceTests
{
    [Fact]
    public async Task SyncOnceAsync_ShouldSkipOutsideTradingHours()
    {
        var crawler = new FakeCrawler();
        using var provider = BuildProvider(Guid.NewGuid().ToString("N"), crawler);
        await SeedWatchlistAsync(provider, "600000");

        var service = new HighFrequencyQuoteService(
            provider,
            NullLogger<HighFrequencyQuoteService>.Instance,
            Options.Create(new HighFrequencyQuoteOptions()));

        var processed = await service.SyncOnceAsync(new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(0, processed);
        Assert.Equal(0, crawler.QuoteCalls);
        Assert.Equal(0, crawler.MinuteCalls);
    }

    [Fact]
    public async Task SyncOnceAsync_ShouldPersistRealtimeDataAndMarkWatchlistDuringTradingHours()
    {
        var crawler = new FakeCrawler();
        var databaseName = Guid.NewGuid().ToString("N");
        using var provider = BuildProvider(databaseName, crawler);
        await SeedWatchlistAsync(provider, "600000");

        var service = new HighFrequencyQuoteService(
            provider,
            NullLogger<HighFrequencyQuoteService>.Instance,
            Options.Create(new HighFrequencyQuoteOptions { MaxConcurrentSymbols = 1 }));

        var processed = await service.SyncOnceAsync(new DateTimeOffset(2026, 3, 16, 2, 0, 0, TimeSpan.Zero));

        Assert.Equal(1, processed);
        Assert.Equal(1, crawler.QuoteCalls);
        Assert.Equal(1, crawler.MinuteCalls);
        Assert.Equal(1, crawler.MessageCalls);

        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(dbContext.StockQuoteSnapshots);
        Assert.Single(dbContext.MinuteLinePoints);
        Assert.Single(dbContext.IntradayMessages);

        var watchlist = await dbContext.ActiveWatchlists.SingleAsync();
        Assert.NotNull(watchlist.LastQuoteSyncAt);
        Assert.Equal("sh600000", watchlist.Symbol);
        Assert.Equal("示例", watchlist.Name);
    }

    [Fact]
    public async Task SyncOnceAsync_ShouldSkipLegalHolidayEvenWithinTradingWindow()
    {
        var crawler = new FakeCrawler();
        using var provider = BuildProvider(Guid.NewGuid().ToString("N"), crawler);
        await SeedWatchlistAsync(provider, "600000");

        var service = new HighFrequencyQuoteService(
            provider,
            NullLogger<HighFrequencyQuoteService>.Instance,
            Options.Create(new HighFrequencyQuoteOptions()));

        var processed = await service.SyncOnceAsync(new DateTimeOffset(2026, 10, 1, 2, 0, 0, TimeSpan.Zero));

        Assert.Equal(0, processed);
        Assert.Equal(0, crawler.QuoteCalls);
        Assert.Equal(0, crawler.MinuteCalls);
        Assert.Equal(0, crawler.MessageCalls);
    }

    [Fact]
    public async Task SyncOnceAsync_ShouldPersistQuoteAndMinuteWhenMessagesFail()
    {
        var crawler = new FakeCrawler { ThrowOnMessages = true };
        var databaseName = Guid.NewGuid().ToString("N");
        using var provider = BuildProvider(databaseName, crawler);
        await SeedWatchlistAsync(provider, "600000");

        var service = new HighFrequencyQuoteService(
            provider,
            NullLogger<HighFrequencyQuoteService>.Instance,
            Options.Create(new HighFrequencyQuoteOptions { MaxConcurrentSymbols = 1 }));

        var processed = await service.SyncOnceAsync(new DateTimeOffset(2026, 3, 16, 2, 0, 0, TimeSpan.Zero));

        Assert.Equal(1, processed);
        Assert.Equal(1, crawler.MessageCalls);

        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(dbContext.StockQuoteSnapshots);
        Assert.Single(dbContext.MinuteLinePoints);
        Assert.Empty(dbContext.IntradayMessages);
    }

    private static async Task SeedWatchlistAsync(ServiceProvider provider, string symbol)
    {
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IActiveWatchlistService>();
        await service.UpsertAsync(symbol, sourceTag: "manual", note: "test", isEnabled: true);
    }

    private static ServiceProvider BuildProvider(string databaseName, FakeCrawler crawler)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IActiveWatchlistService, ActiveWatchlistService>();
        services.AddScoped<IStockSyncService, StockSyncService>();
        services.AddSingleton<IStockCrawler>(crawler);
        services.AddSingleton<IOptions<StockSyncOptions>>(Options.Create(new StockSyncOptions()));
        return services.BuildServiceProvider();
    }

    private sealed class FakeCrawler : IStockCrawler
    {
        public int QuoteCalls { get; private set; }
        public int MinuteCalls { get; private set; }
        public int MessageCalls { get; private set; }
        public bool ThrowOnMessages { get; set; }

        public string SourceName => "Fake";

        public Task<StockQuoteDto> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            QuoteCalls++;
            return Task.FromResult(new StockQuoteDto(
                StockSymbolNormalizer.Normalize(symbol),
                "示例",
                10m,
                0.5m,
                5m,
                0m,
                12m,
                0m,
                0m,
                0m,
                DateTime.UtcNow,
                Array.Empty<StockNewsDto>(),
                Array.Empty<StockIndicatorDto>(),
                1000m,
                1.1m,
                9999,
                "银行"));
        }

        public Task<MarketIndexDto> GetMarketIndexAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MarketIndexDto(symbol, "指数", 0m, 0m, 0m, DateTime.UtcNow));
        }

        public Task<IReadOnlyList<KLinePointDto>> GetKLineAsync(string symbol, string interval, int count, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<KLinePointDto>>(Array.Empty<KLinePointDto>());
        }

        public Task<IReadOnlyList<MinuteLinePointDto>> GetMinuteLineAsync(string symbol, CancellationToken cancellationToken = default)
        {
            MinuteCalls++;
            return Task.FromResult<IReadOnlyList<MinuteLinePointDto>>(new[]
            {
                new MinuteLinePointDto(DateOnly.FromDateTime(DateTime.UtcNow.Date), new TimeSpan(9, 30, 0), 10m, 9.8m, 100m)
            });
        }

        public Task<IReadOnlyList<IntradayMessageDto>> GetIntradayMessagesAsync(string symbol, CancellationToken cancellationToken = default)
        {
            MessageCalls++;
            if (ThrowOnMessages)
            {
                throw new InvalidOperationException("message source unavailable");
            }

            return Task.FromResult<IReadOnlyList<IntradayMessageDto>>(new[]
            {
                new IntradayMessageDto("高频快讯", "fake", DateTime.UtcNow, "https://example.com")
            });
        }
    }
}