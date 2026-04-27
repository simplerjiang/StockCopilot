using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public sealed class ResearchSessionService : IResearchSessionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SymbolLocks = new();

    private readonly AppDbContext _dbContext;
    private readonly IResearchEventBus _eventBus;
    private readonly IResearchFollowUpRoutingService _followUpRoutingService;
    private readonly ResearchZombieCleanupService _zombieCleanup;
    private readonly ILogger<ResearchSessionService> _logger;

    public ResearchSessionService(
        AppDbContext dbContext,
        IResearchEventBus eventBus,
        IResearchFollowUpRoutingService followUpRoutingService,
        ResearchZombieCleanupService zombieCleanup,
        ILogger<ResearchSessionService> logger)
    {
        _dbContext = dbContext;
        _eventBus = eventBus;
        _followUpRoutingService = followUpRoutingService;
        _zombieCleanup = zombieCleanup;
        _logger = logger;
    }

    public async Task<ResearchActiveSessionDto?> GetActiveSessionAsync(string symbol, CancellationToken cancellationToken = default)
    {
        await _zombieCleanup.CleanupStaleRunningAsync(
            ResearchZombieCleanupService.QueryStaleThreshold,
            symbol: symbol,
            cancellationToken: cancellationToken);

        var session = await _dbContext.ResearchSessions
            .AsNoTracking()
            .Where(s => s.Symbol == symbol && s.Status != ResearchSessionStatus.Closed && s.Status != ResearchSessionStatus.Failed)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (session is null)
            return null;

        var latestTurn = await _dbContext.ResearchTurns
            .AsNoTracking()
            .Where(t => t.SessionId == session.Id)
            .OrderByDescending(t => t.TurnIndex)
            .FirstOrDefaultAsync(cancellationToken);

        return new ResearchActiveSessionDto(
            session.Id, session.SessionKey, session.Symbol, session.Status.ToString(),
            session.ActiveStage,
            latestTurn is null ? null : MapTurnSummary(latestTurn));
    }

    public async Task<ResearchSessionDetailDto?> GetSessionDetailAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        await _zombieCleanup.CleanupStaleRunningAsync(
            ResearchZombieCleanupService.QueryStaleThreshold,
            sessionId: sessionId,
            cancellationToken: cancellationToken);

        var session = await _dbContext.ResearchSessions
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(s => s.Turns).ThenInclude(t => t.StageSnapshots).ThenInclude(ss => ss.RoleStates)
            .Include(s => s.Turns).ThenInclude(t => t.FeedItems)
            .Include(s => s.Reports)
            .Include(s => s.Decisions)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session is null) return null;

        var orderedTurns = session.Turns.OrderBy(t => t.TurnIndex).ToArray();
        var latestTurn = orderedTurns.LastOrDefault();

        var stageSnapshots = latestTurn?.StageSnapshots
            .OrderBy(ss => ss.StageRunIndex)
            .Select(ss => new ResearchStageSnapshotDto(
                ss.Id, ss.StageType.ToString(), ss.StageRunIndex, ss.ExecutionMode.ToString(), ss.Status.ToString(),
                ss.Summary,
                ParseJsonStringArray(ss.DegradedFlagsJson),
                ss.RoleStates.OrderBy(rs => rs.RunIndex).Select(rs => new ResearchRoleStateDto(
                    rs.Id, rs.RoleId, rs.RunIndex, rs.Status.ToString(),
                    rs.ErrorCode, ErrorSanitizer.SanitizeErrorMessage(rs.ErrorMessage), rs.LlmTraceId,
                    rs.OutputRefsJson, rs.OutputContentJson, ParseJsonStringArray(rs.DegradedFlagsJson),
                    rs.StartedAt, rs.CompletedAt)).ToArray(),
                ss.StartedAt, ss.CompletedAt)).ToArray()
            ?? Array.Empty<ResearchStageSnapshotDto>();

        var feedItems = orderedTurns
            .SelectMany(t => t.FeedItems.Select(fi => new ResearchFeedItemDto(
                fi.Id, fi.TurnId, fi.ItemType.ToString(), fi.RoleId, fi.Content, fi.TraceId, fi.CreatedAt, fi.MetadataJson)))
            .OrderBy(fi => fi.CreatedAt)
            .ToList();

        // Merge in-memory events for running turns so the frontend sees real-time progress
        if (session.Status == ResearchSessionStatus.Running && latestTurn is not null)
        {
            var liveEvents = _eventBus.Peek(latestTurn.Id);
            if (liveEvents.Count > 0)
            {
                var existingKeys = new HashSet<(string, string?, string)>(
                    feedItems.Where(f => f.TurnId == latestTurn.Id)
                             .Select(f => (f.ItemType, f.RoleId, f.Content ?? "")));

                foreach (var evt in liveEvents)
                {
                    if (existingKeys.Contains((evt.EventType.ToString(), evt.RoleId, evt.Summary))) continue;
                    feedItems.Add(new ResearchFeedItemDto(
                        0, evt.TurnId, evt.EventType.ToString(), evt.RoleId, evt.Summary, evt.TraceId, evt.Timestamp, evt.DetailJson));
                }
                feedItems = feedItems.OrderBy(fi => fi.CreatedAt).ToList();
            }
        }

        return new ResearchSessionDetailDto(
            session.Id, session.SessionKey, session.Symbol, session.Name,
            session.Status.ToString(), session.ActiveStage, session.LastUserIntent,
            session.LatestRating, session.LatestDecisionHeadline,
            orderedTurns.Select(MapTurnSummary).ToArray(),
            session.Reports.OrderBy(r => r.VersionIndex)
                .Select(r => new ResearchReportSnapshotDto(r.Id, r.TurnId, r.VersionIndex, r.IsFinal, r.ReportBlocksJson, r.CreatedAt)).ToArray(),
            session.Decisions.OrderByDescending(d => d.CreatedAt)
                .Select(d => new ResearchDecisionSnapshotDto(d.Id, d.TurnId, d.Rating, d.Action, d.ExecutiveSummary, d.Confidence, d.CreatedAt)).ToArray(),
            stageSnapshots,
            feedItems,
            session.CreatedAt, session.UpdatedAt);
    }

    public async Task<IReadOnlyList<ResearchSessionSummaryDto>> ListSessionsAsync(string symbol, int limit = 20, CancellationToken cancellationToken = default)
    {
        await _zombieCleanup.CleanupStaleRunningAsync(
            ResearchZombieCleanupService.QueryStaleThreshold,
            symbol: symbol,
            cancellationToken: cancellationToken);

        return await _dbContext.ResearchSessions
            .AsNoTracking()
            .Where(s => s.Symbol == symbol)
            .OrderByDescending(s => s.UpdatedAt)
            .Take(limit)
            .Select(s => new ResearchSessionSummaryDto(
                s.Id, s.SessionKey, s.Symbol, s.Name, s.Status.ToString(),
                s.LatestRating, s.LatestDecisionHeadline, s.CreatedAt, s.UpdatedAt))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<ResearchTurnSubmitResponseDto> SubmitTurnAsync(ResearchTurnSubmitRequestDto request, CancellationToken cancellationToken = default)
    {
        var semaphore = SymbolLocks.GetOrAdd(request.Symbol, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            return await SubmitTurnCoreAsync(request, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<ResearchTurnSubmitResponseDto> SubmitTurnCoreAsync(ResearchTurnSubmitRequestDto request, CancellationToken cancellationToken)
    {
        await _zombieCleanup.CleanupStaleRunningAsync(
            ResearchZombieCleanupService.QueryStaleThreshold,
            symbol: request.Symbol,
            cancellationToken: cancellationToken);

        var mode = ParseContinuationMode(request.ContinuationMode);
        ResearchSession session = null!;
        ResearchFollowUpRoutingDecision? routingDecision = null;
        bool createNew = mode == ResearchContinuationMode.NewSession || string.IsNullOrWhiteSpace(request.SessionKey);

        if (!createNew)
        {
            var existing = await _dbContext.ResearchSessions
                .Include(s => s.Turns)
                .FirstOrDefaultAsync(s => s.SessionKey == request.SessionKey, cancellationToken);

            if (existing is not null && existing.Symbol == request.Symbol)
            {
                session = existing;
                session.LastUserIntent = request.UserPrompt;
                session.UpdatedAt = DateTime.UtcNow;

                if (!request.FromStageIndex.HasValue && mode == ResearchContinuationMode.ContinueSession)
                {
                    // Use instant heuristic for HTTP response speed; full LLM routing runs in background pipeline
                    routingDecision = _followUpRoutingService.DecideHeuristic(request.UserPrompt);
                }
            }
            else
            {
                if (existing is not null)
                {
                    _logger.LogWarning(
                        "Session symbol mismatch: session {SessionKey} is for {OldSymbol} but request targets {NewSymbol}. Creating new session.",
                        request.SessionKey, existing.Symbol, request.Symbol);
                }
                createNew = true;
            }
        }

        if (createNew)
        {
            var activeSessions = await _dbContext.ResearchSessions
                .Where(s => s.Symbol == request.Symbol &&
                    (s.Status == ResearchSessionStatus.Running || s.Status == ResearchSessionStatus.Degraded || s.Status == ResearchSessionStatus.Idle))
                .ToListAsync(cancellationToken);

            foreach (var active in activeSessions)
            {
                active.Status = ResearchSessionStatus.Closed;
                active.UpdatedAt = DateTime.UtcNow;
            }

            session = new ResearchSession
            {
                SessionKey = Guid.NewGuid().ToString("N"),
                Symbol = request.Symbol,
                Name = request.UserPrompt.Length > 100 ? request.UserPrompt[..100] : request.UserPrompt,
                Status = ResearchSessionStatus.Idle,
                LastUserIntent = request.UserPrompt,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.ResearchSessions.Add(session);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var turnIndex = session.Turns.Count;
        var effectiveMode = request.FromStageIndex.HasValue
            ? ResearchContinuationMode.PartialRerun
            : routingDecision?.ContinuationMode ?? mode;
        var routingStageIndex = request.FromStageIndex ?? routingDecision?.FromStageIndex;
        var turn = new ResearchTurn
        {
            SessionId = session.Id,
            TurnIndex = turnIndex,
            UserPrompt = request.UserPrompt,
            Status = ResearchTurnStatus.Queued,
            ContinuationMode = effectiveMode,
            ReuseScope = routingDecision?.ReuseScope,
            RerunScope = routingStageIndex?.ToString(),
            ChangeSummary = routingDecision?.ChangeSummary,
            RoutingDecision = routingDecision?.ContinuationMode.ToString(),
            RoutingReasoning = routingDecision?.Reasoning,
            RoutingConfidence = routingDecision?.Confidence,
            RoutingStageIndex = routingStageIndex,
            RequestedAt = DateTime.UtcNow
        };

        _dbContext.ResearchTurns.Add(turn);
        await _dbContext.SaveChangesAsync(cancellationToken);

        session.ActiveTurnId = turn.Id;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Research turn {TurnId} queued for {SessionKey} symbol {Symbol}",
            turn.Id, session.SessionKey, session.Symbol);

        return new ResearchTurnSubmitResponseDto(session.Id, turn.Id, session.SessionKey, turn.Status.ToString());
    }

    public async Task<bool> CancelActiveTurnAsync(long sessionId, CancellationToken ct)
    {
        var session = await _dbContext.ResearchSessions
            .Include(s => s.Turns)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session is null) return false;

        var runningTurn = session.Turns
            .FirstOrDefault(t => t.Status == ResearchTurnStatus.Running || t.Status == ResearchTurnStatus.Queued);

        if (runningTurn is null) return false;

        runningTurn.Status = ResearchTurnStatus.Cancelled;
        runningTurn.CompletedAt = DateTime.UtcNow;
        runningTurn.StopReason = "用户手动取消";
        session.Status = ResearchSessionStatus.Idle;
        session.ActiveTurnId = null;
        session.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);
        return true;
    }

    internal static ResearchContinuationMode ParseContinuationMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return ResearchContinuationMode.NewSession;

        return Enum.TryParse<ResearchContinuationMode>(mode, true, out var parsed)
            ? parsed
            : ResearchContinuationMode.NewSession;
    }

    private static ResearchTurnSummaryDto MapTurnSummary(ResearchTurn t) =>
        new(t.Id, t.TurnIndex, t.UserPrompt, t.Status.ToString(),
            t.ContinuationMode.ToString(), t.RoutingDecision, t.RoutingReasoning,
            t.RoutingConfidence, t.RoutingStageIndex, t.RequestedAt, t.CompletedAt);

    private static IReadOnlyList<string> ParseJsonStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            return doc.RootElement.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
