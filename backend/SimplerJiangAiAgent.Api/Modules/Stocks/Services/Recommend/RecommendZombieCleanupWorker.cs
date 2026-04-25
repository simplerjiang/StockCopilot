using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend;

public sealed class RecommendZombieCleanupWorker : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ZombieThreshold = TimeSpan.FromMinutes(10);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecommendZombieCleanupWorker> _logger;

    public RecommendZombieCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<RecommendZombieCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ScanInterval, stoppingToken);
                await CleanupZombiesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RecommendZombieCleanupWorker scan failed");
            }
        }
    }

    private async Task CleanupZombiesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = DateTime.UtcNow - ZombieThreshold;

        var zombieTurns = await db.RecommendationTurns
            .Where(t => t.Status == RecommendTurnStatus.Running && t.StartedAt != null && t.StartedAt < cutoff)
            .ToListAsync(ct);

        foreach (var turn in zombieTurns)
        {
            turn.Status = RecommendTurnStatus.Failed;
            turn.CompletedAt = DateTime.UtcNow;
            _logger.LogWarning("Marked zombie recommend turn {TurnId} (session {SessionId}) as Failed after {Minutes}min",
                turn.Id, turn.SessionId, (DateTime.UtcNow - turn.StartedAt!.Value).TotalMinutes);
        }

        // 同时清理 Running 但所有 Turn 都已终态的 Session
        var zombieSessions = await db.RecommendationSessions
            .Where(s => s.Status == RecommendSessionStatus.Running)
            .Include(s => s.Turns)
            .Where(s => s.Turns.All(t => t.Status != RecommendTurnStatus.Running && t.Status != RecommendTurnStatus.Queued))
            .ToListAsync(ct);

        foreach (var session in zombieSessions)
        {
            var latestTurn = session.Turns.OrderByDescending(t => t.TurnIndex).FirstOrDefault();
            session.Status = latestTurn?.Status == RecommendTurnStatus.Completed
                ? RecommendSessionStatus.Completed
                : RecommendSessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            _logger.LogWarning("Marked zombie recommend session {SessionId} as {Status}", session.Id, session.Status);
        }

        if (zombieTurns.Count > 0 || zombieSessions.Count > 0)
            await db.SaveChangesAsync(ct);

        // Cascade: fail orphaned RoleStates where parent Turn is already terminal
        await CleanupOrphanedRoleStatesAsync(db, ct);
    }

    private async Task CleanupOrphanedRoleStatesAsync(AppDbContext db, CancellationToken ct)
    {
        var terminalTurnIds = await db.RecommendationTurns
            .Where(t => t.Status == RecommendTurnStatus.Failed
                     || t.Status == RecommendTurnStatus.Cancelled
                     || t.Status == RecommendTurnStatus.Completed)
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (terminalTurnIds.Count == 0) return;

        var stageIds = await db.RecommendationStageSnapshots
            .Where(ss => terminalTurnIds.Contains(ss.TurnId))
            .Select(ss => ss.Id)
            .ToListAsync(ct);

        if (stageIds.Count == 0) return;

        var orphanRoles = await db.RecommendationRoleStates
            .Where(rs => stageIds.Contains(rs.StageId)
                && (rs.Status == RecommendRoleStatus.Running || rs.Status == RecommendRoleStatus.Pending))
            .ToListAsync(ct);

        if (orphanRoles.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var role in orphanRoles)
        {
            role.Status = RecommendRoleStatus.Failed;
            role.CompletedAt ??= now;
        }

        await db.SaveChangesAsync(ct);
        _logger.LogWarning("Cleaned up {Count} orphaned recommend role states stuck in Running/Pending", orphanRoles.Count);
    }
}
