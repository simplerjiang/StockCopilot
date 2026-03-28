using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public sealed class ResearchSessionService : IResearchSessionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SymbolLocks = new();

    private readonly AppDbContext _dbContext;
    private readonly ILogger<ResearchSessionService> _logger;

    public ResearchSessionService(AppDbContext dbContext, ILogger<ResearchSessionService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ResearchActiveSessionDto?> GetActiveSessionAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.ResearchSessions
            .AsNoTracking()
            .Where(s => s.Symbol == symbol && (s.Status == ResearchSessionStatus.Running || s.Status == ResearchSessionStatus.Degraded))
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
        var session = await _dbContext.ResearchSessions
            .AsNoTracking()
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
                ss.RoleStates.OrderBy(rs => rs.RunIndex).Select(rs => new ResearchRoleStateDto(
                    rs.Id, rs.RoleId, rs.RunIndex, rs.Status.ToString(),
                    rs.ErrorCode, rs.ErrorMessage, rs.LlmTraceId, rs.StartedAt, rs.CompletedAt)).ToArray(),
                ss.StartedAt, ss.CompletedAt)).ToArray()
            ?? Array.Empty<ResearchStageSnapshotDto>();

        var feedItems = orderedTurns
            .SelectMany(t => t.FeedItems.Select(fi => new ResearchFeedItemDto(
                fi.Id, fi.TurnId, fi.ItemType.ToString(), fi.RoleId, fi.Content, fi.TraceId, fi.CreatedAt)))
            .OrderBy(fi => fi.CreatedAt)
            .ToArray();

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
        var mode = ParseContinuationMode(request.ContinuationMode);
        ResearchSession session;

        if (mode == ResearchContinuationMode.NewSession || string.IsNullOrWhiteSpace(request.SessionKey))
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
        else
        {
            session = await _dbContext.ResearchSessions
                .Include(s => s.Turns)
                .FirstOrDefaultAsync(s => s.SessionKey == request.SessionKey, cancellationToken)
                ?? throw new InvalidOperationException($"Session not found: {request.SessionKey}");

            session.LastUserIntent = request.UserPrompt;
            session.UpdatedAt = DateTime.UtcNow;
        }

        var turnIndex = session.Turns.Count;
        var turn = new ResearchTurn
        {
            SessionId = session.Id,
            TurnIndex = turnIndex,
            UserPrompt = request.UserPrompt,
            Status = ResearchTurnStatus.Queued,
            ContinuationMode = mode,
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
            t.ContinuationMode.ToString(), t.RequestedAt, t.CompletedAt);
}
