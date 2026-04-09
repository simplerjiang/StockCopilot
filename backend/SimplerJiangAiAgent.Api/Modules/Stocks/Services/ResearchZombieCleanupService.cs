using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public sealed record ResearchZombieCleanupResult(
    int SessionCount,
    int TurnCount,
    int StageCount,
    int RoleCount)
{
    public static ResearchZombieCleanupResult None { get; } = new(0, 0, 0, 0);

    public bool HasChanges => SessionCount > 0 || TurnCount > 0 || StageCount > 0 || RoleCount > 0;
}

public sealed class ResearchZombieCleanupService
{
    public static readonly TimeSpan QueryStaleThreshold = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan BackgroundStaleThreshold = TimeSpan.FromMinutes(10);

    private const string TurnStopReason = "Recovered stale running turn after interrupted execution.";
    private const string StageStopReason = "Recovered stale running stage after interrupted execution.";
    private const string RoleErrorCode = "STALE_RUNNING_RECOVERED";
    private const string RoleErrorMessage = "Recovered stale running role after interrupted execution.";

    private readonly AppDbContext _dbContext;
    private readonly ILogger<ResearchZombieCleanupService> _logger;

    public ResearchZombieCleanupService(AppDbContext dbContext, ILogger<ResearchZombieCleanupService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ResearchZombieCleanupResult> CleanupStaleRunningAsync(
        TimeSpan staleThreshold,
        string? symbol = null,
        long? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - staleThreshold;

        IQueryable<ResearchSession> query = _dbContext.ResearchSessions;
        if (sessionId.HasValue)
        {
            query = query.Where(session => session.Id == sessionId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(symbol))
        {
            query = query.Where(session => session.Symbol == symbol);
        }

        var staleSessions = await query
            .Where(session =>
                (session.Status == ResearchSessionStatus.Running && session.UpdatedAt < cutoff) ||
                (session.Status != ResearchSessionStatus.Running &&
                    session.Turns.Any(turn =>
                        (turn.Status == ResearchTurnStatus.Running && (turn.StartedAt ?? turn.RequestedAt) < cutoff) ||
                        turn.StageSnapshots.Any(stage =>
                            (stage.Status == ResearchStageStatus.Running && (stage.StartedAt ?? turn.StartedAt ?? turn.RequestedAt) < cutoff) ||
                            stage.RoleStates.Any(role =>
                                role.Status == ResearchRoleStatus.Running &&
                                (role.StartedAt ?? stage.StartedAt ?? turn.StartedAt ?? turn.RequestedAt) < cutoff)))))
            .Include(session => session.Turns)
                .ThenInclude(turn => turn.StageSnapshots)
                    .ThenInclude(stage => stage.RoleStates)
            .ToListAsync(cancellationToken);

        if (staleSessions.Count == 0)
        {
            return ResearchZombieCleanupResult.None;
        }

        var recoveredSessionCount = 0;
        var recoveredTurnCount = 0;
        var recoveredStageCount = 0;
        var recoveredRoleCount = 0;

        foreach (var session in staleSessions)
        {
            var recoveredSessionTree = false;
            var sessionAgeMinutes = GetElapsedMinutes(session.UpdatedAt, now);

            foreach (var turn in session.Turns.Where(TurnNeedsRecovery))
            {
                recoveredSessionTree = true;

                if (RecoverTurn(turn, now, ref recoveredStageCount, ref recoveredRoleCount))
                {
                    recoveredTurnCount++;
                }

                _logger.LogWarning(
                    "Recovered stale research turn tree {TurnId} in session {SessionId} after {Minutes:F1} minutes",
                    turn.Id,
                    session.Id,
                    GetElapsedMinutes(turn.StartedAt ?? turn.RequestedAt, now));
            }

            if (RecoverSession(session, now, recoveredSessionTree))
            {
                recoveredSessionCount++;

                _logger.LogWarning(
                    "Recovered stale research session tree {SessionId} as {Status} after {Minutes:F1} minutes",
                    session.Id,
                    session.Status,
                    sessionAgeMinutes);
            }
            else if (recoveredSessionTree)
            {
                _logger.LogWarning(
                    "Recovered stale research descendants in session {SessionId} while preserving status {Status}",
                    session.Id,
                    session.Status);
            }
        }

        var result = new ResearchZombieCleanupResult(
            recoveredSessionCount,
            recoveredTurnCount,
            recoveredStageCount,
            recoveredRoleCount);

        if (!result.HasChanges)
        {
            return ResearchZombieCleanupResult.None;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Recovered stale research state: {SessionCount} sessions, {TurnCount} turns, {StageCount} stages, {RoleCount} roles",
            result.SessionCount,
            result.TurnCount,
            result.StageCount,
            result.RoleCount);

        return result;
    }

    private static bool RecoverTurn(ResearchTurn turn, DateTime now, ref int recoveredStageCount, ref int recoveredRoleCount)
    {
        var turnChanged = false;
        var turnNeedsRecovery = turn.Status == ResearchTurnStatus.Running || turn.StageSnapshots.Any(StageNeedsRecovery);

        if (turnNeedsRecovery)
        {
            if (turn.Status != ResearchTurnStatus.Failed)
            {
                turn.Status = ResearchTurnStatus.Failed;
                turnChanged = true;
            }

            if (!turn.CompletedAt.HasValue)
            {
                turn.CompletedAt = now;
                turnChanged = true;
            }

            if (string.IsNullOrWhiteSpace(turn.StopReason))
            {
                turn.StopReason = TurnStopReason;
                turnChanged = true;
            }
        }

        foreach (var stage in turn.StageSnapshots)
        {
            if (RecoverStage(stage, now, ref recoveredRoleCount))
            {
                recoveredStageCount++;
                turnChanged = true;
            }
        }

        return turnChanged;
    }

    private static bool RecoverStage(ResearchStageSnapshot stage, DateTime now, ref int recoveredRoleCount)
    {
        var stageChanged = false;
        var stageNeedsRecovery = StageNeedsRecovery(stage);

        if (stageNeedsRecovery)
        {
            if (stage.Status != ResearchStageStatus.Failed)
            {
                stage.Status = ResearchStageStatus.Failed;
                stageChanged = true;
            }

            if (!stage.CompletedAt.HasValue)
            {
                stage.CompletedAt = now;
                stageChanged = true;
            }

            if (string.IsNullOrWhiteSpace(stage.StopReason))
            {
                stage.StopReason = StageStopReason;
                stageChanged = true;
            }

            if (string.IsNullOrWhiteSpace(stage.Summary))
            {
                stage.Summary = "Recovered after interrupted execution.";
                stageChanged = true;
            }
        }

        foreach (var role in stage.RoleStates.Where(role => role.Status == ResearchRoleStatus.Running))
        {
            role.Status = ResearchRoleStatus.Failed;
            role.CompletedAt ??= now;
            role.ErrorCode ??= RoleErrorCode;
            role.ErrorMessage ??= RoleErrorMessage;
            recoveredRoleCount++;
        }

        return stageChanged;
    }

    private static bool RecoverSession(ResearchSession session, DateTime now, bool recoveredSessionTree)
    {
        var originalStatus = session.Status;

        if (!recoveredSessionTree && originalStatus != ResearchSessionStatus.Running)
        {
            return false;
        }

        var sessionChanged = false;

        if (originalStatus == ResearchSessionStatus.Running || recoveredSessionTree && originalStatus != ResearchSessionStatus.Closed)
        {
            var recoveredStatus = ResolveRecoveredSessionStatus(session);
            if (session.Status != recoveredStatus)
            {
                session.Status = recoveredStatus;
                sessionChanged = true;
            }
        }

        if (session.ActiveTurnId.HasValue)
        {
            session.ActiveTurnId = null;
            sessionChanged = true;
        }

        if (!string.IsNullOrWhiteSpace(session.ActiveStage))
        {
            session.ActiveStage = null;
            sessionChanged = true;
        }

        if (originalStatus == ResearchSessionStatus.Running || session.Status != originalStatus)
        {
            session.UpdatedAt = now;
            sessionChanged = true;
        }

        return sessionChanged;
    }

    private static bool TurnNeedsRecovery(ResearchTurn turn)
    {
        return turn.Status == ResearchTurnStatus.Running || turn.StageSnapshots.Any(StageNeedsRecovery);
    }

    private static bool StageNeedsRecovery(ResearchStageSnapshot stage)
    {
        return stage.Status == ResearchStageStatus.Running || stage.RoleStates.Any(role => role.Status == ResearchRoleStatus.Running);
    }

    private static ResearchSessionStatus ResolveRecoveredSessionStatus(ResearchSession session)
    {
        var latestTurn = session.Turns
            .OrderByDescending(turn => turn.TurnIndex)
            .FirstOrDefault();

        if (latestTurn is null)
        {
            return ResearchSessionStatus.Failed;
        }

        return latestTurn.Status switch
        {
            ResearchTurnStatus.Completed when latestTurn.StageSnapshots.Any(stage => stage.Status == ResearchStageStatus.Degraded)
                => ResearchSessionStatus.Degraded,
            ResearchTurnStatus.Completed => ResearchSessionStatus.Completed,
            ResearchTurnStatus.Cancelled => ResearchSessionStatus.Idle,
            ResearchTurnStatus.Failed => ResearchSessionStatus.Failed,
            _ => ResearchSessionStatus.Failed,
        };
    }

    private static double GetElapsedMinutes(DateTime timestamp, DateTime now)
    {
        var elapsed = now - timestamp;
        return elapsed.TotalMinutes < 0 ? 0 : elapsed.TotalMinutes;
    }
}