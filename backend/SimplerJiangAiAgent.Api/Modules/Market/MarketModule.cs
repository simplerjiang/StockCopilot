using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Market.Services;
using SimplerJiangAiAgent.Api.Modules;

namespace SimplerJiangAiAgent.Api.Modules.Market;

public sealed class MarketModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<IEastmoneySectorRotationClient, EastmoneySectorRotationClient>();
        services.Configure<SectorRotationOptions>(configuration.GetSection(SectorRotationOptions.SectionName));
        services.AddScoped<ISectorRotationIngestionService, SectorRotationIngestionService>();
        services.AddScoped<ISectorRotationQueryService, SectorRotationQueryService>();
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
    }
}
