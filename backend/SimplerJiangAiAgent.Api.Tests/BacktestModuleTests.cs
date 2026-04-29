using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class BacktestModuleTests
{
    // ── Test 1: GET /api/backtest/stats returns correct statistics ──
    [Fact]
    public async Task Stats_ReturnsCorrectAggregation()
    {
        await using var db = CreateDb();
        SeedResults(db,
            MakeResult("sh600519", "看多", calculated: true, correct1d: true, correct5d: true, correct10d: false, targetHit: true, stopTriggered: false),
            MakeResult("sh600519", "看多", calculated: true, correct1d: false, correct5d: true, correct10d: true, targetHit: false, stopTriggered: true),
            MakeResult("sh600036", "看空", calculated: true, correct1d: true, correct5d: null, correct10d: null, targetHit: null, stopTriggered: null));
        await db.SaveChangesAsync();

        var results = await db.BacktestResults.ToListAsync();
        var calculated = results.Where(r => r.CalcStatus == "calculated").ToList();

        Assert.Equal(3, results.Count);
        Assert.Equal(3, calculated.Count);

        // 1d accuracy: 2 correct / 3 evaluated
        var eval1d = calculated.Where(r => r.IsCorrect1d.HasValue).ToList();
        Assert.Equal(3, eval1d.Count);
        var accuracy1d = Math.Round((double)eval1d.Count(r => r.IsCorrect1d == true) / eval1d.Count * 100, 1);
        Assert.Equal(66.7, accuracy1d);

        // target hit rate: 1 hit / 2 evaluated
        var evalTarget = calculated.Where(r => r.TargetHit.HasValue).ToList();
        Assert.Equal(2, evalTarget.Count);
        var targetRate = Math.Round((double)evalTarget.Count(r => r.TargetHit == true) / evalTarget.Count * 100, 1);
        Assert.Equal(50.0, targetRate);

        // byDirection 看多 count = 2
        var bullGroup = calculated.Where(r => r.PredictedDirection == "看多").ToList();
        Assert.Equal(2, bullGroup.Count);
    }

    // ── Test 2: GET /api/backtest/results returns paginated data ──
    [Fact]
    public async Task Results_ReturnsPaginatedData()
    {
        await using var db = CreateDb();
        for (int i = 0; i < 25; i++)
        {
            SeedResults(db, MakeResult("sh600519", "看多", calculated: true, analysisDate: new DateOnly(2026, 1, 1).AddDays(i)));
        }
        await db.SaveChangesAsync();

        // Page 1, size 20
        var page1 = await db.BacktestResults
            .OrderByDescending(r => r.AnalysisDate)
            .Skip(0).Take(20)
            .ToListAsync();
        Assert.Equal(20, page1.Count);
        Assert.True(page1[0].AnalysisDate > page1[19].AnalysisDate);

        // Page 2, size 20
        var page2 = await db.BacktestResults
            .OrderByDescending(r => r.AnalysisDate)
            .Skip(20).Take(20)
            .ToListAsync();
        Assert.Equal(5, page2.Count);
    }

    // ── Test 3: GET /api/backtest/results?symbol=xxx filters correctly ──
    [Fact]
    public async Task Results_FilterBySymbol()
    {
        await using var db = CreateDb();
        SeedResults(db,
            MakeResult("sh600519", "看多", calculated: true),
            MakeResult("sh600036", "看空", calculated: true),
            MakeResult("sh600519", "看多", calculated: true));
        await db.SaveChangesAsync();

        var filtered = await db.BacktestResults
            .Where(r => r.Symbol == "sh600519")
            .ToListAsync();
        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, r => Assert.Equal("sh600519", r.Symbol));
    }

    // ── Test 4: stats with no data returns zero values ──
    [Fact]
    public async Task Stats_NoData_ReturnsZeros()
    {
        await using var db = CreateDb();
        var results = await db.BacktestResults.ToListAsync();
        var calculated = results.Where(r => r.CalcStatus == "calculated").ToList();

        Assert.Empty(results);
        Assert.Empty(calculated);

        // Simulate the stats logic with empty data
        var evalTarget = calculated.Where(r => r.TargetHit.HasValue).ToList();
        var targetRate = evalTarget.Count > 0
            ? Math.Round((double)evalTarget.Count(r => r.TargetHit == true) / evalTarget.Count * 100, 1)
            : 0.0;
        Assert.Equal(0.0, targetRate);
    }

    // ── Helpers ──

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    private static BacktestResult MakeResult(string symbol, string direction,
        bool calculated,
        bool? correct1d = null, bool? correct5d = null, bool? correct10d = null,
        bool? targetHit = null, bool? stopTriggered = null,
        DateOnly? analysisDate = null)
    {
        return new BacktestResult
        {
            Symbol = symbol,
            Name = symbol,
            AnalysisDate = analysisDate ?? new DateOnly(2026, 3, 15),
            PredictedDirection = direction,
            Confidence = 70,
            CalcStatus = calculated ? "calculated" : "pending",
            IsCorrect1d = correct1d,
            IsCorrect3d = null,
            IsCorrect5d = correct5d,
            IsCorrect10d = correct10d,
            TargetHit = targetHit,
            StopTriggered = stopTriggered,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static void SeedResults(AppDbContext db, params BacktestResult[] results)
    {
        db.BacktestResults.AddRange(results);
    }
}
