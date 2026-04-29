using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Backtest;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class BacktestServiceTests
{
    // ── Test 1: Bull prediction + price rose → correct ──
    [Fact]
    public async Task RunAsync_BullPrediction_PriceRose_IsCorrect()
    {
        await using var db = CreateDb();
        SeedHistory(db, 1, "sh600000", "浦发银行", BullCommanderJson());
        SeedKlines(db, "sh600000",
            (new DateTime(2026, 3, 10), 10m, 10m, 10.1m, 9.9m),   // base
            (new DateTime(2026, 3, 11), 10.3m, 10.3m, 10.5m, 10.1m)); // day+1 up
        await db.SaveChangesAsync();

        var svc = new BacktestService(db);
        var result = await svc.RunAsync(1);

        Assert.NotNull(result);
        Assert.Equal("calculated", result!.CalcStatus);
        Assert.Equal("看多", result.PredictedDirection);
        Assert.True(result.Window1dActual > 0);
        Assert.True(result.IsCorrect1d);
    }

    // ── Test 2: Bull prediction + price fell → incorrect ──
    [Fact]
    public async Task RunAsync_BullPrediction_PriceFell_IsIncorrect()
    {
        await using var db = CreateDb();
        SeedHistory(db, 1, "sh600000", "浦发银行", BullCommanderJson());
        SeedKlines(db, "sh600000",
            (new DateTime(2026, 3, 10), 10m, 10m, 10.1m, 9.9m),
            (new DateTime(2026, 3, 11), 9.5m, 9.5m, 9.8m, 9.4m)); // day+1 down
        await db.SaveChangesAsync();

        var svc = new BacktestService(db);
        var result = await svc.RunAsync(1);

        Assert.NotNull(result);
        Assert.True(result!.Window1dActual < 0);
        Assert.False(result.IsCorrect1d);
    }

    // ── Test 3: Neutral prediction + within ±2% → correct ──
    [Fact]
    public async Task RunAsync_NeutralPrediction_WithinRange_IsCorrect()
    {
        await using var db = CreateDb();
        SeedHistory(db, 1, "sh600000", "浦发银行", NeutralCommanderJson());
        SeedKlines(db, "sh600000",
            (new DateTime(2026, 3, 10), 10m, 10m, 10.1m, 9.9m),
            (new DateTime(2026, 3, 11), 10.1m, 10.1m, 10.2m, 10m)); // +1%
        await db.SaveChangesAsync();

        var svc = new BacktestService(db);
        var result = await svc.RunAsync(1);

        Assert.NotNull(result);
        Assert.Equal("中性", result!.PredictedDirection);
        Assert.True(Math.Abs(result.Window1dActual!.Value) <= 2m);
        Assert.True(result.IsCorrect1d);
    }

    // ── Test 4: No kline data → insufficient_data ──
    [Fact]
    public async Task RunAsync_NoKlines_ReturnsInsufficientData()
    {
        await using var db = CreateDb();
        SeedHistory(db, 1, "sh600000", "浦发银行", BullCommanderJson());
        await db.SaveChangesAsync();

        var svc = new BacktestService(db);
        var result = await svc.RunAsync(1);

        Assert.NotNull(result);
        Assert.Equal("insufficient_data", result!.CalcStatus);
    }

    // ── Test 5: Target price hit ──
    [Fact]
    public async Task RunAsync_TargetPriceHit()
    {
        await using var db = CreateDb();
        SeedHistory(db, 1, "sh600000", "浦发银行", BullWithTargetJson(targetPrice: 10.5m, stopLoss: 9.0m));
        SeedKlines(db, "sh600000",
            (new DateTime(2026, 3, 10), 10m, 10m, 10.1m, 9.9m),
            (new DateTime(2026, 3, 11), 10.2m, 10.3m, 10.6m, 10.1m)); // High=10.6 >= target 10.5
        await db.SaveChangesAsync();

        var svc = new BacktestService(db);
        var result = await svc.RunAsync(1);

        Assert.NotNull(result);
        Assert.True(result!.TargetHit);
        Assert.False(result.StopTriggered);
    }

    // ── Test 6: Stop loss triggered ──
    [Fact]
    public async Task RunAsync_StopLossTriggered()
    {
        await using var db = CreateDb();
        SeedHistory(db, 1, "sh600000", "浦发银行", BullWithTargetJson(targetPrice: 12m, stopLoss: 9.5m));
        SeedKlines(db, "sh600000",
            (new DateTime(2026, 3, 10), 10m, 10m, 10.1m, 9.9m),
            (new DateTime(2026, 3, 11), 9.6m, 9.4m, 9.7m, 9.3m)); // Low=9.3 <= stopLoss 9.5
        await db.SaveChangesAsync();

        var svc = new BacktestService(db);
        var result = await svc.RunAsync(1);

        Assert.NotNull(result);
        Assert.False(result!.TargetHit);
        Assert.True(result.StopTriggered);
    }

    // ── Test 7: Already calculated → skip re-calculation ──
    [Fact]
    public async Task RunAsync_AlreadyCalculated_SkipsRecalculation()
    {
        await using var db = CreateDb();
        SeedHistory(db, 1, "sh600000", "浦发银行", BullCommanderJson());
        db.BacktestResults.Add(new BacktestResult
        {
            AnalysisHistoryId = 1,
            Symbol = "sh600000",
            Name = "浦发银行",
            AnalysisDate = new DateOnly(2026, 3, 10),
            PredictedDirection = "看多",
            Confidence = 72,
            CalcStatus = "calculated",
            Window1dActual = 3m,
            IsCorrect1d = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = new BacktestService(db);
        var result = await svc.RunAsync(1);

        Assert.NotNull(result);
        Assert.Equal("calculated", result!.CalcStatus);
        Assert.Equal(3m, result.Window1dActual); // original value preserved
    }

    // ── Helpers ──

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    private static void SeedHistory(AppDbContext db, long id, string symbol, string name, string resultJson)
    {
        db.StockAgentAnalysisHistories.Add(new StockAgentAnalysisHistory
        {
            Id = id,
            Symbol = symbol,
            Name = name,
            Interval = "day",
            ResultJson = resultJson,
            CreatedAt = new DateTime(2026, 3, 10, 10, 0, 0)
        });
    }

    private static void SeedKlines(AppDbContext db, string symbol,
        params (DateTime Date, decimal Open, decimal Close, decimal High, decimal Low)[] bars)
    {
        foreach (var (date, open, close, high, low) in bars)
        {
            db.KLinePoints.Add(new KLinePointEntity
            {
                Symbol = symbol,
                Interval = "day",
                Date = date,
                Open = open,
                Close = close,
                High = high,
                Low = low,
                Volume = 1000
            });
        }
    }

    private static string BullCommanderJson() => """
        {
          "agents": [
            {
              "agentId": "commander",
              "data": {
                "agent": "commander",
                "summary": "偏多",
                "directional_bias": "看多",
                "confidence_score": 72,
                "probabilities": { "bull": 62, "base": 23, "bear": 15 }
              }
            }
          ]
        }
        """;

    private static string NeutralCommanderJson() => """
        {
          "agents": [
            {
              "agentId": "commander",
              "data": {
                "agent": "commander",
                "summary": "中性",
                "directional_bias": "中性",
                "confidence_score": 50,
                "probabilities": { "bull": 33, "base": 34, "bear": 33 }
              }
            }
          ]
        }
        """;

    private static string BullWithTargetJson(decimal targetPrice, decimal stopLoss) => $$"""
        {
          "agents": [
            {
              "agentId": "commander",
              "data": {
                "agent": "commander",
                "summary": "偏多",
                "directional_bias": "看多",
                "confidence_score": 72,
                "probabilities": { "bull": 62, "base": 23, "bear": 15 },
                "targetPrice": {{targetPrice}},
                "stopLossPrice": {{stopLoss}}
              }
            }
          ]
        }
        """;
}
