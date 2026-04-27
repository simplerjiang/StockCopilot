using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

// V048-S1 #90: 0 笔交易时，PlanExecutionRate 与 DisciplineScore 必须为 null（前端显示 N/A）
public sealed class TradingBehaviorServiceTests
{
    [Fact]
    public async Task GetBehaviorStatsAsync_WithNoTrades_ShouldReturnNullForRateAndScore()
    {
        await using var db = CreateDbContext();
        var stats = await new TradingBehaviorService(db).GetBehaviorStatsAsync();

        Assert.Equal(0, stats.Trades30Days);
        Assert.Null(stats.PlanExecutionRate);
        Assert.Null(stats.DisciplineScore);
    }

    [Fact]
    public async Task GetBehaviorStatsAsync_WithTrades_ShouldReturnConcreteRateAndScore()
    {
        await using var db = CreateDbContext();
        db.TradeExecutions.AddRange(
            new TradeExecution
            {
                Symbol = "sh600000", Name = "浦发银行",
                Direction = TradeDirection.Buy, TradeType = TradeType.Normal,
                ExecutedPrice = 10m, Quantity = 100,
                ExecutedAt = DateTime.UtcNow.AddDays(-1), CreatedAt = DateTime.UtcNow.AddDays(-1),
                PlanId = 1L,
                ComplianceTag = ComplianceTag.FollowedPlan
            },
            new TradeExecution
            {
                Symbol = "sh600000", Name = "浦发银行",
                Direction = TradeDirection.Sell, TradeType = TradeType.Normal,
                ExecutedPrice = 12m, Quantity = 100,
                ExecutedAt = DateTime.UtcNow.AddHours(-2), CreatedAt = DateTime.UtcNow.AddHours(-2),
                PlanId = 1L,
                RealizedPnL = 200m,
                ComplianceTag = ComplianceTag.FollowedPlan
            });
        await db.SaveChangesAsync();

        var stats = await new TradingBehaviorService(db).GetBehaviorStatsAsync();

        Assert.Equal(2, stats.Trades30Days);
        Assert.NotNull(stats.PlanExecutionRate);
        Assert.Equal(1m, stats.PlanExecutionRate);
        Assert.NotNull(stats.DisciplineScore);
        // Score must be a real number, not null sentinel; exact value depends on over-trading heuristic
        Assert.InRange(stats.DisciplineScore!.Value, 0, 100);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }
}
