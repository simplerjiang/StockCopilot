using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SimplerJiangAiAgent.Api.Infrastructure;

namespace SimplerJiangAiAgent.Api.Modules.GpuQueue;

public sealed class GpuQueueModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        // GpuTaskQueueService is registered elsewhere as a singleton; no extra services needed.
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/gpu-queue");

        group.MapGet("/status", (IGpuTaskQueue queue) =>
        {
            var snapshot = queue.GetSnapshot();
            return Results.Ok(snapshot);
        })
        .WithName("GetGpuQueueStatus")
        .WithOpenApi();

        group.MapGet("/history", (int? count, IGpuTaskQueue queue) =>
        {
            var history = queue.GetHistory(count ?? 50);
            return Results.Ok(history);
        })
        .WithName("GetGpuQueueHistory")
        .WithOpenApi();

        group.MapPost("/pause", (IGpuTaskQueue queue) =>
        {
            queue.Pause();
            return Results.Ok(new { paused = true });
        })
        .WithName("PauseGpuQueue");

        group.MapPost("/resume", (IGpuTaskQueue queue) =>
        {
            queue.Resume();
            return Results.Ok(new { paused = false });
        })
        .WithName("ResumeGpuQueue");

        group.MapPost("/cancel-current", (IGpuTaskQueue queue) =>
        {
            var cancelled = queue.CancelCurrent();
            return Results.Ok(new { cancelled });
        })
        .WithName("CancelCurrentGpuTask");
    }
}
