using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;

namespace SimplerJiangAiAgent.Api.Modules.Backtest;

public sealed class BacktestModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IBacktestService, BacktestService>();
        services.AddHostedService<BacktestWorker>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/backtest");

        // POST /api/backtest/run/{historyId}
        group.MapPost("/run/{historyId:long}", async (long historyId, IBacktestService svc, CancellationToken ct) =>
        {
            var result = await svc.RunAsync(historyId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("RunBacktest")
        .WithOpenApi();

        // POST /api/backtest/run-batch
        group.MapPost("/run-batch", async (BacktestBatchRequest req, IBacktestService svc, CancellationToken ct) =>
        {
            var dto = await svc.RunBatchAsync(req.Symbol, req.From, req.To, ct);
            return Results.Ok(dto);
        })
        .WithName("RunBacktestBatch")
        .WithOpenApi();

        // GET /api/backtest/stats
        group.MapGet("/stats", async (string? symbol, DateOnly? from, DateOnly? to,
            AppDbContext dbContext, CancellationToken ct) =>
        {
            var query = dbContext.BacktestResults.AsQueryable();
            if (!string.IsNullOrEmpty(symbol))
                query = query.Where(r => r.Symbol == symbol);
            if (from.HasValue)
                query = query.Where(r => r.AnalysisDate >= from.Value);
            if (to.HasValue)
                query = query.Where(r => r.AnalysisDate <= to.Value);

            var results = await query.ToListAsync(ct);
            var insufficientCount = results.Count(r => r.CalcStatus == "insufficient_data");
            var calculated = results.Where(r => r.CalcStatus == "calculated").ToList();

            var stats = new
            {
                totalAnalyses = results.Count,
                insufficientDataCount = insufficientCount,
                windows = BuildWindowStats(calculated),
                targetHitRate = ComputeRate(calculated, r => r.TargetHit),
                stopTriggerRate = ComputeRate(calculated, r => r.StopTriggered),
                byDirection = BuildDirectionStats(calculated)
            };

            return Results.Ok(stats);
        })
        .WithName("GetBacktestStats")
        .WithOpenApi();

        // GET /api/backtest/results
        group.MapGet("/results", async (string? symbol, string? status, int? page, int? size,
            AppDbContext dbContext, CancellationToken ct) =>
        {
            var query = dbContext.BacktestResults.AsQueryable();
            if (!string.IsNullOrEmpty(symbol))
                query = query.Where(r => r.Symbol == symbol);
            if (!string.IsNullOrEmpty(status))
                query = query.Where(r => r.CalcStatus == status);

            var pageNum = Math.Max(page ?? 1, 1);
            var pageSize = Math.Clamp(size ?? 20, 1, 100);

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(r => r.AnalysisDate)
                .Skip((pageNum - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return Results.Ok(new { total, page = pageNum, size = pageSize, items });
        })
        .WithName("GetBacktestResults")
        .WithOpenApi();
    }

    // ── Stats helpers ──

    private static Dictionary<string, object> BuildWindowStats(List<Data.Entities.BacktestResult> calculated)
    {
        return new Dictionary<string, object>
        {
            ["1d"] = WindowStat(calculated, r => r.IsCorrect1d),
            ["3d"] = WindowStat(calculated, r => r.IsCorrect3d),
            ["5d"] = WindowStat(calculated, r => r.IsCorrect5d),
            ["10d"] = WindowStat(calculated, r => r.IsCorrect10d),
        };
    }

    private static object WindowStat(List<Data.Entities.BacktestResult> items,
        Func<Data.Entities.BacktestResult, bool?> selector)
    {
        var evaluated = items.Where(r => selector(r).HasValue).ToList();
        var correct = evaluated.Count(r => selector(r) == true);
        var insufficientData = items.Count(r => !selector(r).HasValue);

        return new
        {
            count = evaluated.Count,
            accuracy = evaluated.Count > 0 ? Math.Round((double)correct / evaluated.Count * 100, 1) : 0.0,
            insufficientData
        };
    }

    private static double ComputeRate(List<Data.Entities.BacktestResult> items,
        Func<Data.Entities.BacktestResult, bool?> selector)
    {
        var evaluated = items.Where(r => selector(r).HasValue).ToList();
        if (evaluated.Count == 0) return 0.0;
        var hit = evaluated.Count(r => selector(r) == true);
        return Math.Round((double)hit / evaluated.Count * 100, 1);
    }

    private static Dictionary<string, object> BuildDirectionStats(List<Data.Entities.BacktestResult> calculated)
    {
        var directions = calculated
            .GroupBy(r => r.PredictedDirection)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var list = g.ToList();
                    return (object)new
                    {
                        count = list.Count,
                        accuracy1d = AccuracyOf(list, r => r.IsCorrect1d),
                        accuracy3d = AccuracyOf(list, r => r.IsCorrect3d),
                        accuracy5d = AccuracyOf(list, r => r.IsCorrect5d),
                        accuracy10d = AccuracyOf(list, r => r.IsCorrect10d),
                    };
                });

        // Ensure 看多/看空 always present
        directions.TryAdd("看多", new { count = 0, accuracy1d = 0.0, accuracy3d = 0.0, accuracy5d = 0.0, accuracy10d = 0.0 });
        directions.TryAdd("看空", new { count = 0, accuracy1d = 0.0, accuracy3d = 0.0, accuracy5d = 0.0, accuracy10d = 0.0 });

        return directions;
    }

    private static double AccuracyOf(List<Data.Entities.BacktestResult> items,
        Func<Data.Entities.BacktestResult, bool?> selector)
    {
        var evaluated = items.Where(r => selector(r).HasValue).ToList();
        if (evaluated.Count == 0) return 0.0;
        var correct = evaluated.Count(r => selector(r) == true);
        return Math.Round((double)correct / evaluated.Count * 100, 1);
    }
}

public record BacktestBatchRequest(string? Symbol = null, DateOnly? From = null, DateOnly? To = null);
