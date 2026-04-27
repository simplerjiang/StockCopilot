using Microsoft.Extensions.Logging.Abstractions;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend;
using System.Text.Json;
using Xunit;

namespace SimplerJiangAiAgent.Api.Tests;

public class RecommendRoleContractAndDispatchTests
{
    private static readonly string[] AllRoleIds =
    [
        RecommendAgentRoleIds.MacroAnalyst,
        RecommendAgentRoleIds.SectorHunter,
        RecommendAgentRoleIds.SmartMoneyAnalyst,
        RecommendAgentRoleIds.SectorBull,
        RecommendAgentRoleIds.SectorBear,
        RecommendAgentRoleIds.SectorJudge,
        RecommendAgentRoleIds.LeaderPicker,
        RecommendAgentRoleIds.GrowthPicker,
        RecommendAgentRoleIds.ChartValidator,
        RecommendAgentRoleIds.StockBull,
        RecommendAgentRoleIds.StockBear,
        RecommendAgentRoleIds.RiskReviewer,
        RecommendAgentRoleIds.Director,
    ];

    #region Contract Registry

    [Fact]
    public void AllRoles_HaveNonEmptySystemPrompt()
    {
        var registry = new RecommendRoleContractRegistry();
        foreach (var roleId in AllRoleIds)
        {
            var contract = registry.GetContract(roleId);
            Assert.False(string.IsNullOrWhiteSpace(contract.SystemPrompt), $"{roleId} has empty SystemPrompt");
            Assert.False(string.IsNullOrWhiteSpace(contract.DisplayName), $"{roleId} has empty DisplayName");
        }
    }

    [Fact]
    public void AllRoles_HaveReasonableToolHints()
    {
        var registry = new RecommendRoleContractRegistry();
        var rolesWithTools = new HashSet<string>
        {
            RecommendAgentRoleIds.MacroAnalyst,
            RecommendAgentRoleIds.SectorHunter,
            RecommendAgentRoleIds.SmartMoneyAnalyst,
            RecommendAgentRoleIds.SectorBull,
            RecommendAgentRoleIds.SectorBear,
            RecommendAgentRoleIds.LeaderPicker,
            RecommendAgentRoleIds.GrowthPicker,
            RecommendAgentRoleIds.ChartValidator,
            RecommendAgentRoleIds.StockBull,
            RecommendAgentRoleIds.StockBear,
        };

        foreach (var roleId in AllRoleIds)
        {
            var contract = registry.GetContract(roleId);
            if (rolesWithTools.Contains(roleId))
            {
                Assert.True(contract.ToolHints.Count > 0, $"{roleId} should have ToolHints");
                Assert.True(contract.MaxToolCalls > 0, $"{roleId} should have MaxToolCalls > 0");
            }
            else
            {
                Assert.Equal(0, contract.MaxToolCalls);
            }
        }
    }

    [Theory]
    [InlineData(RecommendStageType.MarketScan, 3)]
    [InlineData(RecommendStageType.SectorDebate, 3)]
    [InlineData(RecommendStageType.StockPicking, 3)]
    [InlineData(RecommendStageType.StockDebate, 3)]
    [InlineData(RecommendStageType.FinalDecision, 1)]
    public void GetStageRoleIds_ReturnsCorrectCount(RecommendStageType stage, int expectedCount)
    {
        var registry = new RecommendRoleContractRegistry();
        var roles = registry.GetStageRoleIds(stage);
        Assert.Equal(expectedCount, roles.Count);
    }

    [Fact]
    public void GetContract_UnknownRole_Throws()
    {
        var registry = new RecommendRoleContractRegistry();
        Assert.Throws<KeyNotFoundException>(() => registry.GetContract("nonexistent_role"));
    }

    #endregion

    #region RoleExecutor tool_call loop

    [Fact]
    public async Task ExecuteAsync_WithToolCall_CallsDispatcherAndLoops()
    {
        var toolCallJson = """{"tool_call":{"name":"web_search","args":{"query":"A股宏观"}}}""";
        var finalJson = """{"sentiment":"bullish","keyDrivers":[]}""";
        var llmService = new SequentialLlmService([toolCallJson, finalJson]);
        var dispatcher = new FakeToolDispatcher();
        var registry = new RecommendRoleContractRegistry();
        var eventBus = new RecommendEventBus();

        var executor = new RecommendationRoleExecutor(
            llmService, eventBus, registry, dispatcher,
            NullLogger<RecommendationRoleExecutor>.Instance);

        var ctx = new RecommendRoleExecutionContext(
            RecommendAgentRoleIds.MacroAnalyst, "", "分析当前宏观环境",
            null, 1, 1, 1);

        var result = await executor.ExecuteAsync(ctx);

        Assert.True(result.Success);
        Assert.Equal(1, result.ToolCallCount);
        Assert.Equal(finalJson, result.OutputJson);
        Assert.Single(dispatcher.Calls);
        Assert.Equal("web_search", dispatcher.Calls[0].ToolName);
        Assert.Equal(2, llmService.CallCount);
        Assert.All(llmService.Requests, request => Assert.Equal(LlmResponseFormats.Json, request.ResponseFormat));
    }

    [Fact]
    public async Task ExecuteAsync_NoToolCall_ReturnsDirectly()
    {
        var finalJson = """{"sentiment":"neutral","keyDrivers":[]}""";
        var llmService = new SequentialLlmService([finalJson]);
        var dispatcher = new FakeToolDispatcher();
        var registry = new RecommendRoleContractRegistry();
        var eventBus = new RecommendEventBus();

        var executor = new RecommendationRoleExecutor(
            llmService, eventBus, registry, dispatcher,
            NullLogger<RecommendationRoleExecutor>.Instance);

        var ctx = new RecommendRoleExecutionContext(
            RecommendAgentRoleIds.SectorJudge, "", "裁定板块",
            null, 1, 1, 1);

        var result = await executor.ExecuteAsync(ctx);

        Assert.True(result.Success);
        Assert.Equal(0, result.ToolCallCount);
        Assert.Equal(finalJson, result.OutputJson);
        Assert.Empty(dispatcher.Calls);
        Assert.Contains("## 最终输出 JSON schema", llmService.Requests[0].Prompt, StringComparison.Ordinal);
        Assert.Contains("不要输出解释、Markdown、代码块、自然语言", llmService.Requests[0].Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_EmitsRoleSummaryReady_WithStageType()
    {
        var finalJson = """{"sentiment":"neutral","keyDrivers":[]}""";
        var llmService = new SequentialLlmService([finalJson]);
        var dispatcher = new FakeToolDispatcher();
        var registry = new RecommendRoleContractRegistry();
        var eventBus = new RecommendEventBus();

        var executor = new RecommendationRoleExecutor(
            llmService, eventBus, registry, dispatcher,
            NullLogger<RecommendationRoleExecutor>.Instance);

        var ctx = new RecommendRoleExecutionContext(
            RecommendAgentRoleIds.SectorJudge, "", "裁定板块",
            null, 1, 1, 1, "SectorDebate");

        await executor.ExecuteAsync(ctx);

        var events = eventBus.Peek(1);
        var summaryReady = Assert.Single(events.Where(e => e.EventType == RecommendEventType.RoleSummaryReady));
        Assert.Equal("SectorDebate", summaryReady.StageType);
        Assert.Equal(finalJson, summaryReady.Summary);
    }

    [Fact]
    public async Task ExecuteAsync_NormalizesSectorCodeNamePairs_BeforeReturningRoleOutput()
    {
        var finalJson = """
        {
            "selectedSectors": [
                { "name": "新能源汽车", "code": "880398" },
                { "name": "天然气", "code": "880398" }
            ],
            "sectorCards": [
                { "sectorName": "证券", "sectorCode": "880398" }
            ]
        }
        """;
        var llmService = new SequentialLlmService([finalJson]);
        var dispatcher = new FakeToolDispatcher();
        var registry = new RecommendRoleContractRegistry();
        var eventBus = new RecommendEventBus();
        var resolver = new FakeSectorCodeNameResolver(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["880398"] = "天然气",
            ["880472"] = "证券",
            ["880987"] = "新能源汽车"
        });

        var executor = new RecommendationRoleExecutor(
            llmService, eventBus, registry, dispatcher,
            NullLogger<RecommendationRoleExecutor>.Instance,
            sessionLogger: null,
            sectorCodeNameResolver: resolver);

        var ctx = new RecommendRoleExecutionContext(
            RecommendAgentRoleIds.SectorJudge, "", "裁定板块",
            null, 1, 1, 1, "SectorDebate");

        var result = await executor.ExecuteAsync(ctx);

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(result.OutputJson!);
        var selected = doc.RootElement.GetProperty("selectedSectors");
        Assert.Equal("新能源汽车", selected[0].GetProperty("name").GetString());
        Assert.Equal("880987", selected[0].GetProperty("code").GetString());
        Assert.Equal("天然气", selected[1].GetProperty("name").GetString());
        Assert.Equal("880398", selected[1].GetProperty("code").GetString());

        var sectorCard = doc.RootElement.GetProperty("sectorCards")[0];
        Assert.Equal("证券", sectorCard.GetProperty("sectorName").GetString());
        Assert.Equal("880472", sectorCard.GetProperty("sectorCode").GetString());

        var summaryReady = Assert.Single(eventBus.Peek(1).Where(e => e.EventType == RecommendEventType.RoleSummaryReady));
        Assert.Equal(result.OutputJson, summaryReady.Summary);
    }

    [Fact]
    public async Task ExecuteAsync_MalformedToolCall_FailsAfterBoundedCorrections()
    {
        var brokenJson = """{"tool_call":{"name":"web_search","args":{"query":""";
        var llmService = new SequentialLlmService([brokenJson]);
        var dispatcher = new FakeToolDispatcher();
        var registry = new RecommendRoleContractRegistry();
        var eventBus = new RecommendEventBus();

        var executor = new RecommendationRoleExecutor(
            llmService, eventBus, registry, dispatcher,
            NullLogger<RecommendationRoleExecutor>.Instance);

        var ctx = new RecommendRoleExecutionContext(
            RecommendAgentRoleIds.MacroAnalyst, "", "分析",
            null, 1, 1, 1);

        var result = await executor.ExecuteAsync(ctx);

        Assert.False(result.Success);
        Assert.Equal(0, result.ToolCallCount);
        Assert.Null(result.OutputJson);
        Assert.Equal("LLM_INVALID_JSON_RESPONSE", result.ErrorCode);
        Assert.Contains("连续 2 次返回非 JSON / 非法响应", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(2, llmService.CallCount);
        Assert.Empty(dispatcher.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_RespectsMaxToolCallBudget()
    {
        var toolCallJson = """{"tool_call":{"name":"web_search","args":{"query":"test"}}}""";
        var finalJson = """{"done":true}""";

        // MacroAnalyst has MaxToolCalls=5: produce 5 tool_call responses + 1 final
        var responses = new List<string>();
        for (int i = 0; i < 5; i++) responses.Add(toolCallJson);
        responses.Add(finalJson);
        var llmService = new SequentialLlmService(responses);
        var dispatcher = new FakeToolDispatcher();
        var registry = new RecommendRoleContractRegistry();
        var eventBus = new RecommendEventBus();

        var executor = new RecommendationRoleExecutor(
            llmService, eventBus, registry, dispatcher,
            NullLogger<RecommendationRoleExecutor>.Instance);

        var ctx = new RecommendRoleExecutionContext(
            RecommendAgentRoleIds.MacroAnalyst, "", "分析",
            null, 1, 1, 1);

        var result = await executor.ExecuteAsync(ctx);

        Assert.True(result.Success);
        Assert.Equal(5, result.ToolCallCount);
        Assert.Equal(finalJson, result.OutputJson);
    }

    #endregion

    #region TryParseToolCall unit tests

    [Fact]
    public void TryParseToolCall_ValidJson_ReturnsTrue()
    {
        var json = """{"tool_call":{"name":"stock_kline","args":{"symbol":"000001","interval":"day","count":"60"}}}""";
        var parsed = RecommendationRoleExecutor.TryParseToolCall(json, out var name, out var args);
        Assert.True(parsed);
        Assert.Equal("stock_kline", name);
        Assert.Equal("000001", args["symbol"]);
        Assert.Equal("day", args["interval"]);
        Assert.Equal("60", args["count"]);
    }

    [Fact]
    public void TryParseToolCall_WithSurroundingText_StillParses()
    {
        var content = """好的，我需要搜索相关信息。{"tool_call":{"name":"web_search","args":{"query":"A股政策"}}}这是一些额外的文字。""";
        var parsed = RecommendationRoleExecutor.TryParseToolCall(content, out var name, out _);
        Assert.True(parsed);
        Assert.Equal("web_search", name);
    }

    [Fact]
    public void TryParseToolCall_NoToolCall_ReturnsFalse()
    {
        var content = """{"sentiment":"bullish","keyDrivers":[]}""";
        var parsed = RecommendationRoleExecutor.TryParseToolCall(content, out _, out _);
        Assert.False(parsed);
    }

    [Fact]
    public void TryParseToolCall_BrokenJson_ReturnsFalse()
    {
        var content = """{"tool_call":{"name":""";
        var parsed = RecommendationRoleExecutor.TryParseToolCall(content, out _, out _);
        Assert.False(parsed);
    }

    [Fact]
    public void TryParseToolCall_NumericArg_SerializedAsRawText()
    {
        var json = """{"tool_call":{"name":"stock_kline","args":{"symbol":"600519","count":30}}}""";
        var parsed = RecommendationRoleExecutor.TryParseToolCall(json, out _, out var args);
        Assert.True(parsed);
        Assert.Equal("30", args["count"]);
    }

    #endregion

    #region Truncate helper

    [Fact]
    public void Truncate_Null_ReturnsEmpty()
    {
        Assert.Equal("", RecommendationRoleExecutor.Truncate(null, 100));
    }

    [Fact]
    public void Truncate_ShortString_ReturnsUnchanged()
    {
        Assert.Equal("hello", RecommendationRoleExecutor.Truncate("hello", 100));
    }

    [Fact]
    public void Truncate_LongString_TruncatesWithEllipsis()
    {
        var input = new string('x', 50);
        var result = RecommendationRoleExecutor.Truncate(input, 10);
        Assert.Equal(13, result.Length); // 10 chars + "..."
        Assert.EndsWith("...", result);
    }

    #endregion

    #region DetailJson in events

    [Fact]
    public async Task ExecuteAsync_ToolDispatched_HasDetailJson()
    {
        var toolCallJson = """{"tool_call":{"name":"web_search","args":{"query":"test"}}}""";
        var finalJson = """{"done":true}""";
        var llmService = new SequentialLlmService([toolCallJson, finalJson]);
        var dispatcher = new FakeToolDispatcher();
        var registry = new RecommendRoleContractRegistry();
        var eventBus = new RecommendEventBus();

        var executor = new RecommendationRoleExecutor(
            llmService, eventBus, registry, dispatcher,
            NullLogger<RecommendationRoleExecutor>.Instance);

        var ctx = new RecommendRoleExecutionContext(
            RecommendAgentRoleIds.MacroAnalyst, "", "分析",
            null, 1, 1, 1);

        await executor.ExecuteAsync(ctx);

        var events = eventBus.Peek(1);
        var dispatched = events.First(e => e.EventType == RecommendEventType.ToolDispatched);
        Assert.NotNull(dispatched.DetailJson);
        using var doc = System.Text.Json.JsonDocument.Parse(dispatched.DetailJson);
        Assert.Equal("web_search", doc.RootElement.GetProperty("toolName").GetString());
        Assert.Equal("test", doc.RootElement.GetProperty("args").GetProperty("query").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ToolCompleted_HasDetailJsonWithPreview()
    {
        var toolCallJson = """{"tool_call":{"name":"web_search","args":{"query":"test"}}}""";
        var finalJson = """{"done":true}""";
        var llmService = new SequentialLlmService([toolCallJson, finalJson]);
        var dispatcher = new FakeToolDispatcher();
        var registry = new RecommendRoleContractRegistry();
        var eventBus = new RecommendEventBus();

        var executor = new RecommendationRoleExecutor(
            llmService, eventBus, registry, dispatcher,
            NullLogger<RecommendationRoleExecutor>.Instance);

        var ctx = new RecommendRoleExecutionContext(
            RecommendAgentRoleIds.MacroAnalyst, "", "分析",
            null, 1, 1, 1);

        await executor.ExecuteAsync(ctx);

        var events = eventBus.Peek(1);
        var completed = events.First(e => e.EventType == RecommendEventType.ToolCompleted);
        Assert.NotNull(completed.DetailJson);
        using var doc = System.Text.Json.JsonDocument.Parse(completed.DetailJson);
        Assert.Equal("web_search", doc.RootElement.GetProperty("toolName").GetString());
        Assert.Equal("Completed", doc.RootElement.GetProperty("status").GetString());
        Assert.NotNull(doc.RootElement.GetProperty("resultPreview").GetString());
    }

    #endregion

    #region Test Helpers

    private sealed class SequentialLlmService : ILlmService
    {
        private readonly IReadOnlyList<string> _responses;
        private int _callIndex;
        public List<LlmChatRequest> Requests { get; } = new();

        public int CallCount => _callIndex;

        public SequentialLlmService(IReadOnlyList<string> responses) => _responses = responses;

        public Task<LlmChatResult> ChatAsync(string provider, LlmChatRequest request, CancellationToken cancellationToken)
        {
            var idx = _callIndex < _responses.Count ? _callIndex : _responses.Count - 1;
            _callIndex++;
            Requests.Add(request);
            return Task.FromResult(new LlmChatResult(_responses[idx], $"trace-{_callIndex}"));
        }
    }

    private sealed class FakeSectorCodeNameResolver : IRecommendSectorCodeNameResolver
    {
        private readonly IReadOnlyDictionary<string, string> _map;

        public FakeSectorCodeNameResolver(IReadOnlyDictionary<string, string> map)
        {
            _map = map;
        }

        public Task<IReadOnlyDictionary<string, string>> GetLatestCodeNameMapAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_map);
        }
    }

    private sealed record ToolCallRecord(string ToolName, Dictionary<string, string> Args);

    private sealed class FakeToolDispatcher : IRecommendToolDispatcher
    {
        public List<ToolCallRecord> Calls { get; } = new();

        public Task<string> DispatchAsync(string toolName, Dictionary<string, string> args, CancellationToken ct = default)
        {
            Calls.Add(new ToolCallRecord(toolName, new Dictionary<string, string>(args)));
            return Task.FromResult("""{"data":"mock_tool_result"}""");
        }
    }

    #endregion
}
