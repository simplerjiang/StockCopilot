using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StockCopilotSessionServiceTests
{
    [Fact]
    public async Task BuildDraftTurnAsync_ShouldCreateSessionAndDraftTimeline()
    {
        var chatHistory = new FakeStockChatHistoryService();
        var service = new StockCopilotSessionService(chatHistory, new FakeStockMarketContextService());

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
        Assert.Equal("needs_tool_execution", result.Turns[0].FinalAnswer.Status);
        Assert.Contains(result.Turns[0].FollowUpActions, item => item.ToolName == "StockKlineMcp");
    }

    [Fact]
    public async Task BuildDraftTurnAsync_ShouldBlockExternalSearchWithoutApproval()
    {
        var service = new StockCopilotSessionService(new FakeStockChatHistoryService(), new FakeStockMarketContextService());

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
    }

    [Fact]
    public async Task BuildDraftTurnAsync_ShouldApproveExternalSearchWhenExplicitlyAllowed()
    {
        var service = new StockCopilotSessionService(new FakeStockChatHistoryService(), new FakeStockMarketContextService());

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
        {
            return Task.FromResult<StockMarketContextDto?>(new StockMarketContextDto("主升", 81m, "银行", "银行", "BK001", 88m, 0.7m, "积极执行", false, true));
        }
    }
}