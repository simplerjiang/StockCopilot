using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class TradingPlanTriggerServiceTests
{
    [Fact]
    public async Task EvaluateAsync_TriggersPendingPlanWhenPriceReachesTrigger()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext, triggerPrice: 10.2m, invalidPrice: 9.6m);
        await SeedActiveWatchlistAsync(dbContext, plan.Symbol, plan.Name);
        dbContext.StockQuoteSnapshots.Add(new StockQuoteSnapshot
        {
            Symbol = plan.Symbol,
            Name = plan.Name,
            Price = 10.3m,
            Timestamp = new DateTime(2026, 3, 16, 2, 0, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var changes = await service.EvaluateAsync(new DateTimeOffset(2026, 3, 16, 2, 0, 0, TimeSpan.Zero));

        Assert.Equal(1, changes);
        Assert.Equal(TradingPlanStatus.Triggered, plan.Status);
        Assert.NotNull(plan.TriggeredAt);

        var events = await dbContext.TradingPlanEvents.ToListAsync();
        Assert.Single(events);
        Assert.Equal(TradingPlanEventType.Triggered, events[0].EventType);
        Assert.Equal(TradingPlanEventSeverity.Info, events[0].Severity);
    }

    [Fact]
    public async Task EvaluateAsync_InvalidatesPendingPlanWhenPriceFallsBelowInvalid()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext, triggerPrice: 10.2m, invalidPrice: 9.6m);
        await SeedActiveWatchlistAsync(dbContext, plan.Symbol, plan.Name);
        dbContext.StockQuoteSnapshots.Add(new StockQuoteSnapshot
        {
            Symbol = plan.Symbol,
            Name = plan.Name,
            Price = 9.5m,
            Timestamp = new DateTime(2026, 3, 16, 2, 5, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var changes = await service.EvaluateAsync(new DateTimeOffset(2026, 3, 16, 2, 5, 0, TimeSpan.Zero));

        Assert.Equal(1, changes);
        Assert.Equal(TradingPlanStatus.Invalid, plan.Status);
        Assert.NotNull(plan.InvalidatedAt);

        var events = await dbContext.TradingPlanEvents.ToListAsync();
        Assert.Single(events);
        Assert.Equal(TradingPlanEventType.Invalidated, events[0].EventType);
        Assert.Equal(TradingPlanEventSeverity.Critical, events[0].Severity);
    }

    [Fact]
    public async Task EvaluateAsync_InvalidWinsWhenTriggerAndInvalidMatchTogether()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext, triggerPrice: 10.0m, invalidPrice: 10.5m);
        await SeedActiveWatchlistAsync(dbContext, plan.Symbol, plan.Name);
        dbContext.StockQuoteSnapshots.Add(new StockQuoteSnapshot
        {
            Symbol = plan.Symbol,
            Name = plan.Name,
            Price = 10.0m,
            Timestamp = new DateTime(2026, 3, 16, 2, 10, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        await service.EvaluateAsync(new DateTimeOffset(2026, 3, 16, 2, 10, 0, TimeSpan.Zero));

        Assert.Equal(TradingPlanStatus.Invalid, plan.Status);
        var events = await dbContext.TradingPlanEvents.ToListAsync();
        Assert.Single(events);
        Assert.Equal(TradingPlanEventType.Invalidated, events[0].EventType);
    }

    [Fact]
    public async Task EvaluateAsync_DoesNotDuplicateStatusEventAcrossRepeatedPasses()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext, triggerPrice: 10.2m, invalidPrice: 9.6m);
        await SeedActiveWatchlistAsync(dbContext, plan.Symbol, plan.Name);
        dbContext.StockQuoteSnapshots.Add(new StockQuoteSnapshot
        {
            Symbol = plan.Symbol,
            Name = plan.Name,
            Price = 10.4m,
            Timestamp = new DateTime(2026, 3, 16, 2, 15, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var first = await service.EvaluateAsync(new DateTimeOffset(2026, 3, 16, 2, 15, 0, TimeSpan.Zero));
        var second = await service.EvaluateAsync(new DateTimeOffset(2026, 3, 16, 2, 16, 0, TimeSpan.Zero));

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(TradingPlanStatus.Triggered, plan.Status);
        Assert.Single(await dbContext.TradingPlanEvents.ToListAsync());
    }

    [Fact]
    public async Task EvaluateAsync_CreatesVolumeDivergenceWarningWithoutChangingPlanStatus()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext, triggerPrice: 11.5m, invalidPrice: 9.2m);
        await SeedActiveWatchlistAsync(dbContext, plan.Symbol, plan.Name);
        dbContext.StockQuoteSnapshots.Add(new StockQuoteSnapshot
        {
            Symbol = plan.Symbol,
            Name = plan.Name,
            Price = 10.8m,
            Timestamp = new DateTime(2026, 3, 16, 2, 20, 0, DateTimeKind.Utc)
        });
        SeedMinutePoints(dbContext, plan.Symbol, new DateOnly(2026, 3, 16), new[]
        {
            (new TimeSpan(9, 30, 0), 10.00m, 100m),
            (new TimeSpan(9, 35, 0), 10.15m, 185m),
            (new TimeSpan(9, 40, 0), 10.30m, 255m),
            (new TimeSpan(9, 45, 0), 10.45m, 310m),
            (new TimeSpan(9, 50, 0), 10.60m, 350m),
            (new TimeSpan(9, 55, 0), 10.75m, 375m)
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var first = await service.EvaluateAsync(new DateTimeOffset(2026, 3, 16, 2, 20, 0, TimeSpan.Zero));
        var second = await service.EvaluateAsync(new DateTimeOffset(2026, 3, 16, 2, 21, 0, TimeSpan.Zero));

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(TradingPlanStatus.Pending, plan.Status);

        var events = await dbContext.TradingPlanEvents.ToListAsync();
        Assert.Single(events);
        Assert.Equal(TradingPlanEventType.VolumeDivergenceWarning, events[0].EventType);
        Assert.Equal(TradingPlanEventSeverity.Warning, events[0].Severity);
    }

    [Fact]
    public async Task EvaluateAsync_SkipsPendingPlansOutsideActiveWatchlist()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext, triggerPrice: 10.2m, invalidPrice: 9.6m);
        dbContext.StockQuoteSnapshots.Add(new StockQuoteSnapshot
        {
            Symbol = plan.Symbol,
            Name = plan.Name,
            Price = 10.3m,
            Timestamp = new DateTime(2026, 3, 16, 2, 0, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var changes = await service.EvaluateAsync(new DateTimeOffset(2026, 3, 16, 2, 0, 0, TimeSpan.Zero));

        Assert.Equal(0, changes);
        Assert.Equal(TradingPlanStatus.Pending, plan.Status);
        Assert.Empty(await dbContext.TradingPlanEvents.ToListAsync());
    }

    [Fact]
    public async Task EvaluateAsync_DeduplicatesVolumeDivergenceWarningsWithinSameWindow()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext, triggerPrice: 11.5m, invalidPrice: 9.2m);
        await SeedActiveWatchlistAsync(dbContext, plan.Symbol, plan.Name);
        dbContext.StockQuoteSnapshots.Add(new StockQuoteSnapshot
        {
            Symbol = plan.Symbol,
            Name = plan.Name,
            Price = 10.8m,
            Timestamp = new DateTime(2026, 3, 16, 2, 20, 0, DateTimeKind.Utc)
        });
        SeedMinutePoints(dbContext, plan.Symbol, new DateOnly(2026, 3, 16), new[]
        {
            (new TimeSpan(9, 30, 0), 10.00m, 100m),
            (new TimeSpan(9, 35, 0), 10.15m, 185m),
            (new TimeSpan(9, 40, 0), 10.30m, 255m),
            (new TimeSpan(9, 45, 0), 10.45m, 310m),
            (new TimeSpan(9, 50, 0), 10.60m, 350m),
            (new TimeSpan(9, 56, 0), 10.78m, 380m)
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var first = await service.EvaluateAsync(new DateTimeOffset(2026, 3, 16, 2, 20, 0, TimeSpan.Zero));

        dbContext.MinuteLinePoints.Add(new MinuteLinePointEntity
        {
            Symbol = plan.Symbol,
            Date = new DateOnly(2026, 3, 16),
            Time = new TimeSpan(9, 58, 0),
            Price = 10.82m,
            AveragePrice = 10.82m,
            Volume = 398m
        });
        dbContext.StockQuoteSnapshots.Add(new StockQuoteSnapshot
        {
            Symbol = plan.Symbol,
            Name = plan.Name,
            Price = 10.82m,
            Timestamp = new DateTime(2026, 3, 16, 2, 22, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var second = await service.EvaluateAsync(new DateTimeOffset(2026, 3, 16, 2, 22, 0, TimeSpan.Zero));

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Single(await dbContext.TradingPlanEvents.Where(item => item.EventType == TradingPlanEventType.VolumeDivergenceWarning).ToListAsync());
    }

    private static TradingPlanTriggerService CreateService(AppDbContext dbContext)
    {
        return new TradingPlanTriggerService(
            dbContext,
            Options.Create(new TradingPlanTriggerOptions
            {
                Enabled = true,
                MaxPlansPerPass = 50,
                DivergenceLookbackMinutes = 30
            }));
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }

    private static async Task<TradingPlan> SeedPlanAsync(AppDbContext dbContext, decimal? triggerPrice, decimal? invalidPrice)
    {
        var plan = new TradingPlan
        {
            Symbol = "sh600000",
            Name = "浦发银行",
            Direction = TradingPlanDirection.Long,
            Status = TradingPlanStatus.Pending,
            TriggerPrice = triggerPrice,
            InvalidPrice = invalidPrice,
            AnalysisHistoryId = 1,
            SourceAgent = "commander",
            CreatedAt = new DateTime(2026, 3, 16, 1, 50, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 3, 16, 1, 50, 0, DateTimeKind.Utc)
        };

        dbContext.TradingPlans.Add(plan);
        await dbContext.SaveChangesAsync();
        return plan;
    }

    private static void SeedMinutePoints(AppDbContext dbContext, string symbol, DateOnly date, IEnumerable<(TimeSpan Time, decimal Price, decimal Volume)> points)
    {
        foreach (var point in points)
        {
            dbContext.MinuteLinePoints.Add(new MinuteLinePointEntity
            {
                Symbol = symbol,
                Date = date,
                Time = point.Time,
                Price = point.Price,
                AveragePrice = point.Price,
                Volume = point.Volume
            });
        }
    }

    private static async Task SeedActiveWatchlistAsync(AppDbContext dbContext, string symbol, string name)
    {
        dbContext.ActiveWatchlists.Add(new ActiveWatchlist
        {
            Symbol = symbol,
            Name = name,
            SourceTag = "trading-plan",
            Note = "plan:test",
            IsEnabled = true,
            CreatedAt = new DateTime(2026, 3, 16, 1, 45, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 3, 16, 1, 45, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();
    }
}