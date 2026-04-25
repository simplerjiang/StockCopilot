using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend;

public interface IRecommendationSessionService
{
    Task<RecommendationSession> CreateSessionAsync(string userPrompt, CancellationToken ct = default);
    Task<RecommendSessionDetailDto?> GetSessionDetailAsync(long sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<RecommendSessionSummaryDto>> ListSessionsAsync(int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<(RecommendationTurn Turn, FollowUpPlan Plan)> SubmitFollowUpAsync(long sessionId, string userPrompt, CancellationToken ct = default);
}

public sealed class RecommendationSessionService : IRecommendationSessionService
{
    private readonly AppDbContext _db;
    private readonly IRecommendEventBus _eventBus;
    private readonly IRecommendFollowUpRouter _router;
    private readonly ILogger<RecommendationSessionService> _logger;

    public RecommendationSessionService(
        AppDbContext db,
        IRecommendEventBus eventBus,
        IRecommendFollowUpRouter router,
        ILogger<RecommendationSessionService> logger)
    {
        _db = db;
        _eventBus = eventBus;
        _router = router;
        _logger = logger;
    }

    public async Task<RecommendationSession> CreateSessionAsync(string userPrompt, CancellationToken ct = default)
    {
        var session = new RecommendationSession
        {
            SessionKey = Guid.NewGuid().ToString("N"),
            Status = RecommendSessionStatus.Idle,
            LastUserIntent = userPrompt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.RecommendationSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        var turn = new RecommendationTurn
        {
            SessionId = session.Id,
            TurnIndex = 0,
            UserPrompt = userPrompt,
            Status = RecommendTurnStatus.Queued,
            ContinuationMode = RecommendContinuationMode.NewSession,
            RequestedAt = DateTime.UtcNow,
        };
        _db.RecommendationTurns.Add(turn);
        await _db.SaveChangesAsync(ct);

        session.ActiveTurnId = turn.Id;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created recommendation session {SessionId} with turn {TurnId}", session.Id, turn.Id);
        return session;
    }

    public async Task<RecommendSessionDetailDto?> GetSessionDetailAsync(long sessionId, CancellationToken ct = default)
    {
        var session = await _db.RecommendationSessions
            .Include(s => s.Turns)
                .ThenInclude(t => t.StageSnapshots)
                    .ThenInclude(ss => ss.RoleStates)
            .Include(s => s.Turns)
                .ThenInclude(t => t.FeedItems)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session is null)
        {
            return null;
        }

        var orderedTurns = session.Turns
            .OrderBy(t => t.TurnIndex)
            .ToArray();
        var turnFeeds = orderedTurns.ToDictionary(
            turn => turn.Id,
            turn => turn.FeedItems
                .Select(MapFeedItem)
                .OrderBy(item => item.Timestamp)
                .ToList());

        var liveTurn = orderedTurns
            .FirstOrDefault(turn => turn.Id == session.ActiveTurnId)
            ?? orderedTurns.LastOrDefault();

        if (liveTurn is not null && ShouldMergeLiveEvents(session.Status, liveTurn.Status))
        {
            var feedList = turnFeeds[liveTurn.Id];
            var existingKeys = feedList
                .Select(CreateFeedDedupKey)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var evt in _eventBus.Snapshot(liveTurn.Id).OrderBy(item => item.Timestamp))
            {
                var feedItem = MapLiveEvent(evt);
                if (existingKeys.Add(CreateFeedDedupKey(feedItem)))
                {
                    feedList.Add(feedItem);
                }
            }

            feedList.Sort((left, right) => left.Timestamp.CompareTo(right.Timestamp));
        }

        var turnDtos = orderedTurns
            .Select(turn => MapTurn(turn, turnFeeds[turn.Id]))
            .ToArray();
        var feedItems = turnDtos
            .SelectMany(turn => turn.FeedItems)
            .OrderBy(item => item.Timestamp)
            .ToArray();

        return new RecommendSessionDetailDto(
            session.Id,
            session.SessionKey,
            session.Status.ToString(),
            session.ActiveTurnId,
            session.LastUserIntent,
            session.MarketSentiment,
            turnDtos,
            feedItems,
            session.CreatedAt,
            session.UpdatedAt);
    }

    public async Task<IReadOnlyList<RecommendSessionSummaryDto>> ListSessionsAsync(int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        // Auto-cleanup zombie Running sessions older than 30 minutes
        var cutoff = DateTime.UtcNow.AddMinutes(-30);
        var zombies = await _db.RecommendationSessions
            .Where(s => s.Status == RecommendSessionStatus.Running && s.UpdatedAt < cutoff)
            .ToArrayAsync(ct);
        if (zombies.Length > 0)
        {
            foreach (var z in zombies)
            {
                z.Status = RecommendSessionStatus.TimedOut;
                z.UpdatedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Auto-marked {Count} zombie Running sessions as TimedOut", zombies.Length);
        }

        var clamped = Math.Clamp(pageSize, 1, 100);
        return await _db.RecommendationSessions
            .AsNoTracking()
            .Where(s => s.LastUserIntent == null
                || (!s.LastUserIntent.StartsWith("runtimeclean") && !s.LastUserIntent.StartsWith("debug")))
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * clamped)
            .Take(clamped)
            .Select(session => new RecommendSessionSummaryDto(
                session.Id,
                session.SessionKey,
                session.Status.ToString(),
                session.LastUserIntent,
                session.ActiveTurnId,
                session.CreatedAt,
                session.UpdatedAt))
            .ToArrayAsync(ct);
    }

    public async Task<(RecommendationTurn Turn, FollowUpPlan Plan)> SubmitFollowUpAsync(long sessionId, string userPrompt, CancellationToken ct = default)
    {
        var session = await _db.RecommendationSessions
            .Include(s => s.Turns)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException($"Session {sessionId} not found");

        // Route the follow-up
        var plan = await _router.RouteAsync(sessionId, userPrompt, ct);

        var continuationMode = plan.Strategy switch
        {
            FollowUpStrategy.PartialRerun => RecommendContinuationMode.PartialRerun,
            FollowUpStrategy.FullRerun => RecommendContinuationMode.FullRerun,
            FollowUpStrategy.WorkbenchHandoff => RecommendContinuationMode.WorkbenchHandoff,
            FollowUpStrategy.DirectAnswer => RecommendContinuationMode.DirectAnswer,
            _ => RecommendContinuationMode.FullRerun
        };

        var maxIndex = session.Turns.Any() ? session.Turns.Max(t => t.TurnIndex) : -1;

        var turn = new RecommendationTurn
        {
            SessionId = sessionId,
            TurnIndex = maxIndex + 1,
            UserPrompt = userPrompt,
            Status = RecommendTurnStatus.Queued,
            ContinuationMode = continuationMode,
            RoutingDecision = plan.Strategy.ToString(),
            RoutingReasoning = plan.Reasoning,
            RoutingConfidence = plan.Confidence,
            RequestedAt = DateTime.UtcNow,
        };
        _db.RecommendationTurns.Add(turn);
        session.LastUserIntent = userPrompt;
        session.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        session.ActiveTurnId = turn.Id;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Follow-up turn {TurnId} created for session {SessionId}: strategy={Strategy}, fromStage={FromStage}",
            turn.Id, sessionId, plan.Strategy, plan.FromStageIndex);

        return (turn, plan);
    }

    private static RecommendTurnDto MapTurn(RecommendationTurn turn, IReadOnlyList<RecommendFeedItemDto> feedItems)
    {
        var stageSnapshots = turn.StageSnapshots
            .OrderBy(snapshot => snapshot.StageRunIndex)
            .Select(snapshot => new RecommendStageSnapshotDto(
                snapshot.Id,
                snapshot.StageType.ToString(),
                snapshot.StageRunIndex,
                snapshot.ExecutionMode.ToString(),
                snapshot.Status.ToString(),
                snapshot.Summary,
                snapshot.RoleStates
                    .OrderBy(state => state.RunIndex)
                    .Select(state => new RecommendRoleStateDto(
                        state.Id,
                        state.RoleId,
                        state.RunIndex,
                        state.Status.ToString(),
                        state.ErrorCode,
                        ErrorSanitizer.SanitizeErrorMessage(state.ErrorMessage),
                        state.LlmTraceId,
                        state.OutputContentJson,
                        state.StartedAt,
                        state.CompletedAt))
                    .ToArray(),
                snapshot.StartedAt,
                snapshot.CompletedAt))
            .ToArray();

        return new RecommendTurnDto(
            turn.Id,
            turn.TurnIndex,
            turn.UserPrompt,
            turn.Status.ToString(),
            turn.ContinuationMode.ToString(),
            turn.RoutingDecision,
            turn.RoutingReasoning,
            turn.RoutingConfidence,
            turn.RequestedAt,
            turn.StartedAt,
            turn.CompletedAt,
            stageSnapshots,
            feedItems);
    }

    private static RecommendFeedItemDto MapFeedItem(RecommendationFeedItem item)
    {
        var metadata = ParseFeedMetadata(item.MetadataJson);
        return new RecommendFeedItemDto(
            item.Id,
            item.TurnId,
            item.ItemType.ToString(),
            metadata.EventType,
            item.RoleId,
            item.Content,
            metadata.DetailJson,
            metadata.StageType,
            item.TraceId,
            item.CreatedAt);
    }

    private static RecommendFeedItemDto MapLiveEvent(RecommendEvent evt) => new(
        0,
        evt.TurnId,
        MapEventToFeedType(evt.EventType).ToString(),
        evt.EventType.ToString(),
        evt.RoleId,
        evt.Summary,
        evt.DetailJson,
        evt.StageType,
        evt.TraceId,
        evt.Timestamp);

    private static bool ShouldMergeLiveEvents(RecommendSessionStatus sessionStatus, RecommendTurnStatus turnStatus) =>
        sessionStatus is RecommendSessionStatus.Running or RecommendSessionStatus.Degraded ||
        turnStatus is RecommendTurnStatus.Running or RecommendTurnStatus.Queued;

    private static string CreateFeedDedupKey(RecommendFeedItemDto item) => string.Join("|",
        item.TurnId,
        item.ItemType,
        item.EventType ?? string.Empty,
        item.RoleId ?? string.Empty,
        item.StageType ?? string.Empty,
        item.TraceId ?? string.Empty,
        item.Timestamp.ToUniversalTime().Ticks,
        item.Summary);

    private static RecommendFeedMetadata ParseFeedMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new RecommendFeedMetadata(null, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new RecommendFeedMetadata(null, null, metadataJson);
            }

            var root = document.RootElement;
            return new RecommendFeedMetadata(
                TryGetString(root, "eventType"),
                TryGetString(root, "stageType"),
                TryGetString(root, "detailJson") ?? metadataJson);
        }
        catch
        {
            return new RecommendFeedMetadata(null, null, metadataJson);
        }
    }

    private static string? TryGetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.GetRawText();
    }

    private static RecommendFeedItemType MapEventToFeedType(RecommendEventType eventType) => eventType switch
    {
        RecommendEventType.TurnStarted => RecommendFeedItemType.UserFollowUp,
        RecommendEventType.RoleStarted or RecommendEventType.RoleSummaryReady or
        RecommendEventType.RoleCompleted or RecommendEventType.RoleFailed => RecommendFeedItemType.RoleMessage,
        RecommendEventType.ToolDispatched or RecommendEventType.ToolCompleted => RecommendFeedItemType.ToolEvent,
        RecommendEventType.StageStarted or RecommendEventType.StageCompleted or RecommendEventType.StageFailed => RecommendFeedItemType.StageTransition,
        RecommendEventType.DegradedNotice => RecommendFeedItemType.DegradedNotice,
        RecommendEventType.TurnFailed => RecommendFeedItemType.ErrorNotice,
        _ => RecommendFeedItemType.SystemNotice
    };

    private sealed record RecommendFeedMetadata(string? EventType, string? StageType, string? DetailJson);
}
