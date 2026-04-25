using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public class StockHistoryServiceTests
{
    [Fact]
    public async Task UpsertAsync_ShouldInsertAndUpdate()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new AppDbContext(options);
        var dataService = new FakeStockDataService();
        var service = new StockHistoryService(dbContext, dataService);

        var quote = CreateQuote("600000", 10m, 1.2m, 2.5m, 12m, 8m, 0.6m);
        await service.UpsertAsync(quote);

        var stored = await dbContext.StockQueryHistories.SingleAsync();
        Assert.Equal("sh600000", stored.Symbol);
        Assert.Equal(10m, stored.Price);
        Assert.Equal(2.5m, stored.TurnoverRate);

        var updated = CreateQuote("sh600000", 11m, 1.3m, 3.5m, 13m, 9m, 0.8m);
        await service.UpsertAsync(updated);

        var storedUpdated = await dbContext.StockQueryHistories.SingleAsync();
        Assert.Equal(11m, storedUpdated.Price);
        Assert.Equal(3.5m, storedUpdated.TurnoverRate);
    }

    [Fact]
    public async Task RecordAsync_ShouldInsertAndNormalizeSymbol()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new AppDbContext(options);
        var service = new StockHistoryService(dbContext, new FakeStockDataService());

        var stored = await service.RecordAsync(new StockHistoryRecordRequestDto(
            "600519",
            "贵州茅台",
            1234m,
            1.8m,
            2.1m,
            30m,
            1250m,
            1200m,
            0.5m));

        Assert.NotNull(stored);
        Assert.Equal("sh600519", stored.Symbol);
        Assert.Equal("贵州茅台", stored.Name);
        Assert.Equal(1234m, stored.Price);
        Assert.Single(dbContext.StockQueryHistories);
    }

    [Fact]
    public async Task RefreshAsync_ShouldUpdateHistoryFromDataService()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new AppDbContext(options);
        dbContext.StockQueryHistories.Add(new StockQueryHistory
        {
            Symbol = "sh600000",
            Name = "旧数据",
            Price = 1m,
            ChangePercent = 0m,
            TurnoverRate = 0m,
            PeRatio = 0m,
            High = 0m,
            Low = 0m,
            Speed = 0m,
            UpdatedAt = DateTime.UtcNow.AddMinutes(-10)
        });
        await dbContext.SaveChangesAsync();

        var dataService = new FakeStockDataService
        {
            QuoteFactory = symbol => CreateQuote(symbol, 20m, 2m, 4m, 22m, 18m, 1.2m)
        };
        var service = new StockHistoryService(dbContext, dataService);

        var list = await service.RefreshAsync("tencent");

        Assert.Single(list);
        var item = list[0];
        Assert.Equal("sh600000", item.Symbol);
        Assert.Equal(20m, item.Price);
        Assert.Equal(4m, item.TurnoverRate);
        Assert.Equal("tencent", dataService.LastSource);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveHistory()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new AppDbContext(options);
        dbContext.StockQueryHistories.Add(new StockQueryHistory
        {
            Symbol = "sh600000",
            Name = "测试",
            Price = 1m,
            ChangePercent = 0m,
            TurnoverRate = 0m,
            PeRatio = 0m,
            High = 0m,
            Low = 0m,
            Speed = 0m,
            UpdatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = new StockHistoryService(dbContext, new FakeStockDataService());
        var entity = await dbContext.StockQueryHistories.FirstAsync();

        var removed = await service.DeleteAsync(entity.Id);

        Assert.True(removed);
        Assert.Empty(dbContext.StockQueryHistories);
    }

    [Fact]
    public async Task GetAllAsync_ShouldOrderByIdAscending()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new AppDbContext(options);
        dbContext.StockQueryHistories.AddRange(
            new StockQueryHistory
            {
                Id = 2,
                Symbol = "sh600002",
                Name = "B",
                Price = 1m,
                ChangePercent = 0m,
                TurnoverRate = 0m,
                PeRatio = 0m,
                High = 0m,
                Low = 0m,
                Speed = 0m,
                UpdatedAt = DateTime.UtcNow
            },
            new StockQueryHistory
            {
                Id = 1,
                Symbol = "sh600001",
                Name = "A",
                Price = 1m,
                ChangePercent = 0m,
                TurnoverRate = 0m,
                PeRatio = 0m,
                High = 0m,
                Low = 0m,
                Speed = 0m,
                UpdatedAt = DateTime.UtcNow
            });
        await dbContext.SaveChangesAsync();

        var service = new StockHistoryService(dbContext, new FakeStockDataService());
        var list = await service.GetAllAsync();

        Assert.Equal(new[] { 1L, 2L }, list.Select(x => x.Id));
    }

    private static StockQuoteDto CreateQuote(
        string symbol,
        decimal price,
        decimal changePercent,
        decimal turnoverRate,
        decimal high,
        decimal low,
        decimal speed)
    {
        return new StockQuoteDto(
            symbol,
            "示例",
            price,
            0.1m,
            changePercent,
            turnoverRate,
            10m,
            high,
            low,
            speed,
            DateTime.UtcNow,
            Array.Empty<StockNewsDto>(),
            Array.Empty<StockIndicatorDto>()
        );
    }

    private sealed class FakeStockDataService : IStockDataService
    {
        public string? LastSource { get; private set; }
        public Func<string, StockQuoteDto>? QuoteFactory { get; set; }

        public Task<StockQuoteDto> GetQuoteAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            LastSource = source;
            var quote = QuoteFactory?.Invoke(symbol) ?? CreateQuote(symbol, 10m, 1m, 2m, 11m, 9m, 0.5m);
            return Task.FromResult(quote);
        }

        public Task<MarketIndexDto> GetMarketIndexAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MarketIndexDto(symbol, "上证", 1m, 0.1m, 1m, DateTime.UtcNow));
        }

        public Task<IReadOnlyList<KLinePointDto>> GetKLineAsync(string symbol, string interval, int count, string? source = null, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<KLinePointDto> list = new[]
            {
                new KLinePointDto(DateTime.UtcNow.Date, 1m, 1m, 1m, 1m, 100m)
            };
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<MinuteLinePointDto>> GetMinuteLineAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<MinuteLinePointDto> list = new[]
            {
                new MinuteLinePointDto(DateOnly.FromDateTime(DateTime.Today), new TimeSpan(9, 30, 0), 1m, 1m, 10m)
            };
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<IntradayMessageDto>> GetIntradayMessagesAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<IntradayMessageDto> list = new[]
            {
                new IntradayMessageDto("消息", "来源", DateTime.UtcNow, null)
            };
            return Task.FromResult(list);
        }
    }
}
