using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StockCopilotSessionServiceTests
{
    [Fact]
    public async Task BuildDraftTurnAsync_ShouldExecuteApprovedToolsAndProduceGroundedAnswer()
    {
        var chatHistory = new FakeStockChatHistoryService();
        var service = new StockCopilotSessionService(chatHistory, new FakeStockCopilotMcpService(), new FakeStockMarketContextService(), new StockAgentRoleContractRegistry());

        var result = await service.BuildDraftTurnAsync(new StockCopilotTurnDraftRequestDto(
            Symbol: "SH600000",
            Question: "帮我看看浦发银行60日K线结构、RSI和最新公告",
            SessionKey: null,
            SessionTitle: null,
            TaskId: "task-r1",
            AllowExternalSearch: false));

        Assert.Equal("sh600000", result.Symbol);
        Assert.Single(result.Turns);
        Assert.Equal("sh600000", chatHistory.LastCreatedSymbol);
        Assert.Contains(result.Turns[0].PlanSteps, item => item.ToolName == "StockKlineMcp");
        Assert.Contains(result.Turns[0].PlanSteps, item => item.ToolName == "StockStrategyMcp");
        Assert.Contains(result.Turns[0].PlanSteps, item => item.ToolName == "StockNewsMcp");
        Assert.Equal("done", result.Turns[0].FinalAnswer.Status);
        Assert.False(result.Turns[0].FinalAnswer.NeedsToolExecution);
        Assert.Equal("done", result.Turns[0].Status);
        Assert.Equal(3, result.Turns[0].ToolResults.Count);
        Assert.Equal(4000, result.Turns[0].LoopBudget?.MaxPollingSteps);
        Assert.Equal("done", result.Turns[0].LoopExecution?.Status);
        Assert.Contains(result.Turns[0].FollowUpActions, item => item.ToolName == "StockKlineMcp");
        Assert.NotNull(result.RoleContractChecklist);
        Assert.Equal(15, result.RoleContractChecklist!.Roles.Count);
    }

    [Fact]
    public async Task BuildDraftTurnAsync_ShouldBlockExternalSearchWithoutApproval()
    {
        var service = new StockCopilotSessionService(new FakeStockChatHistoryService(), new FakeStockCopilotMcpService(), new FakeStockMarketContextService(), new StockAgentRoleContractRegistry());

        var result = await service.BuildDraftTurnAsync(new StockCopilotTurnDraftRequestDto(
            Symbol: "sz000001",
            Question: "请联网搜索平安银行最新外部研报并给我结论",
            SessionKey: null,
            SessionTitle: "外部搜索测试",
            TaskId: null,
            AllowExternalSearch: false));

        var searchCall = Assert.Single(result.Turns[0].ToolCalls, item => item.ToolName == "StockSearchMcp");
        Assert.Equal("blocked", searchCall.ApprovalStatus);
        Assert.NotNull(searchCall.BlockedReason);
        Assert.Contains("Local-First", result.Turns[0].FinalAnswer.Constraints.Last());
        Assert.DoesNotContain(result.Turns[0].ToolResults, item => item.ToolName == "StockSearchMcp");
        Assert.NotEqual("needs_tool_execution", result.Turns[0].FinalAnswer.Status);
    }

    [Fact]
    public async Task BuildDraftTurnAsync_ShouldApproveExternalSearchWhenExplicitlyAllowed()
    {
        var service = new StockCopilotSessionService(new FakeStockChatHistoryService(), new FakeStockCopilotMcpService(), new FakeStockMarketContextService(), new StockAgentRoleContractRegistry());

        var result = await service.BuildDraftTurnAsync(new StockCopilotTurnDraftRequestDto(
            Symbol: "sz000001",
            Question: "联网搜索最新外部研报并补充新闻",
            SessionKey: null,
            SessionTitle: null,
            TaskId: null,
            AllowExternalSearch: true));

        var searchCall = Assert.Single(result.Turns[0].ToolCalls, item => item.ToolName == "StockSearchMcp");
        Assert.Equal("approved", searchCall.ApprovalStatus);
        Assert.DoesNotContain(result.Turns[0].FinalAnswer.Constraints, item => item.Contains("Local-First"));
        Assert.Contains(result.Turns[0].ToolResults, item => item.ToolName == "StockSearchMcp");
        Assert.Equal(1, result.Turns[0].LoopExecution?.ExternalSearchCallsExecuted);
    }

    [Fact]
    public async Task BuildDraftTurnAsync_ShouldCloseWithGapsWhenEvidenceIsWeak()
    {
        var service = new StockCopilotSessionService(new FakeStockChatHistoryService(), new WeakEvidenceStockCopilotMcpService(), new FakeStockMarketContextService(), new StockAgentRoleContractRegistry());

        var result = await service.BuildDraftTurnAsync(new StockCopilotTurnDraftRequestDto(
            Symbol: "sz000001",
            Question: "只看公告和新闻，给我结论",
            SessionKey: null,
            SessionTitle: null,
            TaskId: null,
            AllowExternalSearch: false));

        Assert.Equal("done_with_gaps", result.Turns[0].FinalAnswer.Status);
        Assert.Contains(result.Turns[0].FinalAnswer.Constraints, item => item.Contains("证据缺口") || item.Contains("warning"));
    }

    [Fact]
    public async Task BuildDraftTurnAsync_ShouldExposeRoleContractChecklistInRuntimeOutput()
    {
        var service = new StockCopilotSessionService(new FakeStockChatHistoryService(), new FakeStockCopilotMcpService(), new FakeStockMarketContextService(), new StockAgentRoleContractRegistry());

        var result = await service.BuildDraftTurnAsync(new StockCopilotTurnDraftRequestDto(
            Symbol: "sh600000",
            Question: "看下市场结构和最新新闻",
            SessionKey: null,
            SessionTitle: null,
            TaskId: "task-role-contracts",
            AllowExternalSearch: false));

        Assert.NotNull(result.RoleContractChecklist);
        Assert.Equal("GOAL-AGENT-NEW-001-P0-Pre-Phase-F", result.RoleContractChecklist!.SourceTaskId);
        Assert.Equal("phase-f-20260326-r1", result.RoleContractChecklist.Version);
        Assert.Contains(result.RoleContractChecklist.Roles, item => item.RoleId == StockAgentRoleIds.ProductAnalyst && item.ToolAccessMode == "local_required" && item.PreferredMcpSequence.Contains(StockMcpToolNames.Product));
    }

    private sealed class FakeStockChatHistoryService : IStockChatHistoryService
    {
        public string? LastCreatedSymbol { get; private set; }

        public Task<StockChatSession> CreateSessionAsync(string symbol, string? title, string? sessionKey, CancellationToken cancellationToken = default)
        {
            LastCreatedSymbol = symbol;
            return Task.FromResult(new StockChatSession
            {
                Id = 1,
                Symbol = symbol,
                SessionKey = string.IsNullOrWhiteSpace(sessionKey) ? "session-1" : sessionKey,
                Title = string.IsNullOrWhiteSpace(title) ? "draft" : title,
                CreatedAt = new DateTime(2026, 3, 22, 9, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 3, 22, 9, 0, 0, DateTimeKind.Utc)
            });
        }

        public Task<IReadOnlyList<StockChatSession>> GetSessionsAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<StockChatSession>>(Array.Empty<StockChatSession>());
        }

        public Task<StockChatSession?> GetSessionAsync(string sessionKey, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<StockChatSession?>(null);
        }

        public Task<IReadOnlyList<StockChatMessage>> GetMessagesAsync(string sessionKey, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<StockChatMessage>>(Array.Empty<StockChatMessage>());
        }

        public Task SaveMessagesAsync(string sessionKey, IReadOnlyList<StockChatMessageDto> messages, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStockMarketContextService : IStockMarketContextService
    {
        public Task<StockMarketContextDto?> GetLatestAsync(string symbol, CancellationToken cancellationToken = default)
            => GetLatestAsync(symbol, null, cancellationToken);

        public Task<StockMarketContextDto?> GetLatestAsync(string symbol, string? sectorNameHint, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<StockMarketContextDto?>(new StockMarketContextDto("主升", 81m, "银行", "银行", "BK001", 88m, 0.7m, "积极执行", false, true));
        }
    }

    private class FakeStockCopilotMcpService : IStockCopilotMcpService
    {
        public virtual Task<StockCopilotMcpEnvelopeDto<StockCopilotCompanyOverviewDataDto>> GetCompanyOverviewAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public virtual Task<StockCopilotMcpEnvelopeDto<StockCopilotProductDataDto>> GetProductAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public virtual Task<StockCopilotMcpEnvelopeDto<StockCopilotFundamentalsDataDto>> GetFundamentalsAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public virtual Task<StockCopilotMcpEnvelopeDto<StockCopilotShareholderDataDto>> GetShareholderAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public virtual Task<StockCopilotMcpEnvelopeDto<StockCopilotMarketContextDataDto>> GetMarketContextAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public virtual Task<StockCopilotMcpEnvelopeDto<StockCopilotSocialSentimentDataDto>> GetSocialSentimentAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public virtual Task<StockCopilotMcpEnvelopeDto<StockCopilotKlineDataDto>> GetKlineAsync(string symbol, string interval, int count, string? source, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StockCopilotMcpEnvelopeDto<StockCopilotKlineDataDto>(
                "trace-kline",
                taskId ?? "task-kline",
                "StockKlineMcp",
                320,
                new StockCopilotMcpCacheDto(false, "test", DateTime.UtcNow),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new StockCopilotKlineDataDto(symbol, interval, count, Array.Empty<KLinePointDto>(), new StockCopilotKeyLevelsDto(9.8m, 10.5m, 10m, 10.2m, 10.5m, 9.8m), "主升", 3.4m, 7.8m, 2.1m, 1.2m),
                new[]
                {
                    new StockCopilotMcpEvidenceDto("kline", "K line", "local", DateTime.UtcNow, DateTime.UtcNow, null, "kline excerpt", "kline summary", "local_fact", "summary_only", DateTime.UtcNow, 1, "src-1", "stock", null, symbol, Array.Empty<string>())
                },
                new[]
                {
                    new StockCopilotMcpFeatureDto("trendState", "Trend State", "text", null, "主升", null, null)
                },
                new StockCopilotMcpMetaDto("v1", "local_required", "StockKlineMcp", symbol, interval, null, null)));
        }

            public virtual Task<StockCopilotMcpEnvelopeDto<StockCopilotMinuteDataDto>> GetMinuteAsync(string symbol, string? source, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StockCopilotMcpEnvelopeDto<StockCopilotMinuteDataDto>(
                "trace-minute",
                taskId ?? "task-minute",
                "StockMinuteMcp",
                180,
                new StockCopilotMcpCacheDto(false, "test", DateTime.UtcNow),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new StockCopilotMinuteDataDto(symbol, "午后", 120, Array.Empty<MinuteLinePointDto>(), 10.1m, 0.5m, 0.3m, 1.1m),
                Array.Empty<StockCopilotMcpEvidenceDto>(),
                Array.Empty<StockCopilotMcpFeatureDto>(),
                new StockCopilotMcpMetaDto("v1", "local_required", "StockMinuteMcp", symbol, "minute", null, null)));
        }

            public virtual Task<StockCopilotMcpEnvelopeDto<StockCopilotStrategyDataDto>> GetStrategyAsync(string symbol, string interval, int count, string? source, IReadOnlyList<string>? strategies, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StockCopilotMcpEnvelopeDto<StockCopilotStrategyDataDto>(
                "trace-strategy",
                taskId ?? "task-strategy",
                "StockStrategyMcp",
                260,
                new StockCopilotMcpCacheDto(false, "test", DateTime.UtcNow),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new StockCopilotStrategyDataDto(symbol, interval, strategies ?? Array.Empty<string>(),
                [
                    new StockCopilotStrategySignalDto("macd", interval, "golden", 1m, "bullish", "golden cross"),
                    new StockCopilotStrategySignalDto("rsi", interval, "flat", 55m, "positive", "neutral positive")
                ]),
                Array.Empty<StockCopilotMcpEvidenceDto>(),
                new[]
                {
                    new StockCopilotMcpFeatureDto("signalCount", "Signal Count", "number", 2m, null, null, null)
                },
                new StockCopilotMcpMetaDto("v1", "local_required", "StockStrategyMcp", symbol, interval, null, null)));
        }

            public virtual Task<StockCopilotMcpEnvelopeDto<StockCopilotNewsDataDto>> GetNewsAsync(string symbol, string level, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StockCopilotMcpEnvelopeDto<StockCopilotNewsDataDto>(
                "trace-news",
                taskId ?? "task-news",
                "StockNewsMcp",
                240,
                new StockCopilotMcpCacheDto(false, "test", DateTime.UtcNow),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new StockCopilotNewsDataDto(symbol, level, 2, DateTime.UtcNow),
                new[]
                {
                    new StockCopilotMcpEvidenceDto("news", "公告", "eastmoney", DateTime.UtcNow, DateTime.UtcNow, "https://example.com", "news excerpt", "news summary", "local_fact", "summary_only", DateTime.UtcNow, 2, "src-2", level, "中性", symbol, Array.Empty<string>())
                },
                new[]
                {
                    new StockCopilotMcpFeatureDto("itemCount", "News Count", "number", 2m, null, null, null)
                },
                new StockCopilotMcpMetaDto("v1", "local_required", "StockNewsMcp", symbol, null, level, null)));
        }

        public virtual Task<StockCopilotMcpEnvelopeDto<StockCopilotSearchDataDto>> SearchAsync(string query, bool trustedOnly, string? taskId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StockCopilotMcpEnvelopeDto<StockCopilotSearchDataDto>(
                "trace-search",
                taskId ?? "task-search",
                "StockSearchMcp",
                480,
                new StockCopilotMcpCacheDto(false, "test", DateTime.UtcNow),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new StockCopilotSearchDataDto(query, "test-provider", trustedOnly, 2,
                [
                    new StockCopilotSearchResultDto("外部研报", "https://example.com/report", "external", 0.8m, DateTime.UtcNow, "report excerpt")
                ]),
                new[]
                {
                    new StockCopilotMcpEvidenceDto("search", "外部研报", "external", DateTime.UtcNow, DateTime.UtcNow, "https://example.com/report", "search excerpt", "search summary", "url_fetched", "summary_only", DateTime.UtcNow, null, null, "external", null, null, Array.Empty<string>())
                },
                new[]
                {
                    new StockCopilotMcpFeatureDto("provider", "Provider", "text", null, "test-provider", null, null)
                },
                new StockCopilotMcpMetaDto("v1", "external_gated", "StockSearchMcp", null, null, query, null)));
        }
    }

    private sealed class WeakEvidenceStockCopilotMcpService : FakeStockCopilotMcpService
    {
        public override Task<StockCopilotMcpEnvelopeDto<StockCopilotNewsDataDto>> GetNewsAsync(string symbol, string level, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StockCopilotMcpEnvelopeDto<StockCopilotNewsDataDto>(
                "trace-news-weak",
                taskId ?? "task-news-weak",
                "StockNewsMcp",
                120,
                new StockCopilotMcpCacheDto(false, "test", DateTime.UtcNow),
                new[] { "weak_evidence" },
                new[] { "no_local_news_evidence" },
                new StockCopilotNewsDataDto(symbol, level, 0, null),
                Array.Empty<StockCopilotMcpEvidenceDto>(),
                Array.Empty<StockCopilotMcpFeatureDto>(),
                new StockCopilotMcpMetaDto("v1", "local_required", "StockNewsMcp", symbol, null, level, null)));
        }
    }
}