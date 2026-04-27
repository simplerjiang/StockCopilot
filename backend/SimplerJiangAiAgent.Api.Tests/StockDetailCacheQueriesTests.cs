using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StockDetailCacheQueriesTests
{
    [Fact]
    public async Task GetRecentKLinesAsync_ReturnsLatestWindowInAscendingOrder()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new AppDbContext(options);
        dbContext.KLinePoints.AddRange(
            new KLinePointEntity { Symbol = "sh600000", Interval = "day", Date = new DateTime(2026, 3, 10), Open = 1, Close = 1, High = 1, Low = 1, Volume = 10 },
            new KLinePointEntity { Symbol = "sh600000", Interval = "day", Date = new DateTime(2026, 3, 11), Open = 2, Close = 2, High = 2, Low = 2, Volume = 20 },
            new KLinePointEntity { Symbol = "sh600000", Interval = "day", Date = new DateTime(2026, 3, 12), Open = 3, Close = 3, High = 3, Low = 3, Volume = 30 },
            new KLinePointEntity { Symbol = "sh600000", Interval = "day", Date = new DateTime(2026, 3, 13), Open = 4, Close = 4, High = 4, Low = 4, Volume = 40 });
        await dbContext.SaveChangesAsync();

        var result = await StockDetailCacheQueries.GetRecentKLinesAsync(dbContext, "sh600000", "day", 2);

        Assert.Equal(2, result.Count);
        Assert.Collection(
            result,
            item => Assert.Equal(new DateTime(2026, 3, 12), item.Date),
            item => Assert.Equal(new DateTime(2026, 3, 13), item.Date));
    }

    [Fact]
    public async Task GetRecentKLinesAsync_FiltersZeroHighLowRowsAndKeepsZeroVolumeValidRows()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new AppDbContext(options);
        dbContext.KLinePoints.AddRange(
            new KLinePointEntity { Symbol = "sh600001", Interval = "day", Date = new DateTime(2026, 4, 24), Open = 10, Close = 10, High = 10, Low = 10, Volume = 100 },
            new KLinePointEntity { Symbol = "sh600001", Interval = "day", Date = new DateTime(2026, 4, 25), Open = 10, Close = 10, High = 0, Low = 0, Volume = 100 },
            new KLinePointEntity { Symbol = "sh600001", Interval = "day", Date = new DateTime(2026, 4, 26), Open = 11, Close = 11, High = 12, Low = 8, Volume = 0 });
        await dbContext.SaveChangesAsync();

        var result = await StockDetailCacheQueries.GetRecentKLinesAsync(dbContext, "sh600001", "day", 2);

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, item => item.High == 0m && item.Low == 0m);
        Assert.Collection(
            result,
            item => Assert.Equal(new DateTime(2026, 4, 24), item.Date),
            item =>
            {
                Assert.Equal(new DateTime(2026, 4, 26), item.Date);
                Assert.Equal(12m, item.High);
                Assert.Equal(8m, item.Low);
                Assert.Equal(0m, item.Volume);
            });
    }

    [Fact]
    public async Task GetLatestMinuteLinesAsync_ReturnsOnlyLatestTradingDate()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new AppDbContext(options);
        dbContext.MinuteLinePoints.AddRange(
            new MinuteLinePointEntity { Symbol = "sh600000", Date = new DateOnly(2026, 3, 12), Time = new TimeSpan(9, 30, 0), Price = 10, AveragePrice = 10, Volume = 10 },
            new MinuteLinePointEntity { Symbol = "sh600000", Date = new DateOnly(2026, 3, 12), Time = new TimeSpan(9, 31, 0), Price = 11, AveragePrice = 10.5m, Volume = 11 },
            new MinuteLinePointEntity { Symbol = "sh600000", Date = new DateOnly(2026, 3, 13), Time = new TimeSpan(9, 30, 0), Price = 12, AveragePrice = 12, Volume = 12 },
            new MinuteLinePointEntity { Symbol = "sh600000", Date = new DateOnly(2026, 3, 13), Time = new TimeSpan(9, 31, 0), Price = 13, AveragePrice = 12.5m, Volume = 13 });
        await dbContext.SaveChangesAsync();

        var result = await StockDetailCacheQueries.GetLatestMinuteLinesAsync(dbContext, "sh600000");

        Assert.Equal(2, result.Count);
        Assert.All(result, item => Assert.Equal(new DateOnly(2026, 3, 13), item.Date));
        Assert.Collection(
            result,
            item => Assert.Equal(new TimeSpan(9, 30, 0), item.Time),
            item => Assert.Equal(new TimeSpan(9, 31, 0), item.Time));
    }

    [Fact]
    public async Task GetLatestMinuteLinesAsync_WorksWithSqliteAndAppliesTakeInAscendingOrder()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        dbContext.MinuteLinePoints.AddRange(
            new MinuteLinePointEntity { Symbol = "sh600000", Date = new DateOnly(2026, 3, 12), Time = new TimeSpan(9, 30, 0), Price = 10, AveragePrice = 10, Volume = 10 },
            new MinuteLinePointEntity { Symbol = "sh600000", Date = new DateOnly(2026, 3, 13), Time = new TimeSpan(10, 0, 0), Price = 13, AveragePrice = 12.5m, Volume = 13 },
            new MinuteLinePointEntity { Symbol = "sh600000", Date = new DateOnly(2026, 3, 13), Time = new TimeSpan(9, 31, 0), Price = 12, AveragePrice = 11.5m, Volume = 12 },
            new MinuteLinePointEntity { Symbol = "sh600000", Date = new DateOnly(2026, 3, 13), Time = new TimeSpan(9, 30, 0), Price = 11, AveragePrice = 11, Volume = 11 });
        await dbContext.SaveChangesAsync();

        var result = await StockDetailCacheQueries.GetLatestMinuteLinesAsync(dbContext, "sh600000", 2);

        Assert.Equal(2, result.Count);
        Assert.All(result, item => Assert.Equal(new DateOnly(2026, 3, 13), item.Date));
        Assert.Collection(
            result,
            item => Assert.Equal(new TimeSpan(9, 30, 0), item.Time),
            item => Assert.Equal(new TimeSpan(9, 31, 0), item.Time));
    }

    private static AppDbContext CreateSqliteDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var dbContext = new AppDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }
}