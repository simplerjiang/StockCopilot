using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend.WebSearch;
using Xunit;

namespace SimplerJiangAiAgent.Api.Tests;

#region Stubs

internal sealed class StubMcpToolGateway : IMcpToolGateway
{
    public int CallCount;
    public readonly List<string> CalledTools = new();
    public bool ShouldThrow;

    private static readonly StockCopilotMcpCacheDto EmptyCache = new(false, "none", DateTime.MinValue);
    private static readonly StockCopilotMcpMetaDto EmptyMeta = new("1.0", "test", "stub", null, null, null, null);

    private Task<StockCopilotMcpEnvelopeDto<T>> Wrap<T>(string tool, T data)
    {
        CallCount++;
        CalledTools.Add(tool);
        if (ShouldThrow) throw new InvalidOperationException($"Tool {tool} failed");
        return Task.FromResult(new StockCopilotMcpEnvelopeDto<T>(
            "trace-stub", "task-stub", tool, 0, EmptyCache,
            Array.Empty<string>(), Array.Empty<string>(), data,
            Array.Empty<StockCopilotMcpEvidenceDto>(),
            Array.Empty<StockCopilotMcpFeatureDto>(), EmptyMeta));
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotCompanyOverviewDataDto>> GetCompanyOverviewAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window, CancellationToken ct)
        => Wrap(StockMcpToolNames.CompanyOverview, new StockCopilotCompanyOverviewDataDto(symbol, "Test", null, 0m, 0m, null, null, null, null, DateTime.UtcNow, null, 0, null, null));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotProductDataDto>> GetProductAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window, CancellationToken ct)
        => Wrap(StockMcpToolNames.Product, new StockCopilotProductDataDto(symbol, null, null, null, null, null, null, 0, "stub", Array.Empty<StockCopilotProductFactDto>()));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotFundamentalsDataDto>> GetFundamentalsAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window, CancellationToken ct)
        => Wrap(StockMcpToolNames.Fundamentals, new StockCopilotFundamentalsDataDto(symbol, null, 0, Array.Empty<StockFundamentalFactDto>()));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotShareholderDataDto>> GetShareholderAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window, CancellationToken ct)
        => Wrap(StockMcpToolNames.Shareholder, new StockCopilotShareholderDataDto(symbol, null, null, 0, Array.Empty<StockFundamentalFactDto>()));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotMarketContextDataDto>> GetMarketContextAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window, CancellationToken ct)
        => Wrap(StockMcpToolNames.MarketContext, new StockCopilotMarketContextDataDto(symbol, false, null, null, null, null, null));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotSocialSentimentDataDto>> GetSocialSentimentAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window, CancellationToken ct)
        => Wrap(StockMcpToolNames.SocialSentiment, new StockCopilotSocialSentimentDataDto(symbol, "ok", false, null, "stub", 0, null,
            new StockCopilotSentimentCountDto(0, 0, 0, 0, null),
            new StockCopilotSentimentCountDto(0, 0, 0, 0, null),
            new StockCopilotSentimentCountDto(0, 0, 0, 0, null), null));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotKlineDataDto>> GetKlineAsync(string symbol, string interval, int count, string? source, string? taskId, StockCopilotMcpWindowOptions? window, CancellationToken ct)
        => Wrap(StockMcpToolNames.Kline, new StockCopilotKlineDataDto(symbol, interval, count, Array.Empty<KLinePointDto>(), new StockCopilotKeyLevelsDto(null, null, null, null, null, null), "neutral", 0m, 0m, 0m, 0m));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotMinuteDataDto>> GetMinuteAsync(string symbol, string? source, string? taskId, StockCopilotMcpWindowOptions? window, CancellationToken ct)
        => Wrap(StockMcpToolNames.Minute, new StockCopilotMinuteDataDto(symbol, "closed", 0, Array.Empty<MinuteLinePointDto>(), null, null, null, null));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotStrategyDataDto>> GetStrategyAsync(string symbol, string interval, int count, string? source, IReadOnlyList<string>? strategies, string? taskId, StockCopilotMcpWindowOptions? window, CancellationToken ct)
        => Wrap(StockMcpToolNames.Strategy, new StockCopilotStrategyDataDto(symbol, interval, Array.Empty<string>(), Array.Empty<StockCopilotStrategySignalDto>()));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotNewsDataDto>> GetNewsAsync(string symbol, string level, string? taskId, StockCopilotMcpWindowOptions? window, CancellationToken ct)
        => Wrap(StockMcpToolNames.News, new StockCopilotNewsDataDto(symbol, level, 0, null));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotSearchDataDto>> SearchAsync(string query, bool trustedOnly, string? taskId, CancellationToken ct)
        => Wrap(StockMcpToolNames.Search, new StockCopilotSearchDataDto(query, "stub", trustedOnly, 0, Array.Empty<StockCopilotSearchResultDto>()));

    public Task<WebSearchResult> WebSearchAsync(string query, WebSearchOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(new WebSearchResult(Array.Empty<WebSearchItem>(), "stub", false));

    public Task<WebSearchResult> WebSearchNewsAsync(string query, WebSearchOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(new WebSearchResult(Array.Empty<WebSearchItem>(), "stub", false));

    public Task<WebReadResult> WebReadUrlAsync(string url, int maxChars = 8000, CancellationToken ct = default)
        => Task.FromResult(new WebReadResult("", url, 0, false));

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotFinancialReportDataDto>> GetFinancialReportAsync(string symbol, int periods, string? taskId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotFinancialTrendDataDto>> GetFinancialTrendAsync(string symbol, int periods, string? taskId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
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

    public Task<LlmChatResult> ChatAsync(string provider, LlmChatRequest request, CancellationToken ct)
    {
        if (ShouldThrow) throw new InvalidOperationException("LLM call failed");
        return Task.FromResult(new LlmChatResult(ResponseContent, $"trace-{Guid.NewGuid():N}"));
    }
}

internal sealed class StubRoleExecutor : IResearchRoleExecutor
{
    public readonly Dictionary<string, RoleExecutionResult> ResultsByRoleId = new();
    public readonly List<string> ExecutedRoleIds = new();
    public RoleExecutionResult DefaultResult = new("default", ResearchRoleStatus.Completed, "{\"content\":\"ok\"}", "trace-1", Array.Empty<string>(), null, null);

    public Task<RoleExecutionResult> ExecuteRoleAsync(RoleExecutionContext context, CancellationToken ct)
    {
        ExecutedRoleIds.Add(context.RoleId);
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

    private static ResearchSessionService CreateSessionService(AppDbContext db, StubFollowUpRoutingService? routingService = null) =>
        new(db, new ResearchEventBus(), routingService ?? new StubFollowUpRoutingService(), NullLogger<ResearchSessionService>.Instance);

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
    public async Task SubmitTurnAsync_ContinueSession_NotFound_Throws()
    {
        using var db = CreateDb();
        var svc = CreateSessionService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SubmitTurnAsync(new ResearchTurnSubmitRequestDto("SH600000", "continue", "nonexistent-key", "ContinueSession")));
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

    #endregion
}
