using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend.WebSearch;
using System.Text.Json;
using Xunit;

namespace SimplerJiangAiAgent.Api.Tests;

#region Stubs

internal sealed class StubMcpToolGateway : IMcpToolGateway
{
    public int CallCount;
    public readonly List<string> CalledTools = new();
    public readonly List<(string Tool, string? TaskId)> ToolCalls = new();
    public bool ShouldThrow;

    private static readonly StockCopilotMcpCacheDto EmptyCache = new(false, "none", DateTime.MinValue);
    private static readonly StockCopilotMcpMetaDto EmptyMeta = new("1.0", "test", "stub", null, null, null, null);

    private Task<StockCopilotMcpEnvelopeDto<T>> Wrap<T>(string tool, string? taskId, T data)
    {
        CallCount++;
        CalledTools.Add(tool);
        ToolCalls.Add((tool, taskId));
        if (ShouldThrow) throw new InvalidOperationException($"Tool {tool} failed");
        return Task.FromResult(new StockCopilotMcpEnvelopeDto<T>(
            "trace-stub", taskId ?? "task-stub", tool, 0, EmptyCache,
            Array.Empty<string>(), Array.Empty<string>(), data,
            Array.Empty<StockCopilotMcpEvidenceDto>(),
            Array.Empty<StockCopilotMcpFeatureDto>(), EmptyMeta));
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotCompanyOverviewDataDto>> GetCompanyOverviewAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window, CancellationToken ct)
        => Wrap(StockMcpToolNames.CompanyOverview, taskId, new StockCopilotCompanyOverviewDataDto(symbol, "Test", null, 0m, 0m, null, null, null, null, DateTime.UtcNow, null, 0, null, null));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotProductDataDto>> GetProductAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window, CancellationToken ct)
        => Wrap(StockMcpToolNames.Product, taskId, new StockCopilotProductDataDto(symbol, null, null, null, null, null, null, 0, "stub", Array.Empty<StockCopilotProductFactDto>()));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotFundamentalsDataDto>> GetFundamentalsAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window, CancellationToken ct)
        => Wrap(StockMcpToolNames.Fundamentals, taskId, new StockCopilotFundamentalsDataDto(symbol, null, 0, Array.Empty<StockFundamentalFactDto>()));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotShareholderDataDto>> GetShareholderAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window, CancellationToken ct)
        => Wrap(StockMcpToolNames.Shareholder, taskId, new StockCopilotShareholderDataDto(symbol, null, null, 0, Array.Empty<StockFundamentalFactDto>()));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotMarketContextDataDto>> GetMarketContextAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window, CancellationToken ct)
        => Wrap(StockMcpToolNames.MarketContext, taskId, new StockCopilotMarketContextDataDto(symbol, false, null, null, null, null, null));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotSocialSentimentDataDto>> GetSocialSentimentAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window, CancellationToken ct)
        => Wrap(StockMcpToolNames.SocialSentiment, taskId, new StockCopilotSocialSentimentDataDto(symbol, "ok", false, null, "stub", 0, null,
            new StockCopilotSentimentCountDto(0, 0, 0, 0, null),
            new StockCopilotSentimentCountDto(0, 0, 0, 0, null),
            new StockCopilotSentimentCountDto(0, 0, 0, 0, null), null));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotKlineDataDto>> GetKlineAsync(string symbol, string interval, int count, string? source, string? taskId, StockCopilotMcpWindowOptions? window, CancellationToken ct)
        => Wrap(StockMcpToolNames.Kline, taskId, new StockCopilotKlineDataDto(symbol, interval, count, Array.Empty<KLinePointDto>(), new StockCopilotKeyLevelsDto(null, null, null, null, null, null), "neutral", 0m, 0m, 0m, 0m));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotMinuteDataDto>> GetMinuteAsync(string symbol, string? source, string? taskId, StockCopilotMcpWindowOptions? window, CancellationToken ct)
        => Wrap(StockMcpToolNames.Minute, taskId, new StockCopilotMinuteDataDto(symbol, "closed", 0, Array.Empty<MinuteLinePointDto>(), null, null, null, null));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotStrategyDataDto>> GetStrategyAsync(string symbol, string interval, int count, string? source, IReadOnlyList<string>? strategies, string? taskId, StockCopilotMcpWindowOptions? window, CancellationToken ct)
        => Wrap(StockMcpToolNames.Strategy, taskId, new StockCopilotStrategyDataDto(symbol, interval, Array.Empty<string>(), Array.Empty<StockCopilotStrategySignalDto>()));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotNewsDataDto>> GetNewsAsync(string symbol, string level, string? taskId, StockCopilotMcpWindowOptions? window, CancellationToken ct)
        => Wrap(StockMcpToolNames.News, taskId, new StockCopilotNewsDataDto(symbol, level, 0, null));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotSearchDataDto>> SearchAsync(string query, bool trustedOnly, string? taskId, CancellationToken ct)
        => Wrap(StockMcpToolNames.Search, taskId, new StockCopilotSearchDataDto(query, "stub", trustedOnly, 0, Array.Empty<StockCopilotSearchResultDto>()));

    public Task<WebSearchResult> WebSearchAsync(string query, WebSearchOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(new WebSearchResult(Array.Empty<WebSearchItem>(), "stub", false));

    public Task<WebSearchResult> WebSearchNewsAsync(string query, WebSearchOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(new WebSearchResult(Array.Empty<WebSearchItem>(), "stub", false));

    public Task<WebReadResult> WebReadUrlAsync(string url, int maxChars = 8000, CancellationToken ct = default)
        => Task.FromResult(new WebReadResult("", url, 0, false));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotFinancialReportDataDto>> GetFinancialReportAsync(string symbol, int periods, string? taskId, CancellationToken cancellationToken = default)
        => Wrap(StockMcpToolNames.FinancialReport, taskId, new StockCopilotFinancialReportDataDto(symbol, periods, Array.Empty<FinancialReportPeriodDto>()));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotFinancialTrendDataDto>> GetFinancialTrendAsync(string symbol, int periods, string? taskId, CancellationToken cancellationToken = default)
        => Wrap(StockMcpToolNames.FinancialTrend, taskId, new StockCopilotFinancialTrendDataDto(symbol, periods, Array.Empty<FinancialTrendPointDto>(), Array.Empty<FinancialTrendPointDto>(), Array.Empty<FinancialTrendPointDto>(), Array.Empty<FinancialDividendDto>()));

    public Task<List<RagCitationDto>> SearchFinancialReportRagAsync(string symbol, string query, int topK = 5, CancellationToken cancellationToken = default)
    {
        CallCount++;
        CalledTools.Add(StockMcpToolNames.FinancialReportRag);
        ToolCalls.Add((StockMcpToolNames.FinancialReportRag, null));
        if (ShouldThrow) throw new InvalidOperationException($"Tool {StockMcpToolNames.FinancialReportRag} failed");
        return Task.FromResult(new List<RagCitationDto>());
    }
}

internal sealed class StubRoleToolPolicyService : IRoleToolPolicyService
{
    public bool AllowAll = true;
    public readonly HashSet<string> BlockedTools = new();

    public McpToolAuthorizationResult AuthorizeSystemEndpoint(string toolName) =>
        new(true, toolName, "system", null, null);

    public McpToolAuthorizationResult AuthorizeRole(string roleType, string toolName) =>
        BlockedTools.Contains(toolName)
            ? new(false, toolName, "role", "BLOCKED", $"{toolName} blocked for {roleType}")
            : new(AllowAll, toolName, "role", null, null);
}

internal sealed class StubContractRegistry : IStockAgentRoleContractRegistry
{
    private readonly Dictionary<string, StockCopilotRoleContractDto> _contracts = new();

    public void Register(StockCopilotRoleContractDto contract) => _contracts[contract.RoleId] = contract;

    public IReadOnlyList<StockCopilotRoleContractDto> List() => _contracts.Values.ToList();

    public StockCopilotRoleContractDto GetRequired(string roleId) =>
        _contracts.TryGetValue(roleId, out var c) ? c : new StockCopilotRoleContractDto(
            roleId, roleId, "analyst", "local_preferred", Array.Empty<string>(),
            "", "", 0, false, null);

    public StockCopilotRoleContractChecklistDto BuildChecklist() =>
        new("test", "v1", "test", DateTime.UtcNow, List());
}

internal sealed class StubLlmService : ILlmService
{
    public string ResponseContent = "LLM response content";
    public bool ShouldThrow;
    public string? LastProvider;
    public LlmChatRequest? LastRequest;

    public Task<LlmChatResult> ChatAsync(string provider, LlmChatRequest request, CancellationToken ct)
    {
        if (ShouldThrow) throw new InvalidOperationException("LLM call failed");
        LastProvider = provider;
        LastRequest = request;
        return Task.FromResult(new LlmChatResult(ResponseContent, $"trace-{Guid.NewGuid():N}"));
    }
}

internal sealed class StubLlmSettingsStore : ILlmSettingsStore
{
    private readonly Dictionary<string, LlmProviderSettings> _providers;

    public StubLlmSettingsStore(string activeProviderKey, params LlmProviderSettings[] providers)
    {
        ActiveProviderKey = activeProviderKey;
        _providers = providers.ToDictionary(provider => provider.Provider, StringComparer.OrdinalIgnoreCase);
    }

    public string ActiveProviderKey { get; set; }

    public Task<IReadOnlyCollection<LlmProviderSettings>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyCollection<LlmProviderSettings>>(_providers.Values.ToArray());

    public Task<string> GetActiveProviderKeyAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ActiveProviderKey);

    public Task<string> SetActiveProviderKeyAsync(string provider, CancellationToken cancellationToken = default)
    {
        ActiveProviderKey = provider;
        return Task.FromResult(provider);
    }

    public Task<string> ResolveProviderKeyAsync(string? provider, CancellationToken cancellationToken = default)
        => Task.FromResult(string.IsNullOrWhiteSpace(provider) || string.Equals(provider, "active", StringComparison.OrdinalIgnoreCase)
            ? ActiveProviderKey
            : provider);

    public Task<LlmProviderSettings?> GetProviderAsync(string provider, CancellationToken cancellationToken = default)
    {
        _providers.TryGetValue(provider, out var settings);
        return Task.FromResult<LlmProviderSettings?>(settings);
    }

    public Task<LlmProviderSettings> UpsertAsync(LlmProviderSettings settings, CancellationToken cancellationToken = default)
    {
        _providers[settings.Provider] = settings;
        return Task.FromResult(settings);
    }

    public Task<string> GetGlobalTavilyKeyAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(string.Empty);

    public Task<(string Provider, string Model, int BatchSize)> GetNewsCleansingSettingsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<(string Provider, string Model, int BatchSize)>(("active", string.Empty, 12));

    public Task SetNewsCleansingSettingsAsync(string provider, string model, int batchSize, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

internal sealed class StubRoleExecutor : IResearchRoleExecutor
{
    public readonly Dictionary<string, RoleExecutionResult> ResultsByRoleId = new();
    public readonly Dictionary<string, Queue<RoleExecutionResult>> SequencedResultsByRoleId = new();
    public readonly List<string> ExecutedRoleIds = new();
    public RoleExecutionResult DefaultResult = new("default", ResearchRoleStatus.Completed, "{\"content\":\"ok\"}", "trace-1", Array.Empty<string>(), null, null);
    private readonly Dictionary<string, int> _executionCountByRoleId = new(StringComparer.Ordinal);

    public Task<RoleExecutionResult> ExecuteRoleAsync(RoleExecutionContext context, CancellationToken ct)
    {
        ExecutedRoleIds.Add(context.RoleId);
        _executionCountByRoleId[context.RoleId] = _executionCountByRoleId.TryGetValue(context.RoleId, out var count)
            ? count + 1
            : 1;

        if (SequencedResultsByRoleId.TryGetValue(context.RoleId, out var queuedResults) && queuedResults.Count > 0)
        {
            var next = queuedResults.Count > 1 ? queuedResults.Dequeue() : queuedResults.Peek();
            return Task.FromResult(next with { RoleId = context.RoleId });
        }

        if (ResultsByRoleId.TryGetValue(context.RoleId, out var r))
            return Task.FromResult(r with { RoleId = context.RoleId });
        return Task.FromResult(DefaultResult with { RoleId = context.RoleId });
    }
}

internal sealed class StubFollowUpRoutingService : IResearchFollowUpRoutingService
{
    public ResearchFollowUpRoutingDecision Result { get; set; } = new(
        ResearchContinuationMode.ContinueSession,
        null,
        "reuse_existing_materials",
        "default",
        "default",
        0.5m);

    public Task<ResearchFollowUpRoutingDecision> DecideAsync(long sessionId, string userPrompt, CancellationToken cancellationToken = default)
        => Task.FromResult(Result);

    public ResearchFollowUpRoutingDecision DecideHeuristic(string userPrompt) => Result;
}

internal sealed class NullReportService : IResearchReportService
{
    public Task GenerateBlocksFromStageAsync(long sessionId, long turnId, ResearchStageType stageType,
        IReadOnlyList<string> outputs, IReadOnlyList<string> degradedFlags, CancellationToken ct = default)
        => Task.CompletedTask;
    public Task<ResearchTurnReportDto?> GetTurnReportAsync(long turnId, CancellationToken ct = default)
        => Task.FromResult<ResearchTurnReportDto?>(null);
    public Task<ResearchFinalDecisionDto?> GetFinalDecisionAsync(long turnId, CancellationToken ct = default)
        => Task.FromResult<ResearchFinalDecisionDto?>(null);
}

#endregion

public sealed class ResearchSessionAndRunnerTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static ResearchZombieCleanupService CreateZombieCleanupService(AppDbContext db) =>
        new(db, NullLogger<ResearchZombieCleanupService>.Instance);

    private static ResearchSessionService CreateSessionService(AppDbContext db, StubFollowUpRoutingService? routingService = null) =>
        new(
            db,
            new ResearchEventBus(),
            routingService ?? new StubFollowUpRoutingService(),
            CreateZombieCleanupService(db),
            NullLogger<ResearchSessionService>.Instance);

    private static async Task<(ResearchSession Session, ResearchTurn Turn, ResearchStageSnapshot Stage, ResearchRoleState Role)> SeedStaleRunningSessionAsync(
        AppDbContext db,
        string symbol = "SH600000")
    {
        var staleAt = DateTime.UtcNow.AddMinutes(-45);
        var session = new ResearchSession
        {
            SessionKey = Guid.NewGuid().ToString("N"),
            Symbol = symbol,
            Name = "stale session",
            Status = ResearchSessionStatus.Running,
            ActiveStage = ResearchStageType.AnalystTeam.ToString(),
            LastUserIntent = "stale run",
            CreatedAt = staleAt.AddMinutes(-5),
            UpdatedAt = staleAt
        };
        db.ResearchSessions.Add(session);
        await db.SaveChangesAsync();

        var turn = new ResearchTurn
        {
            SessionId = session.Id,
            TurnIndex = 0,
            UserPrompt = "stale run",
            Status = ResearchTurnStatus.Running,
            ContinuationMode = ResearchContinuationMode.NewSession,
            RequestedAt = staleAt.AddMinutes(-1),
            StartedAt = staleAt
        };
        db.ResearchTurns.Add(turn);
        await db.SaveChangesAsync();

        session.ActiveTurnId = turn.Id;

        var stage = new ResearchStageSnapshot
        {
            TurnId = turn.Id,
            StageType = ResearchStageType.AnalystTeam,
            StageRunIndex = 1,
            ExecutionMode = ResearchStageExecutionMode.Parallel,
            Status = ResearchStageStatus.Running,
            StartedAt = staleAt
        };
        db.ResearchStageSnapshots.Add(stage);
        await db.SaveChangesAsync();

        var role = new ResearchRoleState
        {
            StageId = stage.Id,
            RoleId = StockAgentRoleIds.MarketAnalyst,
            RunIndex = 0,
            Status = ResearchRoleStatus.Running,
            StartedAt = staleAt
        };
        db.ResearchRoleStates.Add(role);
        await db.SaveChangesAsync();

        await db.SaveChangesAsync();
        return (session, turn, stage, role);
    }

    private static async Task<(ResearchSession Session, ResearchTurn Turn, ResearchStageSnapshot Stage, ResearchRoleState Role)> SeedClosedSessionWithStaleRunningChildrenAsync(
        AppDbContext db,
        string symbol = "SH600000")
    {
        var seeded = await SeedStaleRunningSessionAsync(db, symbol);
        seeded.Session.Status = ResearchSessionStatus.Closed;
        seeded.Session.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return seeded;
    }

    #region R1 ResearchSessionService

    [Fact]
    public async Task SubmitTurnAsync_NewSession_CreatesSessionAndTurn()
    {
        using var db = CreateDb();
        var svc = CreateSessionService(db);

        var result = await svc.SubmitTurnAsync(new ResearchTurnSubmitRequestDto("SH600000", "analyze", null, null));

        Assert.True(result.SessionId > 0);
        Assert.True(result.TurnId > 0);
        Assert.False(string.IsNullOrWhiteSpace(result.SessionKey));
        Assert.Equal("Queued", result.Status);

        var session = await db.ResearchSessions.FindAsync(result.SessionId);
        Assert.NotNull(session);
        Assert.Equal(ResearchSessionStatus.Idle, session!.Status);
        Assert.Equal("SH600000", session.Symbol);

        var turn = await db.ResearchTurns.FindAsync(result.TurnId);
        Assert.NotNull(turn);
        Assert.Equal(ResearchTurnStatus.Queued, turn!.Status);
    }

    [Fact]
    public async Task SubmitTurnAsync_NewSession_ClosesExistingActiveSessions()
    {
        using var db = CreateDb();
        var svc = CreateSessionService(db);

        var first = await svc.SubmitTurnAsync(new ResearchTurnSubmitRequestDto("SH600000", "first", null, null));
        var firstSession = await db.ResearchSessions.FindAsync(first.SessionId);
        firstSession!.Status = ResearchSessionStatus.Running;
        await db.SaveChangesAsync();

        var second = await svc.SubmitTurnAsync(new ResearchTurnSubmitRequestDto("SH600000", "second", null, "NewSession"));

        await db.Entry(firstSession).ReloadAsync();
        Assert.Equal(ResearchSessionStatus.Closed, firstSession.Status);

        var secondSession = await db.ResearchSessions.FindAsync(second.SessionId);
        Assert.Equal(ResearchSessionStatus.Idle, secondSession!.Status);
    }

    [Fact]
    public async Task SubmitTurnAsync_ContinueSession()
    {
        using var db = CreateDb();
        var svc = CreateSessionService(db);

        var initial = await svc.SubmitTurnAsync(new ResearchTurnSubmitRequestDto("SH600000", "init", null, null));
        var continued = await svc.SubmitTurnAsync(new ResearchTurnSubmitRequestDto("SH600000", "continue", initial.SessionKey, "ContinueSession"));

        Assert.Equal(initial.SessionId, continued.SessionId);
        Assert.NotEqual(initial.TurnId, continued.TurnId);
        Assert.Equal("Queued", continued.Status);

        var turnCount = await db.ResearchTurns.CountAsync(t => t.SessionId == initial.SessionId);
        Assert.Equal(2, turnCount);
    }

    [Fact]
    public async Task SubmitTurnAsync_ContinueSession_UsesRouterDecision()
    {
        using var db = CreateDb();
        var routingService = new StubFollowUpRoutingService
        {
            Result = new ResearchFollowUpRoutingDecision(
                ResearchContinuationMode.PartialRerun,
                4,
                "reuse_research_and_trade_plan",
                "重跑风险链路",
                "该追问聚焦风险。",
                0.82m)
        };
        var svc = CreateSessionService(db, routingService);

        var initial = await svc.SubmitTurnAsync(new ResearchTurnSubmitRequestDto("SH600000", "init", null, null));
        var continued = await svc.SubmitTurnAsync(new ResearchTurnSubmitRequestDto("SH600000", "继续分析一下风险", initial.SessionKey, "ContinueSession"));
        var turn = await db.ResearchTurns.FindAsync(continued.TurnId);

        Assert.NotNull(turn);
        Assert.Equal(ResearchContinuationMode.PartialRerun, turn!.ContinuationMode);
        Assert.Equal("4", turn.RerunScope);
        Assert.Equal("PartialRerun", turn.RoutingDecision);
        Assert.Equal("该追问聚焦风险。", turn.RoutingReasoning);
    }

    [Fact]
    public async Task SubmitTurnAsync_ContinueSession_NotFound_CreatesNewSession()
    {
        using var db = CreateDb();
        var svc = CreateSessionService(db);

        var result = await svc.SubmitTurnAsync(new ResearchTurnSubmitRequestDto("SH600000", "continue", "nonexistent-key", "ContinueSession"));

        Assert.NotNull(result);
        Assert.NotEqual("nonexistent-key", result.SessionKey);
        var session = await db.ResearchSessions.FindAsync(result.SessionId);
        Assert.NotNull(session);
        Assert.Equal("SH600000", session!.Symbol);
    }

    [Fact]
    public async Task SubmitTurnAsync_ContinueSession_SymbolMismatch_CreatesNewSession()
    {
        using var db = CreateDb();
        var svc = CreateSessionService(db);

        // Create a session for SH600000
        var original = await svc.SubmitTurnAsync(new ResearchTurnSubmitRequestDto("SH600000", "analyze", null, null));

        // Continue with a different symbol — should create a new session, not reuse the old one
        var result = await svc.SubmitTurnAsync(new ResearchTurnSubmitRequestDto("SZ000001", "analyze risk", original.SessionKey, "ContinueSession"));

        Assert.NotNull(result);
        Assert.NotEqual(original.SessionKey, result.SessionKey);
        Assert.NotEqual(original.SessionId, result.SessionId);
        var newSession = await db.ResearchSessions.FindAsync(result.SessionId);
        Assert.NotNull(newSession);
        Assert.Equal("SZ000001", newSession!.Symbol);
    }

    [Fact]
    public async Task GetActiveSessionAsync_ReturnsRunningSession()
    {
        using var db = CreateDb();
        var svc = CreateSessionService(db);

        var submit = await svc.SubmitTurnAsync(new ResearchTurnSubmitRequestDto("SH600000", "analyze", null, null));
        var session = await db.ResearchSessions.FindAsync(submit.SessionId);
        session!.Status = ResearchSessionStatus.Running;
        await db.SaveChangesAsync();

        var active = await svc.GetActiveSessionAsync("SH600000");

        Assert.NotNull(active);
        Assert.Equal(submit.SessionId, active!.SessionId);
        Assert.Equal("Running", active.Status);
    }

    [Fact]
    public async Task ZombieCleanupService_CleansStaleRunningHierarchy()
    {
        using var db = CreateDb();
        var (session, turn, stage, role) = await SeedStaleRunningSessionAsync(db);
        var cleanup = CreateZombieCleanupService(db);

        var result = await cleanup.CleanupStaleRunningAsync(
            ResearchZombieCleanupService.QueryStaleThreshold,
            sessionId: session.Id);

        Assert.True(result.HasChanges);
        Assert.Equal(1, result.SessionCount);
        Assert.Equal(1, result.TurnCount);
        Assert.Equal(1, result.StageCount);
        Assert.Equal(1, result.RoleCount);

        await db.Entry(session).ReloadAsync();
        await db.Entry(turn).ReloadAsync();
        await db.Entry(stage).ReloadAsync();
        await db.Entry(role).ReloadAsync();

        Assert.Equal(ResearchSessionStatus.Failed, session.Status);
        Assert.Null(session.ActiveTurnId);
        Assert.Null(session.ActiveStage);
        Assert.Equal(ResearchTurnStatus.Failed, turn.Status);
        Assert.NotNull(turn.CompletedAt);
        Assert.Equal(ResearchStageStatus.Failed, stage.Status);
        Assert.NotNull(stage.CompletedAt);
        Assert.Equal(ResearchRoleStatus.Failed, role.Status);
        Assert.NotNull(role.CompletedAt);
        Assert.Equal("STALE_RUNNING_RECOVERED", role.ErrorCode);
    }

    [Fact]
    public async Task ZombieCleanupService_CleansClosedSession_WithStaleRunningHierarchy()
    {
        using var db = CreateDb();
        var (session, turn, stage, role) = await SeedClosedSessionWithStaleRunningChildrenAsync(db);
        var cleanup = CreateZombieCleanupService(db);

        var result = await cleanup.CleanupStaleRunningAsync(
            ResearchZombieCleanupService.QueryStaleThreshold,
            sessionId: session.Id);

        Assert.True(result.HasChanges);
        Assert.Equal(1, result.SessionCount);
        Assert.Equal(1, result.TurnCount);
        Assert.Equal(1, result.StageCount);
        Assert.Equal(1, result.RoleCount);

        await db.Entry(session).ReloadAsync();
        await db.Entry(turn).ReloadAsync();
        await db.Entry(stage).ReloadAsync();
        await db.Entry(role).ReloadAsync();

        Assert.Equal(ResearchSessionStatus.Closed, session.Status);
        Assert.Null(session.ActiveTurnId);
        Assert.Null(session.ActiveStage);
        Assert.Equal(ResearchTurnStatus.Failed, turn.Status);
        Assert.NotNull(turn.CompletedAt);
        Assert.Equal(ResearchStageStatus.Failed, stage.Status);
        Assert.NotNull(stage.CompletedAt);
        Assert.Equal(ResearchRoleStatus.Failed, role.Status);
        Assert.NotNull(role.CompletedAt);
        Assert.Equal("STALE_RUNNING_RECOVERED", role.ErrorCode);
    }

    [Fact]
    public async Task GetActiveSessionAsync_RecoversStaleRunningSession()
    {
        using var db = CreateDb();
        var (session, turn, _, _) = await SeedStaleRunningSessionAsync(db);
        var svc = CreateSessionService(db);

        var active = await svc.GetActiveSessionAsync(session.Symbol);

        Assert.Null(active);

        await db.Entry(session).ReloadAsync();
        await db.Entry(turn).ReloadAsync();

        Assert.Equal(ResearchSessionStatus.Failed, session.Status);
        Assert.Equal(ResearchTurnStatus.Failed, turn.Status);
        Assert.Null(session.ActiveTurnId);
        Assert.Null(session.ActiveStage);
    }

    [Fact]
    public async Task GetActiveSessionAsync_ReturnsNull_WhenNoActive()
    {
        using var db = CreateDb();
        var svc = CreateSessionService(db);

        var active = await svc.GetActiveSessionAsync("SH999999");
        Assert.Null(active);
    }

    [Fact]
    public async Task ListSessionsAsync_ReturnsOrderedByDate()
    {
        using var db = CreateDb();
        var svc = CreateSessionService(db);

        await svc.SubmitTurnAsync(new ResearchTurnSubmitRequestDto("SH600000", "first", null, null));
        await Task.Delay(10);
        await svc.SubmitTurnAsync(new ResearchTurnSubmitRequestDto("SH600000", "second", null, "NewSession"));

        var list = await svc.ListSessionsAsync("SH600000");

        Assert.Equal(2, list.Count);
        Assert.True(list[0].UpdatedAt >= list[1].UpdatedAt);
    }

    [Fact]
    public async Task SubmitTurnAsync_NewSession_RecoversStaleRunningSessionBeforeFreshSession()
    {
        using var db = CreateDb();
        var (staleSession, staleTurn, _, _) = await SeedStaleRunningSessionAsync(db);
        var svc = CreateSessionService(db);

        var submit = await svc.SubmitTurnAsync(new ResearchTurnSubmitRequestDto("SH600000", "fresh session", null, "NewSession"));

        await db.Entry(staleSession).ReloadAsync();
        await db.Entry(staleTurn).ReloadAsync();

        Assert.NotEqual(staleSession.Id, submit.SessionId);
        Assert.Equal(ResearchSessionStatus.Failed, staleSession.Status);
        Assert.Equal(ResearchTurnStatus.Failed, staleTurn.Status);
        Assert.Null(staleSession.ActiveTurnId);
        Assert.Null(staleSession.ActiveStage);

        var freshSession = await db.ResearchSessions.FindAsync(submit.SessionId);
        Assert.NotNull(freshSession);
        Assert.Equal(ResearchSessionStatus.Idle, freshSession!.Status);
    }

    [Fact]
    public async Task GetSessionDetailAsync_ReturnsFullHierarchy()
    {
        using var db = CreateDb();
        var svc = CreateSessionService(db);

        var submit = await svc.SubmitTurnAsync(new ResearchTurnSubmitRequestDto("SH600000", "full analysis", null, null));

        var stage = new ResearchStageSnapshot
        {
            TurnId = submit.TurnId,
            StageType = ResearchStageType.CompanyOverviewPreflight,
            StageRunIndex = 0,
            ExecutionMode = ResearchStageExecutionMode.Sequential,
            Status = ResearchStageStatus.Completed,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };
        db.ResearchStageSnapshots.Add(stage);
        await db.SaveChangesAsync();

        var roleState = new ResearchRoleState
        {
            StageId = stage.Id,
            RoleId = StockAgentRoleIds.CompanyOverviewAnalyst,
            RunIndex = 0,
            Status = ResearchRoleStatus.Completed,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };
        db.ResearchRoleStates.Add(roleState);
        await db.SaveChangesAsync();

        var detail = await svc.GetSessionDetailAsync(submit.SessionId);

        Assert.NotNull(detail);
        Assert.Equal("SH600000", detail!.Symbol);
        Assert.Single(detail.Turns);
        Assert.Equal("full analysis", detail.Turns[0].UserPrompt);
    }

    [Fact]
    public async Task GetSessionDetailAsync_UsesSplitQueries_ForRelationalProvider()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var cleanupDb = CreateDb();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(warnings => warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning))
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var session = new ResearchSession
        {
            SessionKey = Guid.NewGuid().ToString("N"),
            Symbol = "SH600000",
            Name = "full analysis",
            Status = ResearchSessionStatus.Idle,
            LastUserIntent = "full analysis",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ResearchSessions.Add(session);
        await db.SaveChangesAsync();

        var turn = new ResearchTurn
        {
            SessionId = session.Id,
            TurnIndex = 0,
            UserPrompt = "full analysis",
            Status = ResearchTurnStatus.Queued,
            ContinuationMode = ResearchContinuationMode.NewSession,
            RequestedAt = now
        };
        db.ResearchTurns.Add(turn);
        await db.SaveChangesAsync();

        session.ActiveTurnId = turn.Id;
        await db.SaveChangesAsync();

        var svc = new ResearchSessionService(
            db,
            new ResearchEventBus(),
            new StubFollowUpRoutingService(),
            CreateZombieCleanupService(cleanupDb),
            NullLogger<ResearchSessionService>.Instance);

        var stage = new ResearchStageSnapshot
        {
            TurnId = turn.Id,
            StageType = ResearchStageType.CompanyOverviewPreflight,
            StageRunIndex = 0,
            ExecutionMode = ResearchStageExecutionMode.Sequential,
            Status = ResearchStageStatus.Completed,
            StartedAt = now,
            CompletedAt = now
        };
        db.ResearchStageSnapshots.Add(stage);
        await db.SaveChangesAsync();

        db.ResearchRoleStates.Add(new ResearchRoleState
        {
            StageId = stage.Id,
            RoleId = StockAgentRoleIds.CompanyOverviewAnalyst,
            RunIndex = 0,
            Status = ResearchRoleStatus.Completed,
            StartedAt = now,
            CompletedAt = now
        });
        db.ResearchFeedItems.Add(new ResearchFeedItem
        {
            TurnId = turn.Id,
            StageId = stage.Id,
            RoleId = StockAgentRoleIds.CompanyOverviewAnalyst,
            ItemType = ResearchFeedItemType.RoleMessage,
            Content = "feed item",
            CreatedAt = now
        });
        db.ResearchReportSnapshots.Add(new ResearchReportSnapshot
        {
            SessionId = session.Id,
            TurnId = turn.Id,
            TriggeredByStageId = stage.Id,
            VersionIndex = 0,
            IsFinal = false,
            ReportBlocksJson = "[]",
            CreatedAt = now
        });
        db.ResearchDecisionSnapshots.Add(new ResearchDecisionSnapshot
        {
            SessionId = session.Id,
            TurnId = turn.Id,
            Rating = "Hold",
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        var detail = await svc.GetSessionDetailAsync(session.Id);

        Assert.NotNull(detail);
        Assert.Single(detail!.Turns);
        Assert.Single(detail.StageSnapshots);
        Assert.Single(detail.StageSnapshots[0].RoleStates);
        Assert.Single(detail.FeedItems);
        Assert.Single(detail.Reports);
        Assert.Single(detail.Decisions);
    }

    [Theory]
    [InlineData(null, "NewSession")]
    [InlineData("", "NewSession")]
    [InlineData("ContinueSession", "ContinueSession")]
    [InlineData("NewSession", "NewSession")]
    [InlineData("PartialRerun", "PartialRerun")]
    [InlineData("invalid-mode", "NewSession")]
    public async Task ParseContinuationMode_Defaults(string? input, string expectedStatus)
    {
        using var db = CreateDb();
        var svc = CreateSessionService(db);

        string? sessionKey = null;
        if (input == "ContinueSession")
        {
            var initial = await svc.SubmitTurnAsync(new ResearchTurnSubmitRequestDto("SH600000", "seed", null, null));
            sessionKey = initial.SessionKey;
        }

        var result = await svc.SubmitTurnAsync(new ResearchTurnSubmitRequestDto("SH600000", "test", sessionKey, input));
        var turn = await db.ResearchTurns.FindAsync(result.TurnId);

        Assert.Equal(expectedStatus, turn!.ContinuationMode.ToString());
    }

    #endregion

    #region R3 ResearchEventBus

    [Fact]
    public void EventBus_PublishAndDrain()
    {
        var bus = new ResearchEventBus();
        var evt1 = new ResearchEvent(ResearchEventType.TurnStarted, 1, 10, null, null, null, "started", null, DateTime.UtcNow);
        var evt2 = new ResearchEvent(ResearchEventType.StageStarted, 1, 10, 100, null, null, "stage", null, DateTime.UtcNow);

        bus.Publish(evt1);
        bus.Publish(evt2);

        var drained = bus.Drain(10);
        Assert.Equal(2, drained.Count);
        Assert.Equal("started", drained[0].Summary);
        Assert.Equal("stage", drained[1].Summary);

        var second = bus.Drain(10);
        Assert.Empty(second);
    }

    [Fact]
    public void EventBus_DrainEmpty_ReturnsEmpty()
    {
        var bus = new ResearchEventBus();
        var result = bus.Drain(999);
        Assert.Empty(result);
    }

    #endregion

    #region R3 ResearchRoleExecutor

    [Fact]
    public async Task RoleExecutor_AnalystRole_DispatchesToolsAndCallsLlm()
    {
        var gateway = new StubMcpToolGateway();
        var policy = new StubRoleToolPolicyService();
        var registry = new StubContractRegistry();
        var llm = new StubLlmService();
        var bus = new ResearchEventBus();

        registry.Register(new StockCopilotRoleContractDto(
            StockAgentRoleIds.CompanyOverviewAnalyst, "Company Overview Analyst", "analyst",
            "local_required",
            new[] { StockMcpToolNames.CompanyOverview, StockMcpToolNames.MarketContext },
            "", "", 1, true, null));

        var executor = new ResearchRoleExecutor(gateway, policy, registry, llm, bus, NullLogger<ResearchRoleExecutor>.Instance);

        var ctx = new RoleExecutionContext(1, 10, 100, "SH600000",
            StockAgentRoleIds.CompanyOverviewAnalyst, "analyze", Array.Empty<string>());

        var result = await executor.ExecuteRoleAsync(ctx);

        Assert.Equal(ResearchRoleStatus.Completed, result.Status);
        Assert.NotNull(result.OutputContentJson);
        Assert.NotNull(result.OutputRefsJson);
        Assert.NotNull(result.LlmTraceId);
        Assert.Equal(2, gateway.CallCount);
        Assert.Contains(StockMcpToolNames.CompanyOverview, gateway.CalledTools);
        Assert.Contains(StockMcpToolNames.MarketContext, gateway.CalledTools);
    }

    [Fact]
    public async Task RoleExecutor_FundamentalsRole_DispatchesFinancialToolsWithoutUnknownToolErrors()
    {
        var gateway = new StubMcpToolGateway();
        var policy = new StubRoleToolPolicyService();
        var registry = new StockAgentRoleContractRegistry();
        var llm = new StubLlmService();
        var bus = new ResearchEventBus();

        var executor = new ResearchRoleExecutor(gateway, policy, registry, llm, bus, NullLogger<ResearchRoleExecutor>.Instance);

        var ctx = new RoleExecutionContext(2, 20, 200, "SH600000",
            StockAgentRoleIds.FundamentalsAnalyst, "analyze fundamentals", Array.Empty<string>());

        var result = await executor.ExecuteRoleAsync(ctx);

        Assert.Equal(ResearchRoleStatus.Completed, result.Status);
        Assert.Empty(result.DegradedFlags);
        Assert.Equal(6, gateway.ToolCalls.Count);
        Assert.Equal(6, gateway.CalledTools.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(StockMcpToolNames.Fundamentals, gateway.CalledTools);
        Assert.Contains(StockMcpToolNames.FinancialReport, gateway.CalledTools);
        Assert.Contains(StockMcpToolNames.FinancialTrend, gateway.CalledTools);
        Assert.Contains(StockMcpToolNames.FinancialReportRag, gateway.CalledTools);
        Assert.Contains(StockMcpToolNames.CompanyOverview, gateway.CalledTools);
        Assert.Contains(StockMcpToolNames.MarketContext, gateway.CalledTools);
        Assert.Contains(gateway.ToolCalls, item => item == (StockMcpToolNames.FinancialReport, "research:2:20:sh600000::FinancialReportMcp"));
        Assert.Contains(gateway.ToolCalls, item => item == (StockMcpToolNames.FinancialTrend, "research:2:20:sh600000::FinancialTrendMcp"));

        using var outputRefs = JsonDocument.Parse(result.OutputRefsJson!);
        var refsByTool = outputRefs.RootElement
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("toolName").GetString()!,
                item => item,
                StringComparer.Ordinal);

        Assert.Equal("Completed", refsByTool[StockMcpToolNames.FinancialReport].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, refsByTool[StockMcpToolNames.FinancialReport].GetProperty("errorMessage").ValueKind);
        Assert.Equal("Completed", refsByTool[StockMcpToolNames.FinancialTrend].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, refsByTool[StockMcpToolNames.FinancialTrend].GetProperty("errorMessage").ValueKind);
    }

    [Fact]
    public async Task RoleExecutor_BackOfficeRole_SkipsTools()
    {
        var gateway = new StubMcpToolGateway();
        var policy = new StubRoleToolPolicyService();
        var registry = new StubContractRegistry();
        var llm = new StubLlmService();
        var bus = new ResearchEventBus();

        registry.Register(new StockCopilotRoleContractDto(
            StockAgentRoleIds.BullResearcher, "Bull Researcher", "debate",
            "none", Array.Empty<string>(),
            "", "", 0, false, null));

        var executor = new ResearchRoleExecutor(gateway, policy, registry, llm, bus, NullLogger<ResearchRoleExecutor>.Instance);

        var ctx = new RoleExecutionContext(1, 10, 100, "SH600000",
            StockAgentRoleIds.BullResearcher, "analyze", Array.Empty<string>());

        var result = await executor.ExecuteRoleAsync(ctx);

        Assert.Equal(ResearchRoleStatus.Completed, result.Status);
        Assert.Equal(0, gateway.CallCount);
        Assert.NotNull(result.OutputContentJson);
    }

    [Fact]
    public async Task RoleExecutor_ToolFailure_Degrades()
    {
        var gateway = new StubMcpToolGateway { ShouldThrow = true };
        var policy = new StubRoleToolPolicyService();
        var registry = new StubContractRegistry();
        var llm = new StubLlmService();
        var bus = new ResearchEventBus();

        registry.Register(new StockCopilotRoleContractDto(
            StockAgentRoleIds.MarketAnalyst, "Market Analyst", "analyst",
            "local_preferred",
            new[] { StockMcpToolNames.MarketContext },
            "", "", 1, true, null));

        var executor = new ResearchRoleExecutor(gateway, policy, registry, llm, bus, NullLogger<ResearchRoleExecutor>.Instance);

        var ctx = new RoleExecutionContext(1, 10, 100, "SH600000",
            StockAgentRoleIds.MarketAnalyst, "analyze", Array.Empty<string>());

        var result = await executor.ExecuteRoleAsync(ctx);

        Assert.Equal(ResearchRoleStatus.Degraded, result.Status);
        Assert.NotNull(result.OutputContentJson);
        Assert.True(result.DegradedFlags.Count > 0);
        Assert.Contains(result.DegradedFlags, f => f.Contains("tool_error"));
    }

    [Fact]
    public async Task RoleExecutor_ShouldPassTurnScopedTaskIdsToResearchTools()
    {
        var gateway = new StubMcpToolGateway();
        var policy = new StubRoleToolPolicyService();
        var registry = new StubContractRegistry();
        var llm = new StubLlmService();
        var bus = new ResearchEventBus();

        registry.Register(new StockCopilotRoleContractDto(
            StockAgentRoleIds.MarketAnalyst, "Market Analyst", "analyst",
            "local_required",
            new[] { StockMcpToolNames.Kline, StockMcpToolNames.Minute, StockMcpToolNames.Strategy },
            "", "", 1, true, null));

        var executor = new ResearchRoleExecutor(gateway, policy, registry, llm, bus, NullLogger<ResearchRoleExecutor>.Instance);

        var ctx = new RoleExecutionContext(1, 10, 100, "SH600000",
            StockAgentRoleIds.MarketAnalyst, "analyze", Array.Empty<string>());

        var result = await executor.ExecuteRoleAsync(ctx);

        Assert.Equal(ResearchRoleStatus.Completed, result.Status);
        Assert.Equal(3, gateway.ToolCalls.Count);

        var taskIds = gateway.ToolCalls.Select(item => item.TaskId).Where(item => item is not null).Cast<string>().ToArray();
        Assert.Equal(3, taskIds.Length);
        Assert.Equal(3, taskIds.Distinct(StringComparer.Ordinal).Count());

        var scopeKeys = taskIds
            .Select(taskId => taskId.Split("::", 2, StringSplitOptions.None)[0])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Single(scopeKeys);
        Assert.Equal("research:1:10:sh600000", scopeKeys[0]);
    }

    [Fact]
    public void RoleExecutor_ResolveToolTimeout_UsesLongerBudgetForMarketContext()
    {
        var timeout = ResearchRoleExecutor.ResolveToolTimeout(StockMcpToolNames.MarketContext);

        Assert.Equal(TimeSpan.FromSeconds(90), timeout);
    }

    [Fact]
    public void RoleExecutor_ResolveToolTimeout_KeepsDefaultBudgetForOtherTools()
    {
        var companyOverviewTimeout = ResearchRoleExecutor.ResolveToolTimeout(StockMcpToolNames.CompanyOverview);
        var unknownToolTimeout = ResearchRoleExecutor.ResolveToolTimeout("UnknownTool");

        Assert.Equal(TimeSpan.FromSeconds(45), companyOverviewTimeout);
        Assert.Equal(TimeSpan.FromSeconds(45), unknownToolTimeout);
    }

    [Fact]
    public async Task RoleExecutor_LlmFailure_ReturnsFailed()
    {
        var gateway = new StubMcpToolGateway();
        var policy = new StubRoleToolPolicyService();
        var registry = new StubContractRegistry();
        var llm = new StubLlmService { ShouldThrow = true };
        var bus = new ResearchEventBus();

        registry.Register(new StockCopilotRoleContractDto(
            StockAgentRoleIds.CompanyOverviewAnalyst, "Company Overview Analyst", "analyst",
            "local_required", new[] { StockMcpToolNames.CompanyOverview },
            "", "", 0, true, null));

        var executor = new ResearchRoleExecutor(gateway, policy, registry, llm, bus, NullLogger<ResearchRoleExecutor>.Instance);

        var ctx = new RoleExecutionContext(1, 10, 100, "SH600000",
            StockAgentRoleIds.CompanyOverviewAnalyst, "analyze", Array.Empty<string>());

        var result = await executor.ExecuteRoleAsync(ctx);

        Assert.Equal(ResearchRoleStatus.Failed, result.Status);
        Assert.Equal("LLM_FAILED", result.ErrorCode);
        Assert.Null(result.OutputContentJson);
    }

    [Fact]
    public void RoleExecutor_BuildUserContent_LocalGovernance_CompactsArtifactsAndToolResults()
    {
        var longNarrative = string.Join(' ', Enumerable.Repeat("超长证据片段", 220));
        var upstreamArtifact = $"[bull_researcher]\n{JsonSerializer.Serialize(new
        {
            content = JsonSerializer.Serialize(new
            {
                claim = "看多结论",
                summary = "核心结论保持正向。",
                rating = "Buy",
                action = "buy",
                confidence = 0.82,
                key_points = Enumerable.Range(0, 12).Select(index => $"要点-{index}").ToArray(),
                evidence_refs = Enumerable.Range(0, 20).Select(index => $"evidence-ref-{index}").ToArray(),
                risk_limits = Enumerable.Range(0, 12).Select(index => $"risk-{index}").ToArray(),
                invalidations = Enumerable.Range(0, 12).Select(index => $"invalid-{index}").ToArray(),
                evidence_details = Enumerable.Range(0, 20).Select(index => new { detail = $"detail-{index}-{longNarrative}" }).ToArray(),
                converged = true
            })
        })}";
        var toolResult = $"[MarketContextMcp]\n{JsonSerializer.Serialize(new
        {
            degradedFlags = Array.Empty<string>(),
            data = new
            {
                stageLabel = "主升",
                stageConfidence = 78,
                mainlineSectorName = "AI 算力",
                boardItems = Enumerable.Range(0, 40).Select(index => new
                {
                    name = $"sector-{index}",
                    reason = $"reason-{index}-{longNarrative}"
                }).ToArray(),
                realtimeSeries = Enumerable.Range(0, 80).ToArray()
            },
            evidence = Enumerable.Range(0, 12).Select(index => new
            {
                title = $"title-{index}",
                source = "eastmoney",
                publishedAt = $"2026-04-08T0{index % 10}:00:00Z",
                summary = longNarrative
            }).ToArray()
        })}";
        var governance = ResearchRoleExecutor.CreateLocalPromptGovernance(2048, "ollama", "gemma4:e2b");
        var context = new RoleExecutionContext(
            1,
            10,
            100,
            "SH600000",
            StockAgentRoleIds.PortfolioManager,
            "请给出完整研究结论",
            new[] { upstreamArtifact });

        var prompt = ResearchRoleExecutor.BuildUserContent(context, new[] { toolResult }, governance, out var stats);

        Assert.NotNull(stats);
        Assert.True(stats!.OriginalUpstreamChars > 0);
        Assert.True(stats.OriginalToolChars > 0);
        Assert.DoesNotContain("本地模型上下文压缩说明", prompt);
        Assert.Contains("\"claim\":\"看多结论\"", prompt);
        Assert.Contains("\"risk_limits\"", prompt);
        Assert.Contains("\"invalidations\"", prompt);
        Assert.Contains("\"evidence\"", prompt);
        Assert.Contains("evidence_details", prompt);
    }

    [Fact]
    public async Task RoleExecutor_OllamaProvider_UsesGovernedPromptAssembly()
    {
        var gateway = new StubMcpToolGateway();
        var policy = new StubRoleToolPolicyService();
        var registry = new StubContractRegistry();
        var llm = new StubLlmService();
        var bus = new ResearchEventBus();
        var settingsStore = new StubLlmSettingsStore(
            "ollama",
            new LlmProviderSettings
            {
                Provider = "ollama",
                ProviderType = "ollama",
                Model = "gemma4:e2b",
                OllamaNumCtx = 2048,
                Enabled = true
            });

        registry.Register(new StockCopilotRoleContractDto(
            StockAgentRoleIds.BullResearcher, "Bull Researcher", "debate",
            "none", Array.Empty<string>(),
            "", "", 0, false, null));

        var executor = new ResearchRoleExecutor(gateway, policy, registry, llm, bus, NullLogger<ResearchRoleExecutor>.Instance, settingsStore);
        var longNarrative = string.Join(' ', Enumerable.Range(0, 320).Select(index => $"local-marker-{index:D3}"));
        var upstreamArtifact = $"[bull_researcher]\n{JsonSerializer.Serialize(new
        {
            content = JsonSerializer.Serialize(new
            {
                claim = "继续看多",
                analysis = longNarrative,
                evidence_details = Enumerable.Range(0, 16).Select(index => new { detail = $"detail-{index}-{longNarrative}" }).ToArray()
            })
        })}";

        var result = await executor.ExecuteRoleAsync(new RoleExecutionContext(
            1,
            10,
            100,
            "SH600000",
            StockAgentRoleIds.BullResearcher,
            "继续做完整研究",
            new[] { upstreamArtifact }));

        Assert.Equal(ResearchRoleStatus.Completed, result.Status);
        Assert.NotNull(llm.LastRequest);
        Assert.Contains("继续看多", llm.LastRequest!.Prompt);
        Assert.DoesNotContain("本地模型上下文压缩说明", llm.LastRequest.Prompt);
        Assert.Contains("local-marker-319", llm.LastRequest.Prompt);
    }

    [Fact]
    public async Task RoleExecutor_MissingProviderSettings_UsesFailSafeGovernedPromptAssembly()
    {
        var gateway = new StubMcpToolGateway();
        var policy = new StubRoleToolPolicyService();
        var registry = new StubContractRegistry();
        var llm = new StubLlmService();
        var bus = new ResearchEventBus();
        var settingsStore = new StubLlmSettingsStore("broken-provider");

        registry.Register(new StockCopilotRoleContractDto(
            StockAgentRoleIds.BullResearcher, "Bull Researcher", "debate",
            "none", Array.Empty<string>(),
            "", "", 0, false, null));

        var executor = new ResearchRoleExecutor(gateway, policy, registry, llm, bus, NullLogger<ResearchRoleExecutor>.Instance, settingsStore);
        var longNarrative = string.Join(' ', Enumerable.Range(0, 320).Select(index => $"failsafe-marker-{index:D3}"));
        var upstreamArtifact = $"[bull_researcher]\n{JsonSerializer.Serialize(new
        {
            content = JsonSerializer.Serialize(new
            {
                claim = "继续看多",
                analysis = longNarrative,
                evidence_details = Enumerable.Range(0, 16).Select(index => new { detail = $"detail-{index}-{longNarrative}" }).ToArray()
            })
        })}";

        var result = await executor.ExecuteRoleAsync(new RoleExecutionContext(
            1,
            10,
            100,
            "SH600000",
            StockAgentRoleIds.BullResearcher,
            "继续做完整研究",
            new[] { upstreamArtifact }));

        Assert.Equal(ResearchRoleStatus.Completed, result.Status);
        Assert.NotNull(llm.LastRequest);
        Assert.Contains("继续看多", llm.LastRequest!.Prompt);
        Assert.DoesNotContain("本地模型上下文压缩说明", llm.LastRequest.Prompt);
        Assert.Contains("failsafe-marker-319", llm.LastRequest.Prompt);
    }

    [Fact]
    public async Task RoleExecutor_NonOllamaProvider_KeepsRawPromptAssembly()
    {
        var gateway = new StubMcpToolGateway();
        var policy = new StubRoleToolPolicyService();
        var registry = new StubContractRegistry();
        var llm = new StubLlmService();
        var bus = new ResearchEventBus();
        var settingsStore = new StubLlmSettingsStore(
            "openai",
            new LlmProviderSettings
            {
                Provider = "openai",
                ProviderType = "openai",
                Model = "gpt-4o-mini",
                Enabled = true
            });

        registry.Register(new StockCopilotRoleContractDto(
            StockAgentRoleIds.BullResearcher, "Bull Researcher", "debate",
            "none", Array.Empty<string>(),
            "", "", 0, false, null));

        var executor = new ResearchRoleExecutor(gateway, policy, registry, llm, bus, NullLogger<ResearchRoleExecutor>.Instance, settingsStore);
        var longNarrative = string.Join(' ', Enumerable.Range(0, 180).Select(index => $"cloud-marker-{index:D3}"));
        var upstreamArtifact = $"[bull_researcher]\n{JsonSerializer.Serialize(new
        {
            content = JsonSerializer.Serialize(new
            {
                claim = "继续看多",
                analysis = longNarrative
            })
        })}";

        var result = await executor.ExecuteRoleAsync(new RoleExecutionContext(
            1,
            10,
            100,
            "SH600000",
            StockAgentRoleIds.BullResearcher,
            "继续做完整研究",
            new[] { upstreamArtifact }));

        Assert.Equal(ResearchRoleStatus.Completed, result.Status);
        Assert.NotNull(llm.LastRequest);
        Assert.Contains("cloud-marker-179", llm.LastRequest!.Prompt);
        Assert.DoesNotContain("本地模型上下文压缩说明", llm.LastRequest.Prompt);
    }

    [Fact]
    public void RoleExecutor_BuildUserContent_LocalGovernance_BoundsMalformedFallbacks()
    {
        var longNarrative = string.Join(' ', Enumerable.Range(0, 260).Select(index => $"malformed-marker-{index:D3}"));
        var malformedUpstreamArtifact = $"[bull_researcher]\n{{\"content\":\"{longNarrative}";
        var malformedToolResult = $"[MarketContextMcp]\n{{\"data\":{{\"stageLabel\":\"主升\",\"analysis\":\"{longNarrative}\"}}";
        var governance = ResearchRoleExecutor.CreateLocalPromptGovernance(2048, "ollama", "gemma4:e2b");
        var context = new RoleExecutionContext(
            1,
            10,
            100,
            "SH600000",
            StockAgentRoleIds.PortfolioManager,
            "请给出完整研究结论",
            new[] { malformedUpstreamArtifact });

        var prompt = ResearchRoleExecutor.BuildUserContent(context, new[] { malformedToolResult }, governance, out var stats);

        Assert.NotNull(stats);
        Assert.True(stats!.OriginalUpstreamChars > 0);
        Assert.True(stats.OriginalToolChars > 0);
        // Malformed JSON cannot be parsed — passes through unchanged
        Assert.Contains("malformed-marker-000", prompt);
        Assert.Contains("malformed-marker-259", prompt);
    }

    [Fact]
    public void RoleExecutor_BuildUserContent_LocalGovernance_KeepsSmallNoisyArraysIntact()
    {
        var toolResult = $"[MarketContextMcp]\n{JsonSerializer.Serialize(new
        {
            data = new
            {
                boardItems = Enumerable.Range(0, 6).Select(index => new
                {
                    name = $"small-sector-{index}",
                    reason = $"reason-{index}"
                }).ToArray(),
                realtimeSeries = Enumerable.Range(0, 24).ToArray()
            }
        })}";
        var governance = ResearchRoleExecutor.CreateLocalPromptGovernance(2048, "ollama", "gemma4:e2b");
        var context = new RoleExecutionContext(
            1,
            10,
            100,
            "SH600000",
            StockAgentRoleIds.PortfolioManager,
            "请给出完整研究结论",
            Array.Empty<string>());

        var prompt = ResearchRoleExecutor.BuildUserContent(context, new[] { toolResult }, governance, out var stats);

        Assert.NotNull(stats);
        Assert.True(stats!.OriginalToolChars > 0);
        Assert.True(stats.CompactedToolChars > 0);
        Assert.Contains("\"boardItems\":[", prompt);
        Assert.DoesNotContain("\"boardItems\":{\"count\":6", prompt);
        Assert.Contains("small-sector-0", prompt);
        Assert.Contains("small-sector-5", prompt);
        Assert.Contains("\"realtimeSeries\":[0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23]", prompt);
        Assert.DoesNotContain("\"realtimeSeries\":{\"count\":", prompt);
    }

    #endregion

    #region R3 ResearchRunner

    private static (AppDbContext db, ResearchSession session, ResearchTurn turn) SeedTurnForRunner()
    {
        var db = CreateDb();
        var session = new ResearchSession
        {
            SessionKey = Guid.NewGuid().ToString("N"),
            Symbol = "SH600000",
            Name = "Test Session",
            Status = ResearchSessionStatus.Idle,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.ResearchSessions.Add(session);
        db.SaveChanges();

        var turn = new ResearchTurn
        {
            SessionId = session.Id,
            TurnIndex = 0,
            UserPrompt = "test research",
            Status = ResearchTurnStatus.Queued,
            ContinuationMode = ResearchContinuationMode.NewSession,
            RequestedAt = DateTime.UtcNow
        };
        db.ResearchTurns.Add(turn);
        db.SaveChanges();

        session.ActiveTurnId = turn.Id;
        db.SaveChanges();

        return (db, session, turn);
    }

    private static StubLlmSettingsStore CreateSettingsStore(string activeProviderKey, string providerType, string model = "test-model")
    {
        return new StubLlmSettingsStore(
            activeProviderKey,
            new LlmProviderSettings
            {
                Provider = activeProviderKey,
                ProviderType = providerType,
                Model = model
            });
    }

    [Fact]
    public async Task Runner_FullPipeline_6Stages()
    {
        var (db, session, turn) = SeedTurnForRunner();
        using (db)
        {
            var executor = new StubRoleExecutor();
            var bus = new ResearchEventBus();
            var runner = new ResearchRunner(db, executor, bus, new NullReportService(), new StubFollowUpRoutingService(), NullLogger<ResearchRunner>.Instance);

            await runner.RunTurnAsync(turn.Id);

            await db.Entry(turn).ReloadAsync();
            Assert.Equal(ResearchTurnStatus.Completed, turn.Status);
            Assert.NotNull(turn.CompletedAt);

            var stages = await db.ResearchStageSnapshots
                .Where(s => s.TurnId == turn.Id)
                .OrderBy(s => s.StageRunIndex)
                .ToListAsync();

            Assert.Equal(6, stages.Count);
            Assert.Equal(ResearchStageType.CompanyOverviewPreflight, stages[0].StageType);
            Assert.Equal(ResearchStageType.AnalystTeam, stages[1].StageType);
            Assert.Equal(ResearchStageType.ResearchDebate, stages[2].StageType);
            Assert.Equal(ResearchStageType.TraderProposal, stages[3].StageType);
            Assert.Equal(ResearchStageType.RiskDebate, stages[4].StageType);
            Assert.Equal(ResearchStageType.PortfolioDecision, stages[5].StageType);

            Assert.All(stages, s => Assert.Equal(ResearchStageStatus.Completed, s.Status));
        }
    }

    [Fact]
    public async Task Runner_FailFast_OnRequiredToolFailure()
    {
        var (db, session, turn) = SeedTurnForRunner();
        using (db)
        {
            var executor = new StubRoleExecutor();
            executor.ResultsByRoleId[StockAgentRoleIds.CompanyOverviewAnalyst] = new RoleExecutionResult(
                StockAgentRoleIds.CompanyOverviewAnalyst,
                ResearchRoleStatus.Failed,
                null, null, Array.Empty<string>(), "TOOL_BLOCKED", "Required tool blocked");

            var bus = new ResearchEventBus();
            var runner = new ResearchRunner(db, executor, bus, new NullReportService(), new StubFollowUpRoutingService(), NullLogger<ResearchRunner>.Instance);

            await runner.RunTurnAsync(turn.Id);

            await db.Entry(turn).ReloadAsync();
            Assert.Equal(ResearchTurnStatus.Failed, turn.Status);
            Assert.Contains("CompanyOverviewPreflight", turn.StopReason);

            var stages = await db.ResearchStageSnapshots.Where(s => s.TurnId == turn.Id).ToListAsync();
            Assert.Single(stages);
            Assert.Equal(ResearchStageStatus.Failed, stages[0].Status);
        }
    }

    [Fact]
    public async Task Runner_DegradedPath()
    {
        var (db, session, turn) = SeedTurnForRunner();
        using (db)
        {
            var executor = new StubRoleExecutor();
            executor.ResultsByRoleId[StockAgentRoleIds.MarketAnalyst] = new RoleExecutionResult(
                StockAgentRoleIds.MarketAnalyst,
                ResearchRoleStatus.Degraded,
                "{\"content\":\"degraded output\"}",
                "trace-degraded",
                new[] { "tool_error:MarketContextMcp" },
                null, null);

            var bus = new ResearchEventBus();
            var runner = new ResearchRunner(db, executor, bus, new NullReportService(), new StubFollowUpRoutingService(), NullLogger<ResearchRunner>.Instance);

            await runner.RunTurnAsync(turn.Id);

            await db.Entry(turn).ReloadAsync();
            await db.Entry(session).ReloadAsync();

            Assert.Equal(ResearchTurnStatus.Completed, turn.Status);
            Assert.Equal(ResearchSessionStatus.Degraded, session.Status);
        }
    }

    [Fact]
    public async Task Runner_Cancellation()
    {
        var (db, session, turn) = SeedTurnForRunner();
        using (db)
        {
            var executor = new StubRoleExecutor();
            var bus = new ResearchEventBus();
            var runner = new ResearchRunner(db, executor, bus, new NullReportService(), new StubFollowUpRoutingService(), NullLogger<ResearchRunner>.Instance);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                runner.RunTurnAsync(turn.Id, cts.Token));

            await db.Entry(turn).ReloadAsync();
            Assert.Equal(ResearchTurnStatus.Cancelled, turn.Status);
        }
    }

    [Fact]
    public async Task Runner_AnalystTeam_Parallel()
    {
        var (db, session, turn) = SeedTurnForRunner();
        using (db)
        {
            var executor = new StubRoleExecutor();
            var bus = new ResearchEventBus();
            var runner = new ResearchRunner(db, executor, bus, new NullReportService(), new StubFollowUpRoutingService(), NullLogger<ResearchRunner>.Instance);

            await runner.RunTurnAsync(turn.Id);

            var analystRoles = new[]
            {
                StockAgentRoleIds.MarketAnalyst,
                StockAgentRoleIds.SocialSentimentAnalyst,
                StockAgentRoleIds.NewsAnalyst,
                StockAgentRoleIds.FundamentalsAnalyst,
                StockAgentRoleIds.ShareholderAnalyst,
                StockAgentRoleIds.ProductAnalyst
            };

            foreach (var role in analystRoles)
            {
                Assert.Contains(role, executor.ExecutedRoleIds);
            }

            var analystStage = await db.ResearchStageSnapshots
                .Include(s => s.RoleStates)
                .FirstOrDefaultAsync(s => s.TurnId == turn.Id && s.StageType == ResearchStageType.AnalystTeam);

            Assert.NotNull(analystStage);
            Assert.Equal(6, analystStage!.RoleStates.Count);
        }
    }

    [Fact]
    public async Task Runner_AnalystTeam_UsesFullProfile_ForLocalOllama()
    {
        var (db, session, turn) = SeedTurnForRunner();
        using (db)
        {
            var executor = new StubRoleExecutor();
            var bus = new ResearchEventBus();
            var settingsStore = CreateSettingsStore("ollama", "ollama", "qwen3:8b");
            var runner = new ResearchRunner(
                db,
                executor,
                bus,
                new NullReportService(),
                new StubFollowUpRoutingService(),
                NullLogger<ResearchRunner>.Instance,
                settingsStore);

            await runner.RunTurnAsync(turn.Id);

            var expectedRoles = new[]
            {
                StockAgentRoleIds.MarketAnalyst,
                StockAgentRoleIds.SocialSentimentAnalyst,
                StockAgentRoleIds.NewsAnalyst,
                StockAgentRoleIds.FundamentalsAnalyst,
                StockAgentRoleIds.ShareholderAnalyst,
                StockAgentRoleIds.ProductAnalyst
            };

            foreach (var role in expectedRoles)
            {
                Assert.Contains(role, executor.ExecutedRoleIds);
            }

            var analystStage = await db.ResearchStageSnapshots
                .Include(s => s.RoleStates)
                .FirstOrDefaultAsync(s => s.TurnId == turn.Id && s.StageType == ResearchStageType.AnalystTeam);

            Assert.NotNull(analystStage);
            Assert.Equal(expectedRoles.Length, analystStage!.RoleStates.Count);
            Assert.Equal(expectedRoles.OrderBy(role => role), analystStage.RoleStates.Select(role => role.RoleId).OrderBy(role => role));
        }
    }

    [Fact]
    public async Task Runner_AnalystTeam_UsesFullProfile_ForNonOllama()
    {
        var (db, session, turn) = SeedTurnForRunner();
        using (db)
        {
            var executor = new StubRoleExecutor();
            var bus = new ResearchEventBus();
            var settingsStore = CreateSettingsStore("openai", "openai", "gpt-4.1");
            var runner = new ResearchRunner(
                db,
                executor,
                bus,
                new NullReportService(),
                new StubFollowUpRoutingService(),
                NullLogger<ResearchRunner>.Instance,
                settingsStore);

            await runner.RunTurnAsync(turn.Id);

            var expectedRoles = new[]
            {
                StockAgentRoleIds.MarketAnalyst,
                StockAgentRoleIds.SocialSentimentAnalyst,
                StockAgentRoleIds.NewsAnalyst,
                StockAgentRoleIds.FundamentalsAnalyst,
                StockAgentRoleIds.ShareholderAnalyst,
                StockAgentRoleIds.ProductAnalyst
            };

            foreach (var role in expectedRoles)
            {
                Assert.Contains(role, executor.ExecutedRoleIds);
            }

            var analystStage = await db.ResearchStageSnapshots
                .Include(s => s.RoleStates)
                .FirstOrDefaultAsync(s => s.TurnId == turn.Id && s.StageType == ResearchStageType.AnalystTeam);

            Assert.NotNull(analystStage);
            Assert.Equal(expectedRoles.Length, analystStage!.RoleStates.Count);
            Assert.Equal(expectedRoles.OrderBy(role => role), analystStage.RoleStates.Select(role => role.RoleId).OrderBy(role => role));
        }
    }

    [Fact]
    public async Task Runner_LocalOllamaFullAnalystTeam_CompletesPipeline()
    {
        var (db, session, turn) = SeedTurnForRunner();
        using (db)
        {
            var executor = new StubRoleExecutor();
            var bus = new ResearchEventBus();
            var settingsStore = CreateSettingsStore("ollama", "ollama", "qwen3:8b");
            var runner = new ResearchRunner(
                db,
                executor,
                bus,
                new NullReportService(),
                new StubFollowUpRoutingService(),
                NullLogger<ResearchRunner>.Instance,
                settingsStore);

            await runner.RunTurnAsync(turn.Id);

            await db.Entry(turn).ReloadAsync();
            Assert.Equal(ResearchTurnStatus.Completed, turn.Status);
            Assert.Contains(StockAgentRoleIds.PortfolioManager, executor.ExecutedRoleIds);

            var stages = await db.ResearchStageSnapshots
                .Where(s => s.TurnId == turn.Id)
                .OrderBy(s => s.StageRunIndex)
                .ToListAsync();

            var analystStage = stages.FirstOrDefault(s => s.StageType == ResearchStageType.AnalystTeam);

            Assert.Equal(6, stages.Count);
            Assert.All(stages, s => Assert.Equal(ResearchStageStatus.Completed, s.Status));
            Assert.NotNull(analystStage);
            Assert.Equal(ResearchStageType.PortfolioDecision, stages[^1].StageType);
        }
    }

    [Fact]
    public async Task Runner_DebateStage_MultiRound()
    {
        var (db, session, turn) = SeedTurnForRunner();
        using (db)
        {
            var executor = new StubRoleExecutor();
            var bus = new ResearchEventBus();
            var runner = new ResearchRunner(db, executor, bus, new NullReportService(), new StubFollowUpRoutingService(), NullLogger<ResearchRunner>.Instance);

            await runner.RunTurnAsync(turn.Id);

            var debateStage = await db.ResearchStageSnapshots
                .Include(s => s.RoleStates)
                .FirstOrDefaultAsync(s => s.TurnId == turn.Id && s.StageType == ResearchStageType.ResearchDebate);

            Assert.NotNull(debateStage);
            var debateRoleStates = debateStage!.RoleStates.ToList();
            Assert.Equal(9, debateRoleStates.Count);

            var runIndices = debateRoleStates.Select(r => r.RunIndex).Distinct().OrderBy(i => i).ToList();
            Assert.Equal(new[] { 0, 1, 2 }, runIndices);
        }
    }

    [Fact]
    public async Task Runner_PortfolioDecision_CreatesDecisionSnapshot()
    {
        var (db, session, turn) = SeedTurnForRunner();
        using (db)
        {
            var executor = new StubRoleExecutor();
            var pmOutput = System.Text.Json.JsonSerializer.Serialize(new
            {
                content = System.Text.Json.JsonSerializer.Serialize(new
                {
                    rating = "Strong Buy",
                    action = "Buy",
                    executive_summary = "Solid growth potential",
                    confidence = 0.85m
                })
            });
            executor.ResultsByRoleId[StockAgentRoleIds.PortfolioManager] = new RoleExecutionResult(
                StockAgentRoleIds.PortfolioManager,
                ResearchRoleStatus.Completed,
                pmOutput,
                "trace-pm",
                Array.Empty<string>(),
                null, null);

            var bus = new ResearchEventBus();
            var runner = new ResearchRunner(db, executor, bus, new NullReportService(), new StubFollowUpRoutingService(), NullLogger<ResearchRunner>.Instance);

            await runner.RunTurnAsync(turn.Id);

            var decision = await db.ResearchDecisionSnapshots
                .FirstOrDefaultAsync(d => d.TurnId == turn.Id);

            Assert.NotNull(decision);
            Assert.Equal("Strong Buy", decision!.Rating);
            Assert.Equal("Buy", decision.Action);
            Assert.Equal("Solid growth potential", decision.ExecutiveSummary);
            Assert.Equal(0.85m, decision.Confidence);

            await db.Entry(session).ReloadAsync();
            Assert.Equal("Strong Buy", session.LatestRating);
        }
    }

    [Fact]
    public async Task Runner_ContinueSession_SkipsToPortfolioDecision()
    {
        var (db, session, turn) = SeedTurnForRunner();
        using (db)
        {
            var executor = new StubRoleExecutor();
            var bus = new ResearchEventBus();
            var runner = new ResearchRunner(db, executor, bus, new NullReportService(), new StubFollowUpRoutingService(), NullLogger<ResearchRunner>.Instance);

            // Complete the first turn (full pipeline)
            await runner.RunTurnAsync(turn.Id);
            await db.Entry(turn).ReloadAsync();
            Assert.Equal(ResearchTurnStatus.Completed, turn.Status);

            // Create a second turn with ContinueSession mode
            var turn2 = new ResearchTurn
            {
                SessionId = session.Id,
                TurnIndex = 1,
                UserPrompt = "explain the rationale",
                Status = ResearchTurnStatus.Queued,
                ContinuationMode = ResearchContinuationMode.ContinueSession,
                RequestedAt = DateTime.UtcNow
            };
            db.ResearchTurns.Add(turn2);
            await db.SaveChangesAsync();

            executor.ExecutedRoleIds.Clear();

            // Run the ContinueSession turn
            await runner.RunTurnAsync(turn2.Id);

            await db.Entry(turn2).ReloadAsync();
            Assert.Equal(ResearchTurnStatus.Completed, turn2.Status);

            // Only PortfolioManager should have been executed in the second turn
            Assert.Contains(StockAgentRoleIds.PortfolioManager, executor.ExecutedRoleIds);
            Assert.DoesNotContain(StockAgentRoleIds.CompanyOverviewAnalyst, executor.ExecutedRoleIds);
            Assert.DoesNotContain(StockAgentRoleIds.MarketAnalyst, executor.ExecutedRoleIds);
            Assert.DoesNotContain(StockAgentRoleIds.BullResearcher, executor.ExecutedRoleIds);
            Assert.DoesNotContain(StockAgentRoleIds.Trader, executor.ExecutedRoleIds);

            // Stages 0-4 should be Skipped, Stage 5 should be Completed
            var stages = await db.ResearchStageSnapshots
                .Where(s => s.TurnId == turn2.Id)
                .OrderBy(s => s.StageRunIndex)
                .ToListAsync();

            Assert.Equal(6, stages.Count);
            for (var i = 0; i < 5; i++)
                Assert.Equal(ResearchStageStatus.Skipped, stages[i].Status);
            Assert.Equal(ResearchStageStatus.Completed, stages[5].Status);
        }
    }

    [Fact]
    public async Task Runner_DebateConvergence_StopsWhenConverged()
    {
        var (db, session, turn) = SeedTurnForRunner();
        using (db)
        {
            var executor = new StubRoleExecutor();
            // Make ResearchManager output contain convergence signal
            executor.ResultsByRoleId[StockAgentRoleIds.ResearchManager] = new RoleExecutionResult(
                StockAgentRoleIds.ResearchManager,
                ResearchRoleStatus.Completed,
                "{\"decision\":\"看多\",\"converged\":true}",
                "trace-mgr",
                Array.Empty<string>(),
                null, null);

            var bus = new ResearchEventBus();
            var runner = new ResearchRunner(db, executor, bus, new NullReportService(), new StubFollowUpRoutingService(), NullLogger<ResearchRunner>.Instance);

            await runner.RunTurnAsync(turn.Id);

            // ResearchDebate should converge after round 1 (2 rounds × 3 roles = 6)
            var debateStage = await db.ResearchStageSnapshots
                .Include(s => s.RoleStates)
                .FirstOrDefaultAsync(s => s.TurnId == turn.Id && s.StageType == ResearchStageType.ResearchDebate);

            Assert.NotNull(debateStage);
            var debateRoleStates = debateStage!.RoleStates.ToList();
            Assert.Equal(6, debateRoleStates.Count); // 2 rounds, not 3
        }
    }

    [Fact]
    public async Task Runner_ResearchDebate_EarlyStopsAfterRound2_WhenManagerRepeatsLowConfidenceHold()
    {
        var (db, session, turn) = SeedTurnForRunner();
        using (db)
        {
            var executor = new StubRoleExecutor();
            executor.SequencedResultsByRoleId[StockAgentRoleIds.ResearchManager] = new Queue<RoleExecutionResult>(
            [
                new RoleExecutionResult(
                    StockAgentRoleIds.ResearchManager,
                    ResearchRoleStatus.Completed,
                    "{\"decision\":\"观望\",\"decisionConfidence\":\"低\",\"recommendation\":\"等待补充关键财务数据后再分析\"}",
                    "trace-mgr-1",
                    Array.Empty<string>(),
                    null,
                    null),
                new RoleExecutionResult(
                    StockAgentRoleIds.ResearchManager,
                    ResearchRoleStatus.Completed,
                    "{\"decision\":\"观望\",\"decisionConfidence\":\"低\",\"recommendation\":\"等待补充关键财务数据后再分析\"}",
                    "trace-mgr-2",
                    Array.Empty<string>(),
                    null,
                    null)
            ]);

            var bus = new ResearchEventBus();
            var runner = new ResearchRunner(db, executor, bus, new NullReportService(), new StubFollowUpRoutingService(), NullLogger<ResearchRunner>.Instance);

            await runner.RunTurnAsync(turn.Id);

            var debateStage = await db.ResearchStageSnapshots
                .Include(s => s.RoleStates)
                .FirstOrDefaultAsync(s => s.TurnId == turn.Id && s.StageType == ResearchStageType.ResearchDebate);

            Assert.NotNull(debateStage);
            var debateRoleStates = debateStage!.RoleStates.OrderBy(r => r.RunIndex).ToList();
            Assert.Equal(6, debateRoleStates.Count);
            Assert.Equal(new[] { 0, 1 }, debateRoleStates.Select(r => r.RunIndex).Distinct().OrderBy(i => i).ToArray());
        }
    }

    [Fact]
    public async Task Runner_RiskDebateConvergence_StopsWhenConverged()
    {
        var (db, session, turn) = SeedTurnForRunner();
        using (db)
        {
            var executor = new StubRoleExecutor();
            // Make NeutralRiskAnalyst output contain convergence signal
            executor.ResultsByRoleId[StockAgentRoleIds.NeutralRiskAnalyst] = new RoleExecutionResult(
                StockAgentRoleIds.NeutralRiskAnalyst,
                ResearchRoleStatus.Completed,
                "{\"riskStance\":\"中性\",\"recommendation\":\"hold\",\"converged\":true}",
                "trace-neutral",
                Array.Empty<string>(),
                null, null);

            var bus = new ResearchEventBus();
            var runner = new ResearchRunner(db, executor, bus, new NullReportService(), new StubFollowUpRoutingService(), NullLogger<ResearchRunner>.Instance);

            await runner.RunTurnAsync(turn.Id);

            // RiskDebate should converge after round 1 (2 rounds × 3 roles = 6)
            var riskStage = await db.ResearchStageSnapshots
                .Include(s => s.RoleStates)
                .FirstOrDefaultAsync(s => s.TurnId == turn.Id && s.StageType == ResearchStageType.RiskDebate);

            Assert.NotNull(riskStage);
            var riskRoleStates = riskStage!.RoleStates.ToList();
            Assert.Equal(6, riskRoleStates.Count); // 2 rounds × 3 analysts, not 3 rounds × 3 = 9
        }
    }

    [Fact]
    public async Task Runner_RiskDebate_EarlyStopsAfterRound2_WhenAllAnalystsRepeatDataInsufficiencyPattern()
    {
        var (db, session, turn) = SeedTurnForRunner();
        using (db)
        {
            var executor = new StubRoleExecutor();
            executor.SequencedResultsByRoleId[StockAgentRoleIds.AggressiveRiskAnalyst] = new Queue<RoleExecutionResult>(
            [
                new RoleExecutionResult(
                    StockAgentRoleIds.AggressiveRiskAnalyst,
                    ResearchRoleStatus.Completed,
                    "{\"riskStance\":\"激进\",\"riskAssessment\":\"由于缺乏量化数据，无法进行量化风险评估。\",\"recommendation\":\"等待补充财务数据后再评估。\"}",
                    "trace-agg-1",
                    Array.Empty<string>(),
                    null,
                    null),
                new RoleExecutionResult(
                    StockAgentRoleIds.AggressiveRiskAnalyst,
                    ResearchRoleStatus.Completed,
                    "{\"riskStance\":\"激进\",\"riskAssessment\":\"由于缺乏量化数据，无法进行量化风险评估。\",\"recommendation\":\"等待补充财务数据后再评估。\"}",
                    "trace-agg-2",
                    Array.Empty<string>(),
                    null,
                    null)
            ]);
            executor.SequencedResultsByRoleId[StockAgentRoleIds.NeutralRiskAnalyst] = new Queue<RoleExecutionResult>(
            [
                new RoleExecutionResult(
                    StockAgentRoleIds.NeutralRiskAnalyst,
                    ResearchRoleStatus.Completed,
                    "{\"riskStance\":\"中性\",\"riskAssessment\":\"由于缺乏量化数据，无法进行量化风险评估。\",\"recommendation\":\"建议等待补充市场和财务数据后再判断。\"}",
                    "trace-neutral-1",
                    Array.Empty<string>(),
                    null,
                    null),
                new RoleExecutionResult(
                    StockAgentRoleIds.NeutralRiskAnalyst,
                    ResearchRoleStatus.Completed,
                    "{\"riskStance\":\"中性\",\"riskAssessment\":\"由于缺乏量化数据，无法进行量化风险评估。\",\"recommendation\":\"建议等待补充市场和财务数据后再判断。\"}",
                    "trace-neutral-2",
                    Array.Empty<string>(),
                    null,
                    null)
            ]);
            executor.SequencedResultsByRoleId[StockAgentRoleIds.ConservativeRiskAnalyst] = new Queue<RoleExecutionResult>(
            [
                new RoleExecutionResult(
                    StockAgentRoleIds.ConservativeRiskAnalyst,
                    ResearchRoleStatus.Completed,
                    "{\"riskStance\":\"保守\",\"riskAssessment\":\"由于缺乏量化数据，无法进行量化风险评估。\",\"recommendation\":\"等待补充完整数据前不建议执行交易。\"}",
                    "trace-cons-1",
                    Array.Empty<string>(),
                    null,
                    null),
                new RoleExecutionResult(
                    StockAgentRoleIds.ConservativeRiskAnalyst,
                    ResearchRoleStatus.Completed,
                    "{\"riskStance\":\"保守\",\"riskAssessment\":\"由于缺乏量化数据，无法进行量化风险评估。\",\"recommendation\":\"等待补充完整数据前不建议执行交易。\"}",
                    "trace-cons-2",
                    Array.Empty<string>(),
                    null,
                    null)
            ]);

            var bus = new ResearchEventBus();
            var runner = new ResearchRunner(db, executor, bus, new NullReportService(), new StubFollowUpRoutingService(), NullLogger<ResearchRunner>.Instance);

            await runner.RunTurnAsync(turn.Id);

            var riskStage = await db.ResearchStageSnapshots
                .Include(s => s.RoleStates)
                .FirstOrDefaultAsync(s => s.TurnId == turn.Id && s.StageType == ResearchStageType.RiskDebate);

            Assert.NotNull(riskStage);
            var riskRoleStates = riskStage!.RoleStates.OrderBy(r => r.RunIndex).ToList();
            Assert.Equal(6, riskRoleStates.Count);
            Assert.Equal(new[] { 0, 1 }, riskRoleStates.Select(r => r.RunIndex).Distinct().OrderBy(i => i).ToArray());
        }
    }

    #endregion
}
