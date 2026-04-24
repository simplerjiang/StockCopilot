using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services.IntentClassification;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend.WebSearch;

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
        Assert.Contains("当前请求：", result.Prompt, StringComparison.Ordinal);
        Assert.Contains("symbol=sh600000", result.Prompt, StringComparison.Ordinal);
        Assert.Contains("question=看下浦发银行的本地新闻证据", result.Prompt, StringComparison.Ordinal);
        Assert.Contains("allowExternalSearch=False", result.Prompt, StringComparison.Ordinal);
        Assert.Contains("工具注册表：", result.Prompt, StringComparison.Ordinal);
        Assert.Contains("local_required=CompanyOverviewMcp", result.Prompt, StringComparison.Ordinal);
        Assert.Contains("external_gated=StockSearchMcp", result.Prompt, StringComparison.Ordinal);
        Assert.Contains("company_overview_analyst: direct=true; access=local_required", result.Prompt, StringComparison.Ordinal);
        Assert.Contains("portfolio_manager", result.Prompt, StringComparison.Ordinal);
        Assert.Contains("direct=false; access=disabled; preferred=none", result.Prompt, StringComparison.Ordinal);
        Assert.Contains("evidenceSkip/evidenceTake", result.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("roleClass=", result.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("allowsDirectQueryTools=", result.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("minimumEvidenceCount=", result.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("counterTrendWarning", result.Prompt, StringComparison.Ordinal);
        Assert.True(
            result.Prompt.IndexOf("question=看下浦发银行的本地新闻证据", StringComparison.Ordinal)
            < result.Prompt.IndexOf("工具注册表：", StringComparison.Ordinal));
        Assert.Equal(LlmResponseFormats.Json, llm.Requests[0].ResponseFormat); // planner call
        Assert.Null(llm.Requests[1].ResponseFormat); // synthesis call is free-text
    }

    [Fact]
    public async Task RunAsync_ShouldRepairSingleProseResponseIntoValidJsonPlan()
    {
        var llm = new FakeLlmService(
            [
                "好的，请提供您需要我处理的请求。",
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
                """
            ],
            ["llm-trace-initial", "llm-trace-repair"]);
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

        var turn = Assert.Single(result.Session.Turns);
        Assert.Equal("done", turn.FinalAnswer.Status);
        Assert.Single(turn.ToolResults);
        Assert.Equal(3, llm.CallCount); // initial + repair + synthesis
        Assert.Equal("llm-trace-repair", result.LlmTraceId);
        Assert.All(llm.Requests.Take(2), request => Assert.Equal(LlmResponseFormats.Json, request.ResponseFormat));
        Assert.Null(llm.Requests[2].ResponseFormat); // synthesis call is free-text
        Assert.Contains("上次输出不是有效 JSON，现在只做一次 JSON repair。", llm.Requests[1].Prompt, StringComparison.Ordinal);
        Assert.Contains("必须字段：plannerSummary, governorSummary, finalAnswerDraft, toolCalls", llm.Requests[1].Prompt, StringComparison.Ordinal);
        Assert.Contains("上一次无效输出片段：", llm.Requests[1].Prompt, StringComparison.Ordinal);
        Assert.Contains("不要输出“好的，请提供您需要我处理的请求。”之类的对话文本。", llm.Requests[1].Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(llm.Requests[0].Prompt, llm.Requests[1].Prompt, StringComparison.Ordinal);
        Assert.True(llm.Requests[1].Prompt.Length < llm.Requests[0].Prompt.Length);
    }

    [Fact]
    public async Task RunAsync_ShouldTruncateLongInvalidOutputInRepairPrompt()
    {
        var invalidResponse = "bad-json:" + new string('x', 900);
        var llm = new FakeLlmService(
            [
                invalidResponse,
                """
                {
                  "plannerSummary": "先走本地新闻链路。",
                  "governorSummary": "不需要外部搜索。",
                  "finalAnswerDraft": "这是 live gate 计划草案，后续以 tool result 为准。",
                  "toolCalls": []
                }
                """
            ],
            ["llm-trace-initial", "llm-trace-repair"]);
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

        Assert.Equal("llm_plan_with_tool_receipts", result.Session.Turns[0].FinalAnswer.GroundingMode);
        Assert.Contains("...[truncated ", llm.Requests[1].Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidResponse, llm.Requests[1].Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ShouldStopAfterSingleRepairWhenStillNotJson()
    {
        var llm = new FakeLlmService(
            [
                "好的，请提供您需要我处理的请求。",
                "还是先告诉我你想做什么。"
            ],
            ["llm-trace-initial", "llm-trace-repair"]);
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

        var turn = Assert.Single(result.Session.Turns);
        Assert.Equal("failed", turn.FinalAnswer.Status);
        Assert.Equal("llm_plan_parse_failed", turn.FinalAnswer.GroundingMode);
        Assert.Equal(2, llm.CallCount);
        Assert.Equal("llm-trace-repair", result.LlmTraceId);
        Assert.Empty(turn.ToolCalls);
        Assert.Contains("repair 后仍不是有效 JSON", turn.FinalAnswer.Summary, StringComparison.Ordinal);
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
            new StockCopilotAcceptanceService(new FakeReplayCalibrationService()),
            new NoOpIntentClassifier(),
            new NoOpEvidencePackBuilder(),
            new NoOpHttpClientFactory(),
            new ConfigurationBuilder().Build(),
            NullLogger<StockCopilotLiveGateService>.Instance);
    }

    private sealed class NoOpHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class NoOpIntentClassifier : IQuestionIntentClassifier
    {
        public Task<QuestionIntent> ClassifyAsync(string question, string? stockSymbol = null, CancellationToken ct = default)
            => Task.FromResult(new QuestionIntent(IntentType.General, 1.0, false, false, SuggestedPipeline.LiveGate));
    }

    private sealed class NoOpEvidencePackBuilder : IEvidencePackBuilder
    {
        public Task<EvidencePack> BuildAsync(string symbol, string query, IntentType intent, CancellationToken ct = default)
            => Task.FromResult(new EvidencePack(symbol, query, intent, Array.Empty<RagCitationDto>(), Array.Empty<FinancialMetricSummary>(), null, Array.Empty<string>()));
        public string FormatAsPromptContext(EvidencePack pack) => string.Empty;
    }

    private sealed class FakeLlmService : ILlmService
    {
        private readonly IReadOnlyList<string> _contents;
        private readonly IReadOnlyList<string> _traceIds;
        private readonly List<LlmChatRequest> _requests = new();

        public FakeLlmService(string content, string traceId)
            : this([content], [traceId])
        {
        }

        public FakeLlmService(IReadOnlyList<string> contents, IReadOnlyList<string> traceIds)
        {
            _contents = contents;
            _traceIds = traceIds;
        }

        public string? LastPrompt { get; private set; }
        public LlmChatRequest? LastRequest { get; private set; }
        public int CallCount { get; private set; }
        public IReadOnlyList<LlmChatRequest> Requests => _requests;

        public Task<LlmChatResult> ChatAsync(string provider, LlmChatRequest request, CancellationToken cancellationToken = default)
        {
            var index = Math.Min(CallCount, _contents.Count - 1);
            CallCount += 1;
            LastRequest = request;
            LastPrompt = request.Prompt;
            _requests.Add(request);
            return Task.FromResult(new LlmChatResult(_contents[index], _traceIds[Math.Min(index, _traceIds.Count - 1)]));
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

        public Task<WebSearchResult> WebSearchAsync(string query, WebSearchOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WebSearchResult(Array.Empty<WebSearchItem>(), "stub", false));

        public Task<WebSearchResult> WebSearchNewsAsync(string query, WebSearchOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WebSearchResult(Array.Empty<WebSearchItem>(), "stub", false));

        public Task<WebReadResult> WebReadUrlAsync(string url, int maxChars = 8000, CancellationToken cancellationToken = default)
            => Task.FromResult(new WebReadResult("", url, 0, false));

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotFinancialReportDataDto>> GetFinancialReportAsync(string symbol, int periods, string? taskId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StockCopilotMcpEnvelopeDto<StockCopilotFinancialTrendDataDto>> GetFinancialTrendAsync(string symbol, int periods, string? taskId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<RagCitationDto>> SearchFinancialReportRagAsync(string symbol, string query, int topK = 5, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<RagCitationDto>());
    }
}