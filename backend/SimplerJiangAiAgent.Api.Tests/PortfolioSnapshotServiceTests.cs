using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class PortfolioSnapshotServiceTests
{
    [Fact]
    public async Task GetSnapshotAsync_ShouldRecalculatePnL_FromRealtimeQuote()
    {
        await using var dbContext = CreateDbContext();
        dbContext.UserPortfolioSettings.Add(new UserPortfolioSettings
        {
            TotalCapital = 100000m,
            UpdatedAt = DateTime.UtcNow
        });
        dbContext.StockPositions.Add(new StockPosition
        {
            Symbol = "sh600000",
            Name = "浦发银行",
            QuantityLots = 1000,
            AverageCostPrice = 10m,
            TotalCost = 10000m,
            LatestPrice = 10m,
            MarketValue = 10000m,
            UnrealizedPnL = 0m,
            UnrealizedReturnRate = 0m,
            UpdatedAt = DateTime.UtcNow
        });
        // V048-S1 #89: 持仓必须有交易流水支撑
        dbContext.TradeExecutions.Add(new TradeExecution
        {
            Symbol = "sh600000",
            Name = "浦发银行",
            Direction = TradeDirection.Buy,
            TradeType = TradeType.Normal,
            ExecutedPrice = 10m,
            Quantity = 1000,
            ExecutedAt = DateTime.UtcNow.AddDays(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            ComplianceTag = ComplianceTag.Unplanned
        });
        await dbContext.SaveChangesAsync();

        var quoteService = new FakeStockDataService();
        quoteService.SetQuote("sh600000", 12.34m, "浦发银行");

        var service = new PortfolioSnapshotService(dbContext, quoteService);

        var snapshot = await service.GetSnapshotAsync();

        var position = Assert.Single(snapshot.Positions);
        Assert.Equal(12.34m, position.LatestPrice);
        Assert.Equal(12340m, position.MarketValue);
        Assert.Equal(2340m, position.UnrealizedPnL);
        Assert.Equal(0.234m, position.UnrealizedReturnRate);
        Assert.Equal(12340m, snapshot.TotalMarketValue);
        Assert.Equal(2340m, snapshot.TotalUnrealizedPnL);
    }

    [Fact]
    public async Task GetSnapshotAsync_ShouldFallbackToPersistedValues_WhenSingleQuoteFails()
    {
        await using var dbContext = CreateDbContext();
        dbContext.UserPortfolioSettings.Add(new UserPortfolioSettings
        {
            TotalCapital = 50000m,
            UpdatedAt = DateTime.UtcNow
        });
        dbContext.StockPositions.AddRange(
            new StockPosition
            {
                Symbol = "sh600000",
                Name = "浦发银行",
                QuantityLots = 1000,
                AverageCostPrice = 10m,
                TotalCost = 10000m,
                LatestPrice = 10m,
                MarketValue = 10000m,
                UnrealizedPnL = 0m,
                UnrealizedReturnRate = 0m,
                UpdatedAt = DateTime.UtcNow
            },
            new StockPosition
            {
                Symbol = "sz000001",
                Name = "平安银行",
                QuantityLots = 1000,
                AverageCostPrice = 7.5m,
                TotalCost = 7500m,
                LatestPrice = 8m,
                MarketValue = 8000m,
                UnrealizedPnL = 500m,
                UnrealizedReturnRate = 0.066667m,
                UpdatedAt = DateTime.UtcNow
            });
        dbContext.TradeExecutions.AddRange(
            new TradeExecution
            {
                Symbol = "sh600000",
                Name = "浦发银行",
                Direction = TradeDirection.Buy,
                TradeType = TradeType.Normal,
                ExecutedPrice = 10m,
                Quantity = 1000,
                ExecutedAt = DateTime.UtcNow.AddDays(-2),
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                ComplianceTag = ComplianceTag.Unplanned
            },
            new TradeExecution
            {
                Symbol = "sz000001",
                Name = "平安银行",
                Direction = TradeDirection.Buy,
                TradeType = TradeType.Normal,
                ExecutedPrice = 7.5m,
                Quantity = 1000,
                ExecutedAt = DateTime.UtcNow.AddDays(-2),
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                ComplianceTag = ComplianceTag.Unplanned
            });
        await dbContext.SaveChangesAsync();

        var quoteService = new FakeStockDataService();
        quoteService.SetQuote("sh600000", 11m, "浦发银行");
        quoteService.ThrowFor("sz000001");

        var service = new PortfolioSnapshotService(dbContext, quoteService);

        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal(19000m, snapshot.TotalMarketValue);
        Assert.Equal(1500m, snapshot.TotalUnrealizedPnL);

        var updated = Assert.Single(snapshot.Positions.Where(p => p.Symbol == "sh600000"));
        Assert.Equal(11m, updated.LatestPrice);
        Assert.Equal(11000m, updated.MarketValue);
        Assert.Equal(1000m, updated.UnrealizedPnL);

        var fallback = Assert.Single(snapshot.Positions.Where(p => p.Symbol == "sz000001"));
        Assert.Equal(8m, fallback.LatestPrice);
        Assert.Equal(8000m, fallback.MarketValue);
        Assert.Equal(500m, fallback.UnrealizedPnL);
    }

    // ─────────────────────────────────────────────────────────────────
    //  V048-S1 #88 / #89 新增单元测试
    // ─────────────────────────────────────────────────────────────────

    // V048-S1 #88: 持仓账务公式必须自洽——成本 + 浮盈 == 市值；浮盈/成本 == 收益率
    [Fact]
    public async Task GetSnapshotAsync_Accounting_ShouldBeSelfConsistent()
    {
        await using var dbContext = CreateDbContext();
        dbContext.UserPortfolioSettings.Add(new UserPortfolioSettings { TotalCapital = 10000m, UpdatedAt = DateTime.UtcNow });
        dbContext.StockPositions.Add(new StockPosition
        {
            Symbol = "sh603099",
            Name = "长白山",
            QuantityLots = 1,
            AverageCostPrice = 35.53m,
            TotalCost = 35.53m,
            UpdatedAt = DateTime.UtcNow
        });
        dbContext.TradeExecutions.Add(new TradeExecution
        {
            Symbol = "sh603099",
            Name = "长白山",
            Direction = TradeDirection.Buy,
            TradeType = TradeType.Normal,
            ExecutedPrice = 35.53m,
            Quantity = 1,
            ExecutedAt = DateTime.UtcNow.AddDays(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            ComplianceTag = ComplianceTag.Unplanned
        });
        await dbContext.SaveChangesAsync();

        var fake = new FakeStockDataService();
        fake.SetQuote("sh603099", 33.64m, "长白山");
        var service = new PortfolioSnapshotService(dbContext, fake);

        var snapshot = await service.GetSnapshotAsync();
        var pos = Assert.Single(snapshot.Positions);

        // 市值 = last price × qty
        Assert.Equal(33.64m, pos.MarketValue);
        // 浮盈 = 市值 - 成本
        Assert.Equal(33.64m - 35.53m, pos.UnrealizedPnL);
        // 公式自洽：成本 + 浮盈 == 市值
        Assert.Equal(pos.MarketValue, pos.TotalCost + pos.UnrealizedPnL);
        // 收益率 = 浮盈 / 成本
        var expectedRate = (33.64m - 35.53m) / 35.53m;
        Assert.InRange(pos.UnrealizedReturnRate ?? 0m, expectedRate - 0.0001m, expectedRate + 0.0001m);
    }

    // V048-S1 #88: 历史 orphan 持仓（TotalCost=0 但 avgCost>0）应自愈
    [Fact]
    public async Task GetSnapshotAsync_ShouldSelfHeal_WhenTotalCostIsZero()
    {
        await using var dbContext = CreateDbContext();
        dbContext.UserPortfolioSettings.Add(new UserPortfolioSettings { TotalCapital = 10000m, UpdatedAt = DateTime.UtcNow });
        dbContext.StockPositions.Add(new StockPosition
        {
            Symbol = "sh603099",
            Name = "长白山",
            QuantityLots = 1,
            AverageCostPrice = 35.53m,
            TotalCost = 0m,       // 历史脏数据
            UpdatedAt = DateTime.UtcNow
        });
        dbContext.TradeExecutions.Add(new TradeExecution
        {
            Symbol = "sh603099",
            Name = "长白山",
            Direction = TradeDirection.Buy,
            TradeType = TradeType.Normal,
            ExecutedPrice = 35.53m,
            Quantity = 1,
            ExecutedAt = DateTime.UtcNow.AddDays(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            ComplianceTag = ComplianceTag.Unplanned
        });
        await dbContext.SaveChangesAsync();

        var fake = new FakeStockDataService();
        fake.SetQuote("sh603099", 33.64m, "长白山");
        var snapshot = await new PortfolioSnapshotService(dbContext, fake).GetSnapshotAsync();

        var pos = Assert.Single(snapshot.Positions);
        Assert.Equal(35.53m, pos.TotalCost);
        // 浮盈不再 == 市值（不再 +33.64）
        Assert.NotEqual(pos.MarketValue, pos.UnrealizedPnL);
        Assert.Equal(33.64m - 35.53m, pos.UnrealizedPnL);
    }

    // V048-S1 #89: 无任何交易记录时，持仓必须为空；可用资金 = 本金
    [Fact]
    public async Task GetSnapshotAsync_ShouldFilterOrphanPositions_WithoutTradeRecords()
    {
        await using var dbContext = CreateDbContext();
        dbContext.UserPortfolioSettings.Add(new UserPortfolioSettings { TotalCapital = 10000m, UpdatedAt = DateTime.UtcNow });
        // orphan 持仓，无对应交易
        dbContext.StockPositions.Add(new StockPosition
        {
            Symbol = "sh603099",
            Name = "长白山",
            QuantityLots = 1,
            AverageCostPrice = 35.53m,
            TotalCost = 35.53m,
            UpdatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var snapshot = await new PortfolioSnapshotService(dbContext, new FakeStockDataService()).GetSnapshotAsync();

        Assert.Empty(snapshot.Positions);
        // 无持仓 → 可用资金 == 本金
        Assert.Equal(10000m, snapshot.AvailableCash);
        Assert.Equal(0m, snapshot.TotalCost);
        Assert.Equal(0m, snapshot.TotalMarketValue);
    }

    // V048-S1 #89: availableCash = 本金 − Σ(持仓成本) + Σ(已实现盈亏)
    [Fact]
    public async Task GetSnapshotAsync_AvailableCash_ShouldIncludeRealizedPnL()
    {
        await using var dbContext = CreateDbContext();
        dbContext.UserPortfolioSettings.Add(new UserPortfolioSettings { TotalCapital = 10000m, UpdatedAt = DateTime.UtcNow });
        dbContext.StockPositions.Add(new StockPosition
        {
            Symbol = "sh600000",
            Name = "浦发银行",
            QuantityLots = 100,
            AverageCostPrice = 10m,
            TotalCost = 1000m,
            UpdatedAt = DateTime.UtcNow
        });
        dbContext.TradeExecutions.AddRange(
            new TradeExecution
            {
                Symbol = "sh600000", Name = "浦发银行",
                Direction = TradeDirection.Buy, TradeType = TradeType.Normal,
                ExecutedPrice = 10m, Quantity = 100,
                ExecutedAt = DateTime.UtcNow.AddDays(-3), CreatedAt = DateTime.UtcNow.AddDays(-3),
                ComplianceTag = ComplianceTag.Unplanned
            },
            // 已实现盈利 200 的卖出
            new TradeExecution
            {
                Symbol = "sh600000", Name = "浦发银行",
                Direction = TradeDirection.Sell, TradeType = TradeType.Normal,
                ExecutedPrice = 12m, Quantity = 100,
                ExecutedAt = DateTime.UtcNow.AddDays(-1), CreatedAt = DateTime.UtcNow.AddDays(-1),
                RealizedPnL = 200m,
                ComplianceTag = ComplianceTag.Unplanned
            });
        await dbContext.SaveChangesAsync();

        var fake = new FakeStockDataService();
        fake.SetQuote("sh600000", 12m, "浦发银行");

        var snapshot = await new PortfolioSnapshotService(dbContext, fake).GetSnapshotAsync();

        // availableCash = 10000 - 1000 + 200 = 9200
        Assert.Equal(9200m, snapshot.AvailableCash);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }

    private sealed class FakeStockDataService : IStockDataService
    {
        private readonly Dictionary<string, StockQuoteDto> _quotes = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _symbolsThatThrow = new(StringComparer.OrdinalIgnoreCase);

        public void SetQuote(string symbol, decimal price, string name)
        {
            _quotes[symbol] = new StockQuoteDto(
                symbol,
                name,
                price,
                0m,
                0m,
                0m,
                0m,
                price,
                price,
                0m,
                DateTime.UtcNow,
                Array.Empty<StockNewsDto>(),
                Array.Empty<StockIndicatorDto>());
        }

        public void ThrowFor(string symbol)
        {
            _symbolsThatThrow.Add(symbol);
        }

        public Task<StockQuoteDto> GetQuoteAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
        {
            if (_symbolsThatThrow.Contains(symbol))
            {
                throw new InvalidOperationException($"quote failed for {symbol}");
            }

            if (_quotes.TryGetValue(symbol, out var quote))
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