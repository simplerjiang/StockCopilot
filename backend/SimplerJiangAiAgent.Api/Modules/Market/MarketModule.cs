using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Market.Services;
using SimplerJiangAiAgent.Api.Modules;

namespace SimplerJiangAiAgent.Api.Modules.Market;

public sealed class MarketModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<IEastmoneySectorRotationClient, EastmoneySectorRotationClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Referer", "https://quote.eastmoney.com/");
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            UseProxy = false,
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = CreateRelaxedSslCallback("SectorRotation")
            }
        });

        services.AddHttpClient<IEastmoneyRealtimeMarketClient, EastmoneyRealtimeMarketClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Referer", "https://quote.eastmoney.com/");
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            UseProxy = false,
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = CreateRelaxedSslCallback("RealtimeMarket")
            }
        });
        services.Configure<SectorRotationOptions>(configuration.GetSection(SectorRotationOptions.SectionName));
        services.AddScoped<ISectorRotationIngestionService, SectorRotationIngestionService>();
        services.AddScoped<ISectorRotationQueryService, SectorRotationQueryService>();
        services.AddScoped<IRealtimeMarketOverviewService, RealtimeMarketOverviewService>();
        services.AddScoped<IRealtimeSectorBoardService, RealtimeSectorBoardService>();
        services.AddHostedService<SectorRotationWorker>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/market");

        group.MapGet("/sentiment/latest", async (ISectorRotationIngestionService ingestionService, ISectorRotationQueryService queryService) =>
        {
            var summary = await queryService.GetLatestSummaryAsync();
            if (summary is null)
            {
                await ingestionService.SyncAsync();
                summary = await queryService.GetLatestSummaryAsync();
            }

            return summary is null ? Results.NotFound() : Results.Ok(summary);
        })
        .WithName("GetLatestMarketSentiment")
        .WithOpenApi();

        group.MapGet("/sentiment/history", async (int? days, ISectorRotationIngestionService ingestionService, ISectorRotationQueryService queryService) =>
        {
            var history = await queryService.GetHistoryAsync(Math.Clamp(days ?? 20, 1, 60));
            if (history.Count == 0)
            {
                await ingestionService.SyncAsync();
                history = await queryService.GetHistoryAsync(Math.Clamp(days ?? 20, 1, 60));
            }

            return Results.Ok(history);
        })
        .WithName("GetMarketSentimentHistory")
        .WithOpenApi();

        group.MapGet("/sectors", async (string? boardType, int? page, int? pageSize, string? sort, ISectorRotationIngestionService ingestionService, ISectorRotationQueryService queryService) =>
        {
            var payload = await queryService.GetSectorPageAsync(boardType ?? SectorBoardTypes.Concept, page ?? 1, pageSize ?? 20, sort ?? "strength");
            if (payload.Total == 0)
            {
                await ingestionService.SyncAsync();
                payload = await queryService.GetSectorPageAsync(boardType ?? SectorBoardTypes.Concept, page ?? 1, pageSize ?? 20, sort ?? "strength");
            }

            return Results.Ok(payload);
        })
        .WithName("GetSectorRotationPage")
        .WithOpenApi();

        group.MapGet("/sectors/realtime", async (string? boardType, int? take, string? sort, IRealtimeSectorBoardService realtimeSectorBoardService, HttpContext httpContext) =>
        {
            var payload = await realtimeSectorBoardService.GetPageAsync(boardType ?? SectorBoardTypes.Concept, take ?? 60, sort, httpContext.RequestAborted);
            return Results.Ok(payload);
        })
        .WithName("GetRealtimeSectorBoardPage")
        .WithOpenApi();

        group.MapGet("/sectors/{sectorCode}", async (string sectorCode, string? boardType, string? window, ISectorRotationIngestionService ingestionService, ISectorRotationQueryService queryService) =>
        {
            var detail = await queryService.GetSectorDetailAsync(sectorCode, boardType ?? SectorBoardTypes.Concept, window ?? "10d");
            if (detail is null)
            {
                await ingestionService.SyncAsync();
                detail = await queryService.GetSectorDetailAsync(sectorCode, boardType ?? SectorBoardTypes.Concept, window ?? "10d");
            }

            return detail is null ? Results.NotFound() : Results.Ok(detail);
        })
        .WithName("GetSectorRotationDetail")
        .WithOpenApi();

        group.MapGet("/sectors/{sectorCode}/trend", async (string sectorCode, string? boardType, string? window, ISectorRotationIngestionService ingestionService, ISectorRotationQueryService queryService) =>
        {
            var trend = await queryService.GetSectorTrendAsync(sectorCode, boardType ?? SectorBoardTypes.Concept, window ?? "10d");
            if (trend is null)
            {
                await ingestionService.SyncAsync();
                trend = await queryService.GetSectorTrendAsync(sectorCode, boardType ?? SectorBoardTypes.Concept, window ?? "10d");
            }

            return trend is null ? Results.NotFound() : Results.Ok(trend);
        })
        .WithName("GetSectorRotationTrend")
        .WithOpenApi();

        group.MapGet("/sectors/{sectorCode}/leaders", async (string sectorCode, string? boardType, int? take, ISectorRotationIngestionService ingestionService, ISectorRotationQueryService queryService) =>
        {
            var leaders = await queryService.GetLeadersAsync(sectorCode, boardType ?? SectorBoardTypes.Concept, Math.Clamp(take ?? 10, 1, 20));
            if (leaders.Count == 0)
            {
                await ingestionService.SyncAsync();
                leaders = await queryService.GetLeadersAsync(sectorCode, boardType ?? SectorBoardTypes.Concept, Math.Clamp(take ?? 10, 1, 20));
            }

            return Results.Ok(leaders);
        })
        .WithName("GetSectorRotationLeaders")
        .WithOpenApi();

        group.MapGet("/mainline", async (string? boardType, string? window, int? take, ISectorRotationIngestionService ingestionService, ISectorRotationQueryService queryService) =>
        {
            var items = await queryService.GetMainlineAsync(boardType ?? SectorBoardTypes.Concept, window ?? "10d", take ?? 6);
            if (items.Count == 0)
            {
                await ingestionService.SyncAsync();
                items = await queryService.GetMainlineAsync(boardType ?? SectorBoardTypes.Concept, window ?? "10d", take ?? 6);
            }

            return Results.Ok(items);
        })
        .WithName("GetMainlineSectors")
        .WithOpenApi();

        group.MapGet("/realtime/overview", async (string? symbols, IRealtimeMarketOverviewService realtimeService, HttpContext httpContext) =>
        {
            var requestedSymbols = ParseSymbols(symbols);
            var payload = await realtimeService.GetOverviewAsync(requestedSymbols, httpContext.RequestAborted);
            return Results.Ok(payload);
        })
        .WithName("GetRealtimeMarketOverview")
        .WithOpenApi();

        group.MapGet("/health", async (AppDbContext dbContext, CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;

            var latestSentiment = await dbContext.MarketSentimentSnapshots
                .AsNoTracking()
                .OrderByDescending(x => x.SnapshotTime)
                .Select(x => new { x.SnapshotTime, x.SourceTag, x.StageLabel, x.StageScore })
                .FirstOrDefaultAsync(ct);

            var latestValidSentiment = await dbContext.MarketSentimentSnapshots
                .AsNoTracking()
                .Where(x => x.StageScore > 0)
                .OrderByDescending(x => x.SnapshotTime)
                .Select(x => new { x.SnapshotTime, x.StageLabel, x.StageScore })
                .FirstOrDefaultAsync(ct);

            var boardTypes = new[] { "concept", "industry", "style" };
            var sectorStatuses = new Dictionary<string, object>();
            foreach (var bt in boardTypes)
            {
                var latestSector = await dbContext.SectorRotationSnapshots
                    .AsNoTracking()
                    .Where(x => x.BoardType == bt)
                    .OrderByDescending(x => x.SnapshotTime)
                    .Select(x => new { x.SnapshotTime, x.SectorName, x.StrengthScore })
                    .FirstOrDefaultAsync(ct);

                sectorStatuses[bt] = new
                {
                    lastSnapshotTime = latestSector?.SnapshotTime,
                    ageHours = latestSector != null ? Math.Round((now - latestSector.SnapshotTime).TotalHours, 1) : (double?)null,
                    sampleSector = latestSector?.SectorName
                };
            }

            var sentimentAge = latestSentiment != null ? Math.Round((now - latestSentiment.SnapshotTime).TotalHours, 1) : (double?)null;
            var validSentimentAge = latestValidSentiment != null ? Math.Round((now - latestValidSentiment.SnapshotTime).TotalHours, 1) : (double?)null;

            return Results.Ok(new
            {
                checkedAt = now,
                sentiment = new
                {
                    lastSnapshotTime = latestSentiment?.SnapshotTime,
                    ageHours = sentimentAge,
                    stageLabel = latestSentiment?.StageScore == 0 ? "同步不完整" : latestSentiment?.StageLabel,
                    stageScore = latestSentiment?.StageScore,
                    isStale = sentimentAge > 24
                },
                lastValidSentiment = new
                {
                    lastSnapshotTime = latestValidSentiment?.SnapshotTime,
                    ageHours = validSentimentAge,
                    stageLabel = latestValidSentiment?.StageLabel,
                    stageScore = latestValidSentiment?.StageScore
                },
                sectors = sectorStatuses,
                overall = sentimentAge.HasValue && sentimentAge <= 4 ? "healthy"
                        : sentimentAge.HasValue && sentimentAge <= 24 ? "degraded"
                        : "unhealthy"
            });
        })
        .WithName("GetMarketHealth")
        .WithDescription("数据源健康状态");

        group.MapPost("/sync", async (ISectorRotationIngestionService ingestionService, HttpContext httpContext) =>
        {
            await ingestionService.SyncAsync(httpContext.RequestAborted);
            return Results.Ok(new { synced = true, timestamp = DateTimeOffset.UtcNow });
        })
        .WithName("TriggerMarketSync")
        .WithOpenApi()
        .RequireRateLimiting("MarketSync");

        group.MapGet("/audit", () =>
        {
            static string DescribeDegradationSource(string code)
            {
                return code switch
                {
                    "market_breadth_unavailable" => "市场涨跌与涨跌停统计不可用（market breadth source unavailable）",
                    "market_turnover_unavailable" => "市场总成交额不可用（market turnover source unavailable）",
                    "limit_up_unavailable" => "涨停家数统计不可用",
                    "limit_down_unavailable" => "跌停家数统计不可用",
                    "broken_board_unavailable" => "炸板统计不可用",
                    "max_streak_unavailable" => "连板高度统计不可用",
                    "sector_rankings_unavailable" => "板块排行总源不可用",
                    "sector_rankings_concept_unavailable" => "概念板块排行源不可用",
                    "sector_rankings_industry_unavailable" => "行业板块排行源不可用",
                    "sector_rankings_style_unavailable" => "风格板块排行源不可用",
                    "sync_incomplete" => "本次同步为部分完成（partial sync）",
                    _ => string.IsNullOrWhiteSpace(code) ? "未知降级项" : $"未登记的降级代码：{code}"
                };
            }

            static IReadOnlyDictionary<string, string> BuildReasons(IReadOnlyList<string>? degradedSources)
            {
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (degradedSources is null)
                {
                    return result;
                }

                foreach (var code in degradedSources)
                {
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        continue;
                    }

                    result[code] = DescribeDegradationSource(code);
                }

                return result;
            }

            var sources = DataSourceTracker.GetAll().Select(s => new
            {
                name = s.Name,
                status = s.Status,
                lastSuccess = s.LastSuccess,
                lastError = s.LastError,
                consecutiveFailures = s.ConsecutiveFailures,
                avgLatencyMs = s.AvgLatencyMs
            });

            var bySource = DataSourceTracker.GetBoardStats().Select(s => new
            {
                boardType = s.BoardType,
                totalAttempts = s.TotalAttempts,
                successCount = s.SuccessCount,
                emptyCount = s.EmptyCount,
                successRate = Math.Round(s.SuccessRate, 4),
                emptyRate = Math.Round(s.EmptyRate, 4),
                consecutiveFailureCount = s.ConsecutiveFailureCount,
                lastSuccessAt = s.LastSuccessAt,
                lastUpdated = s.LastUpdated
            });

            var recentSyncs = DataSourceTracker.GetRecentSyncs().Select(s => new
            {
                timestamp = s.Timestamp,
                durationMs = s.DurationMs,
                tradingDate = s.TradingDate,
                wasComplete = s.WasComplete,
                degradedSources = s.DegradedSources,
                reasons = BuildReasons(s.DegradedSources),
                sectorRowCount = s.SectorRowCount,
                totalTurnover = s.TotalTurnover
            });

            var computation = DataSourceTracker.LastComputation;

            return Results.Ok(new
            {
                sources,
                bySource,
                recentSyncs,
                algorithm = new
                {
                    stageScore = "breadthWeight(0.35) × breadthSignal + momentumWeight(0.25) × momentum + volumeWeight(0.20) × volume + limitWeight(0.20) × limitSignal",
                    diffusionScore = "marketBreadth(0.55) × breadth + sectorDiffusion(0.45) × avgDiffusionRate",
                    continuationScore = "strength5d(0.40) + strength10d(0.35) + mainline(0.25)",
                    stageLabel = "基于 stageScore 区间映射: [0-28]=退潮, [28-52]=混沌, [52-70]=分歧, [70-100]=主升"
                },
                lastComputation = computation is not null ? (object)new
                {
                    timestamp = computation.Timestamp,
                    durationMs = computation.DurationMs,
                    tradingDate = computation.TradingDate,
                    wasComplete = computation.WasComplete,
                    degradedSources = computation.DegradedSources,
                    reasons = BuildReasons(computation.DegradedSources)
                } : null,
                checkedAt = DateTimeOffset.UtcNow
            });
        })
        .WithName("GetMarketAudit")
        .WithDescription("数据源审计与算法说明");
    }

    private static IReadOnlyList<string> ParseSymbols(string? symbols)
    {
        if (string.IsNullOrWhiteSpace(symbols))
        {
            return Array.Empty<string>();
        }

        return symbols
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(20)
            .ToArray();
    }

    private static System.Net.Security.RemoteCertificateValidationCallback CreateRelaxedSslCallback(string _) =>
        (_, _, _, _) => true;
}

internal static class DataSourceTracker
{
    public static IReadOnlyList<DataSourceStatusSnapshot> GetAll() => Array.Empty<DataSourceStatusSnapshot>();

    public static IReadOnlyList<BoardStatsSnapshot> GetBoardStats() => Array.Empty<BoardStatsSnapshot>();

    public static IReadOnlyList<RecentSyncSnapshot> GetRecentSyncs() => Array.Empty<RecentSyncSnapshot>();

    public static ComputationSnapshot? LastComputation => null;
}

internal sealed record DataSourceStatusSnapshot(
    string Name,
    string Status,
    DateTimeOffset? LastSuccess,
    string? LastError,
    int ConsecutiveFailures,
    double AvgLatencyMs);

internal sealed record BoardStatsSnapshot(
    string BoardType,
    int TotalAttempts,
    int SuccessCount,
    int EmptyCount,
    double SuccessRate,
    double EmptyRate,
    int ConsecutiveFailureCount,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastUpdated);

internal sealed record RecentSyncSnapshot(
    DateTimeOffset Timestamp,
    long DurationMs,
    DateTimeOffset TradingDate,
    bool WasComplete,
    IReadOnlyList<string> DegradedSources,
    int SectorRowCount,
    decimal TotalTurnover);

internal sealed record ComputationSnapshot(
    DateTimeOffset Timestamp,
    long DurationMs,
    DateTimeOffset TradingDate,
    bool WasComplete,
    IReadOnlyList<string> DegradedSources);
