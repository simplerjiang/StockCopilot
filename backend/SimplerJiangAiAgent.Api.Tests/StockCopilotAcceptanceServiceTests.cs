using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StockCopilotAcceptanceServiceTests
{
    [Fact]
    public async Task BuildBaselineAsync_ShouldComputeAcceptanceMetricsAndReplayBaseline()
    {
        var replayService = new FakeReplayCalibrationService();
        var service = new StockCopilotAcceptanceService(replayService);

        var turn = new StockCopilotTurnDto(
            TurnId: "turn-1",
            SessionKey: "session-1",
            Symbol: "sh600000",
            UserQuestion: "先看 60 日结构，再看本地公告。",
            CreatedAt: new DateTime(2026, 3, 24, 9, 0, 0, DateTimeKind.Utc),
            Status: "completed",
            PlannerSummary: "planner summary",
            GovernorSummary: "governor summary",
            MarketContext: null,
            PlanSteps: Array.Empty<StockCopilotPlanStepDto>(),
            ToolCalls: new[]
            {
                new StockCopilotToolCallDto("call-kline", "tool-1", "StockKlineMcp", "local_required", "检查价格结构", "symbol=sh600000", "approved", null),
                new StockCopilotToolCallDto("call-news", "tool-2", "StockNewsMcp", "local_required", "读取本地新闻", "symbol=sh600000", "approved", null),
                new StockCopilotToolCallDto("call-search", "tool-3", "StockSearchMcp", "external_gated", "补充外部证据", "query=test", "blocked", "need approval")
            },
            ToolResults: new[]
            {
                new StockCopilotToolResultDto("call-kline", "StockKlineMcp", "completed", "trace-kline", 2, 1, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<StockCopilotMcpEvidenceDto>(), "kline ok"),
                new StockCopilotToolResultDto("call-news", "StockNewsMcp", "completed", "trace-news", 1, 1, new[] { "stale_cache" }, new[] { "market_context_stale" }, Array.Empty<StockCopilotMcpEvidenceDto>(), "news ok")
            },
            FinalAnswer: new StockCopilotFinalAnswerDto("completed", "grounded", "tool_results", 0.78m, false, new[] { "only facts from tool results" }, Array.Empty<StockCopilotMcpEvidenceDto>()),
            FollowUpActions: new[]
            {
                new StockCopilotFollowUpActionDto("action-news", "查看本地新闻证据", "inspect_news", "StockNewsMcp", "news", true, null),
                new StockCopilotFollowUpActionDto("action-plan", "起草交易计划", "draft_trading_plan", null, "plan", false, "need more context")
            });

        var result = await service.BuildBaselineAsync(new StockCopilotAcceptanceBaselineRequestDto(
            Symbol: "SH600000",
            Turn: turn,
            ToolExecutions: new[]
            {
                new StockCopilotToolExecutionMetricDto("call-kline", "StockKlineMcp", "local_required", 620, 2, 1, Array.Empty<string>(), Array.Empty<string>()),
                new StockCopilotToolExecutionMetricDto("call-news", "StockNewsMcp", "local_required", 930, 1, 1, new[] { "stale_cache" }, new[] { "market_context_stale" })
            },
            ReplaySampleTake: 12));

        Assert.Equal("sh600000", result.Symbol);
        Assert.Equal(2, result.ApprovedToolCallCount);
        Assert.Equal(2, result.ExecutedToolCallCount);
        Assert.True(result.OverallScore > 0m);
        Assert.Equal(1, result.WarningCount);
        Assert.Equal(1, result.DegradedFlagCount);
        Assert.Contains(result.Metrics, item => item.Key == "tool_efficiency" && item.Value == 100m);
        Assert.Contains(result.Metrics, item => item.Key == "local_first_hit" && item.Value == 100m);
        Assert.Contains(result.Metrics, item => item.Key == "external_search_trigger" && item.Value == 0m);
        Assert.Equal(12, replayService.LastTake);
        Assert.Equal(4, result.ReplayBaseline.SampleCount);
    }

    [Fact]
    public async Task BuildBaselineAsync_ShouldHandleNoExecutionsGracefully()
    {
        var service = new StockCopilotAcceptanceService(new FakeReplayCalibrationService());
        var turn = new StockCopilotTurnDto(
            TurnId: "turn-empty",
            SessionKey: "session-empty",
            Symbol: "sz000001",
            UserQuestion: "测试",
            CreatedAt: DateTime.UtcNow,
            Status: "draft",
            PlannerSummary: "planner",
            GovernorSummary: "governor",
            MarketContext: null,
            PlanSteps: Array.Empty<StockCopilotPlanStepDto>(),
            ToolCalls: new[]
            {
                new StockCopilotToolCallDto("call-news", "tool-1", "StockNewsMcp", "local_required", "读取本地新闻", "symbol=sz000001", "approved", null)
            },
            ToolResults: Array.Empty<StockCopilotToolResultDto>(),
            FinalAnswer: new StockCopilotFinalAnswerDto("needs_tool_execution", "pending", "tool_results_required", null, true, Array.Empty<string>(), Array.Empty<StockCopilotMcpEvidenceDto>()),
            FollowUpActions: Array.Empty<StockCopilotFollowUpActionDto>());

        var result = await service.BuildBaselineAsync(new StockCopilotAcceptanceBaselineRequestDto(
            Symbol: "sz000001",
            Turn: turn,
            ToolExecutions: Array.Empty<StockCopilotToolExecutionMetricDto>(),
            ReplaySampleTake: 0));

        Assert.Equal(0, result.ExecutedToolCallCount);
        Assert.Contains(result.Metrics, item => item.Key == "tool_efficiency" && item.Value == 0m);
        Assert.Contains(result.Highlights, item => item.Contains("0/1"));
    }

    private sealed class FakeReplayCalibrationService : IStockAgentReplayCalibrationService
    {
        public int LastTake { get; private set; }

        public Task<StockAgentReplayBaselineDto> BuildBaselineAsync(string? symbol, int take = 80, CancellationToken cancellationToken = default)
        {
            LastTake = take;
            return Task.FromResult(new StockAgentReplayBaselineDto(
                Scope: symbol ?? "all",
                GeneratedAt: new DateTime(2026, 3, 24, 8, 0, 0, DateTimeKind.Utc),
                SampleCount: 4,
                TraceableEvidenceRate: 75m,
                ParseRepairRate: 25m,
                PollutedEvidenceRate: 5m,
                RevisionCompletenessRate: 50m,
                Horizons: new[]
                {
                    new StockAgentReplayHorizonMetricDto(5, 4, 75m, 3.2m, 0.18m, 80m, 20m, 50m)
                },
                Samples: Array.Empty<StockAgentReplaySampleDto>()));
        }
    }
}