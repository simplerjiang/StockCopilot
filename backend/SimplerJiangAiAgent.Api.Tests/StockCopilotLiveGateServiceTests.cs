using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StockCopilotLiveGateServiceTests
{
    [Fact]
    public async Task RunAsync_ShouldIncludeContractRulesInPromptAndExposeLlmTrace()
    {
        var llm = new FakeLlmService(
            """
            {
              "plannerSummary": "先走本地新闻链路。",
              "governorSummary": "不需要外部搜索。",
              "finalAnswerDraft": "这是 live gate 计划草案，后续以 tool result 为准。",
              "toolCalls": [
                {
                  "roleId": "news_analyst",
                  "toolName": "StockNewsMcp",
                  "purpose": "先收集本地新闻证据",
                  "inputSummary": "level=stock"
                }
              ]
            }
            """,
            "llm-trace-001");
        var service = CreateService(llm, new FakeMcpToolGateway());

        var result = await service.RunAsync(new StockCopilotLiveGateRequestDto(
            Symbol: "sh600000",
            Question: "看下浦发银行的本地新闻证据",
            SessionKey: null,
            SessionTitle: null,
            TaskId: "phase-f-r1",
            AllowExternalSearch: false,
            Provider: "active",
            Model: "gpt-test",
            Temperature: 0.1));

        Assert.Equal("llm-trace-001", result.LlmTraceId);
        Assert.Equal("llm-trace-001", result.Session.Turns[0].LlmTraceId);
        Assert.Contains("toolAccessMode=local_required", result.Prompt, StringComparison.Ordinal);
        Assert.Contains("toolName=StockSearchMcp; policyClass=external_gated", result.Prompt, StringComparison.Ordinal);
        Assert.Contains("roleId=portfolio_manager", result.Prompt, StringComparison.Ordinal);
        Assert.Contains("allowsDirectQueryTools=False", result.Prompt, StringComparison.Ordinal);
        Assert.Contains("allowExternalSearch=False", result.Prompt, StringComparison.Ordinal);
        Assert.Contains("evidenceSkip/evidenceTake", result.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("counterTrendWarning", result.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ShouldRejectUnauthorizedAndLocalFirstViolations()
    {
        var llm = new FakeLlmService(
            """
            {
              "plannerSummary": "先试图拉外部证据，再补本地新闻。",
              "governorSummary": "需要 governor 审核。",
              "finalAnswerDraft": "这是 live gate 计划草案，后续以 tool result 为准。",
              "toolCalls": [
                {
                  "roleId": "portfolio_manager",
                  "toolName": "StockNewsMcp",
                  "purpose": "组合经理想直接取数",
                  "inputSummary": "level=stock"
                },
                {
                  "roleId": "news_analyst",
                  "toolName": "StockSearchMcp",
                  "purpose": "直接联网搜索",
                  "inputSummary": "query=浦发银行 最新 研报"
                },
                {
                  "roleId": "news_analyst",
                  "toolName": "StockNewsMcp",
                  "purpose": "回到本地新闻",
                  "inputSummary": "level=stock"
                }
              ]
            }
            """,
            "llm-trace-002");
        var service = CreateService(llm, new FakeMcpToolGateway());

        var result = await service.RunAsync(new StockCopilotLiveGateRequestDto(
            Symbol: "sh600000",
            Question: "先搜索最新研报，再补本地新闻",
            SessionKey: null,
            SessionTitle: null,
            TaskId: "phase-f-r1",
            AllowExternalSearch: true,
            Provider: "active",
            Model: "gpt-test",
            Temperature: 0.1));

        Assert.Equal(2, result.RejectedToolCalls.Count);
        Assert.Contains(result.RejectedToolCalls, item => item.RoleId == StockAgentRoleIds.PortfolioManager && item.ErrorCode == McpErrorCodes.RoleNotAuthorized);
        Assert.Contains(result.RejectedToolCalls, item => item.ToolName == StockMcpToolNames.Search && item.ErrorCode == "tool.local_first_required");
        Assert.Single(result.Session.Turns[0].ToolResults);
        Assert.Equal(StockMcpToolNames.News, result.Session.Turns[0].ToolResults[0].ToolName);
        Assert.Equal("trace-news-live", result.Session.Turns[0].ToolResults[0].TraceId);
    }

    [Fact]
    public async Task RunAsync_ShouldExposeToolTraceIdsAndAcceptanceMetrics()
    {
        var llm = new FakeLlmService(
            """
            {
              "plannerSummary": "先看 K 线，再看本地新闻。",
              "governorSummary": "全部走 local-first。",
              "finalAnswerDraft": "这是 live gate 计划草案，后续以 tool result 为准。",
              "toolCalls": [
                {
                  "roleId": "market_analyst",
                  "toolName": "StockKlineMcp",
                  "purpose": "确认趋势结构",
                  "inputSummary": "interval=day; count=60"
                },
                {
                  "roleId": "news_analyst",
                  "toolName": "StockNewsMcp",
                  "purpose": "确认本地新闻",
                  "inputSummary": "level=stock"
                }
              ]
            }
            """,
            "llm-trace-003");
        var service = CreateService(llm, new FakeMcpToolGateway());

        var result = await service.RunAsync(new StockCopilotLiveGateRequestDto(
            Symbol: "sh600000",
            Question: "看下日线结构和本地新闻",
            SessionKey: null,
            SessionTitle: null,
            TaskId: "phase-f-r1",
            AllowExternalSearch: false,
            Provider: "active",
            Model: "gpt-test",
            Temperature: 0.1));

        var turn = Assert.Single(result.Session.Turns);
        Assert.Equal("done", turn.FinalAnswer.Status);
        Assert.Equal(2, turn.ToolResults.Count);
        Assert.Contains(turn.ToolResults, item => item.ToolName == StockMcpToolNames.Kline && item.TraceId == "trace-kline-live");
        Assert.Contains(turn.ToolResults, item => item.ToolName == StockMcpToolNames.News && item.TraceId == "trace-news-live");
        Assert.Equal(2, result.Acceptance.ExecutedToolCallCount);
        Assert.True(result.Acceptance.OverallScore > 0m);
        Assert.Contains(turn.FinalAnswer.Constraints, item => item.Contains("LLM traceId=llm-trace-003", StringComparison.Ordinal));
    }

        [Fact]
        public async Task RunAsync_ShouldForwardWindowOptionsToGateway()
        {
                var llm = new FakeLlmService(
                        """
                        {
                            "plannerSummary": "分页读取基本面。",
                            "governorSummary": "按本地工具执行。",
                            "finalAnswerDraft": "这是 live gate 计划草案，后续以 tool result 为准。",
                            "toolCalls": [
                                {
                                    "roleId": "fundamentals_analyst",
                                    "toolName": "StockFundamentalsMcp",
                                    "purpose": "读取最新财报 facts",
                                    "inputSummary": "factSkip=1; factTake=2; evidenceSkip=2; evidenceTake=4"
                                }
                            ]
                        }
                        """,
                        "llm-trace-004");
                var gateway = new FakeMcpToolGateway();
                var service = CreateService(llm, gateway);

                var result = await service.RunAsync(new StockCopilotLiveGateRequestDto(
                        Symbol: "sh600000",
                        Question: "分页看最新财报",
                        SessionKey: null,
                        SessionTitle: null,
                        TaskId: "phase-f-r2",
                        AllowExternalSearch: false,
                        Provider: "active",
                        Model: "gpt-test",
                        Temperature: 0.1));

                Assert.Single(result.Session.Turns[0].ToolResults);
                Assert.Equal(1, gateway.LastFundamentalWindow?.FactSkip);
                Assert.Equal(2, gateway.LastFundamentalWindow?.FactTake);
                Assert.Equal(2, gateway.LastFundamentalWindow?.EvidenceSkip);
                Assert.Equal(4, gateway.LastFundamentalWindow?.EvidenceTake);
        }

    private static StockCopilotLiveGateService CreateService(FakeLlmService llm, FakeMcpToolGateway gateway)
    {
        return new StockCopilotLiveGateService(
            new FakeStockChatHistoryService(),
            llm,
            gateway,
            new RoleToolPolicyService(new McpServiceRegistry(), new StockAgentRoleContractRegistry()),
            new McpServiceRegistry(),
            new StockAgentRoleContractRegistry(),
            new FakeStockMarketContextService(),
            new StockCopilotAcceptanceService(new FakeReplayCalibrationService()));
    }

    private sealed class FakeLlmService : ILlmService
    {
        private readonly string _content;
        private readonly string _traceId;

        public FakeLlmService(string content, string traceId)
        {
            _content = content;
            _traceId = traceId;
        }

        public string? LastPrompt { get; private set; }

        public Task<LlmChatResult> ChatAsync(string provider, LlmChatRequest request, CancellationToken cancellationToken = default)
        {
            LastPrompt = request.Prompt;
            return Task.FromResult(new LlmChatResult(_content, _traceId));
        }
    }

    private sealed class FakeStockChatHistoryService : IStockChatHistoryService
    {
        public Task<StockChatSession> CreateSessionAsync(string symbol, string? title, string? sessionKey, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StockChatSession
            {
                Id = 1,
                Symbol = symbol,
                SessionKey = string.IsNullOrWhiteSpace(sessionKey) ? "session-live-gate" : sessionKey,
                Title = string.IsNullOrWhiteSpace(title) ? "live-gate" : title,
                CreatedAt = new DateTime(2026, 3, 26, 8, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 3, 26, 8, 0, 0, DateTimeKind.Utc)
            });
        }

        public Task<IReadOnlyList<StockChatSession>> GetSessionsAsync(string symbol, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<StockChatSession>>(Array.Empty<StockChatSession>());

        public Task<StockChatSession?> GetSessionAsync(string sessionKey, CancellationToken cancellationToken = default)
            => Task.FromResult<StockChatSession?>(null);

        public Task<IReadOnlyList<StockChatMessage>> GetMessagesAsync(string sessionKey, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<StockChatMessage>>(Array.Empty<StockChatMessage>());

        public Task SaveMessagesAsync(string sessionKey, IReadOnlyList<StockChatMessageDto> messages, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
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

    private sealed class FakeReplayCalibrationService : IStockAgentReplayCalibrationService
    {
        public Task<StockAgentReplayBaselineDto> BuildBaselineAsync(string? symbol, int take = 80, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StockAgentReplayBaselineDto(
                Scope: symbol ?? "all",
                GeneratedAt: new DateTime(2026, 3, 26, 8, 0, 0, DateTimeKind.Utc),
                SampleCount: 1,
                TraceableEvidenceRate: 100m,
                ParseRepairRate: 0m,
                PollutedEvidenceRate: 0m,
                RevisionCompletenessRate: 100m,
                Horizons: Array.Empty<StockAgentReplayHorizonMetricDto>(),
                Samples: Array.Empty<StockAgentReplaySampleDto>()));
        }
    }

    private sealed class FakeMcpToolGateway : IMcpToolGateway
    {
        public StockCopilotMcpWindowOptions? LastFundamentalWindow { get; private set; }

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotCompanyOverviewDataDto>> GetCompanyOverviewAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotProductDataDto>> GetProductAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotFundamentalsDataDto>> GetFundamentalsAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            LastFundamentalWindow = window;
            return Task.FromResult(new StockCopilotMcpEnvelopeDto<StockCopilotFundamentalsDataDto>(
                "trace-fundamentals-live",
                taskId ?? "task-fundamentals-live",
                StockMcpToolNames.Fundamentals,
                210,
                new StockCopilotMcpCacheDto(false, "test", DateTime.UtcNow),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new StockCopilotFundamentalsDataDto(symbol, DateTime.UtcNow, 5,
                [
                    new StockFundamentalFactDto("营业收入", "1680亿元", "东方财富最新财报"),
                    new StockFundamentalFactDto("归属净利润", "128亿元", "东方财富最新财报")
                ]),
                Array.Empty<StockCopilotMcpEvidenceDto>(),
                Array.Empty<StockCopilotMcpFeatureDto>(),
                new StockCopilotMcpMetaDto("v1", "local_required", StockMcpToolNames.Fundamentals, symbol, null, null, null)));
        }

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotShareholderDataDto>> GetShareholderAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotMarketContextDataDto>> GetMarketContextAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotSocialSentimentDataDto>> GetSocialSentimentAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotKlineDataDto>> GetKlineAsync(string symbol, string interval, int count, string? source, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StockCopilotMcpEnvelopeDto<StockCopilotKlineDataDto>(
                "trace-kline-live",
                taskId ?? "task-kline-live",
                StockMcpToolNames.Kline,
                280,
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
                new StockCopilotMcpMetaDto("v1", "local_required", StockMcpToolNames.Kline, symbol, interval, null, null)));
        }

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotMinuteDataDto>> GetMinuteAsync(string symbol, string? source, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotStrategyDataDto>> GetStrategyAsync(string symbol, string interval, int count, string? source, IReadOnlyList<string>? strategies, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotNewsDataDto>> GetNewsAsync(string symbol, string level, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StockCopilotMcpEnvelopeDto<StockCopilotNewsDataDto>(
                "trace-news-live",
                taskId ?? "task-news-live",
                StockMcpToolNames.News,
                190,
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
                new StockCopilotMcpMetaDto("v1", "local_required", StockMcpToolNames.News, symbol, null, level, null)));
        }

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotSearchDataDto>> SearchAsync(string query, bool trustedOnly, string? taskId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StockCopilotMcpEnvelopeDto<StockCopilotSearchDataDto>(
                "trace-search-live",
                taskId ?? "task-search-live",
                StockMcpToolNames.Search,
                500,
                new StockCopilotMcpCacheDto(false, "test", DateTime.UtcNow),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new StockCopilotSearchDataDto(query, "test-provider", trustedOnly, 1,
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
                new StockCopilotMcpMetaDto("v1", "external_gated", StockMcpToolNames.Search, null, null, query, null)));
        }
    }
}