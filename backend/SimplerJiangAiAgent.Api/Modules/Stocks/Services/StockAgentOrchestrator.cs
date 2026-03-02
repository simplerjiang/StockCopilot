using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Infrastructure.Logging;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IStockAgentOrchestrator
{
    Task<StockAgentResponseDto> RunAsync(StockAgentRequestDto request, CancellationToken cancellationToken = default);
    Task<StockAgentResultDto> RunSingleAsync(StockAgentSingleRequestDto request, CancellationToken cancellationToken = default);
}

public sealed class StockAgentOrchestrator : IStockAgentOrchestrator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IStockDataService _dataService;
    private readonly ILlmService _llmService;
    private readonly IFileLogWriter _fileLogWriter;

    public StockAgentOrchestrator(IStockDataService dataService, ILlmService llmService, IFileLogWriter fileLogWriter)
    {
        _dataService = dataService;
        _llmService = llmService;
        _fileLogWriter = fileLogWriter;
    }

    public async Task<StockAgentResponseDto> RunAsync(StockAgentRequestDto request, CancellationToken cancellationToken = default)
    {
        var symbol = request.Symbol?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("symbol 不能为空", nameof(request.Symbol));
        }

        var interval = string.IsNullOrWhiteSpace(request.Interval) ? "day" : request.Interval.Trim();
        var count = Math.Clamp(request.Count ?? 60, 10, 120);

        var context = await BuildContextAsync(symbol, interval, count, request.Source, cancellationToken);
        var quote = context.Quote;

        var subAgents = new[]
        {
            StockAgentKind.StockNews,
            StockAgentKind.SectorNews,
            StockAgentKind.FinancialAnalysis,
            StockAgentKind.TrendAnalysis
        };

        var subTasks = subAgents
            .Select(kind =>
            {
                var contextJson = SerializeContext(kind, context);
                return RunAgentAsync(kind, contextJson, request, Array.Empty<StockAgentResultDto>(), cancellationToken);
            })
            .ToArray();

        var subResults = await Task.WhenAll(subTasks);

        var commanderContextJson = SerializeContext(StockAgentKind.Commander, context);
        var commanderResult = await RunAgentAsync(StockAgentKind.Commander, commanderContextJson, request, subResults, cancellationToken);

        var results = new List<StockAgentResultDto> { commanderResult };
        results.AddRange(subResults);

        return new StockAgentResponseDto(quote.Symbol, quote.Name, quote.Timestamp, results);
    }

    public async Task<StockAgentResultDto> RunSingleAsync(StockAgentSingleRequestDto request, CancellationToken cancellationToken = default)
    {
        var symbol = request.Symbol?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("symbol 不能为空", nameof(request.Symbol));
        }

        var agentId = request.AgentId?.Trim() ?? string.Empty;
        if (!StockAgentCatalog.TryGetKind(agentId, out var kind))
        {
            throw new ArgumentException("agentId 无效", nameof(request.AgentId));
        }

        var interval = string.IsNullOrWhiteSpace(request.Interval) ? "day" : request.Interval.Trim();
        var count = Math.Clamp(request.Count ?? 60, 10, 120);

        var context = await BuildContextAsync(symbol, interval, count, request.Source, cancellationToken);
        var contextJson = SerializeContext(kind, context);
        var dependencies = request.DependencyResults ?? Array.Empty<StockAgentResultDto>();

        return await RunAgentAsync(kind, contextJson, new StockAgentRequestDto(
            symbol,
            request.Source,
            request.Provider,
            request.Model,
            request.Interval,
            request.Count,
            request.UseInternet), dependencies, cancellationToken);
    }

    private async Task<StockAgentContextDto> BuildContextAsync(
        string symbol,
        string interval,
        int count,
        string? source,
        CancellationToken cancellationToken)
    {
        var quote = await _dataService.GetQuoteAsync(symbol, source);
        var kLines = await _dataService.GetKLineAsync(symbol, interval, count, source);
        var minuteLines = await _dataService.GetMinuteLineAsync(symbol, source);
        var messages = await _dataService.GetIntradayMessagesAsync(symbol, source);
        var newsContext = StockAgentNewsContextPolicy.Apply(messages, DateTime.Now);

        return new StockAgentContextDto(
            quote,
            kLines.OrderBy(item => item.Date).TakeLast(60).ToArray(),
            minuteLines.OrderBy(item => item.Date).ThenBy(item => item.Time).TakeLast(120).ToArray(),
            newsContext.Messages,
            newsContext.Policy,
            DateTime.Now);
    }

    private async Task<StockAgentResultDto> RunAgentAsync(
        StockAgentKind kind,
        string contextJson,
        StockAgentRequestDto request,
        IReadOnlyList<StockAgentResultDto> dependencyResults,
        CancellationToken cancellationToken)
    {
        var definition = StockAgentCatalog.GetDefinition(kind);
        var prompt = StockAgentPromptBuilder.BuildPrompt(kind, contextJson, dependencyResults);
        var provider = string.IsNullOrWhiteSpace(request.Provider) ? "openai" : request.Provider.Trim();

        try
        {
            var result = await _llmService.ChatAsync(
                provider,
                new LlmChatRequest(prompt, request.Model, 0.4, request.UseInternet),
                cancellationToken);

            var raw = result.Content?.Trim() ?? string.Empty;
            if (!StockAgentJsonParser.TryParse(raw, out var data, out var parseError))
            {
                _fileLogWriter.Write("LLM", $"parse_error agent={definition.Id} message={parseError} raw={raw}");

                var currentRaw = raw;
                for (var attempt = 1; attempt <= 2; attempt++)
                {
                    var repairPrompt = StockAgentPromptBuilder.BuildRepairPrompt(kind, currentRaw);
                    var repair = await _llmService.ChatAsync(
                        provider,
                        new LlmChatRequest(repairPrompt, request.Model, 0.2, false),
                        cancellationToken);

                    var repairRaw = repair.Content?.Trim() ?? string.Empty;
                    if (StockAgentJsonParser.TryParse(repairRaw, out var repairData, out _))
                    {
                        var normalizedRepairData = StockAgentResultNormalizer.Normalize(kind, repairData!.Value);
                        return new StockAgentResultDto(definition.Id, definition.Name, true, null, normalizedRepairData, repairRaw);
                    }

                    _fileLogWriter.Write("LLM", $"parse_error agent={definition.Id} stage=repair attempt={attempt} raw={repairRaw}");
                    currentRaw = repairRaw;
                }

                return new StockAgentResultDto(definition.Id, definition.Name, false, parseError, null, currentRaw);
            }

            var normalizedData = StockAgentResultNormalizer.Normalize(kind, data!.Value);
            return new StockAgentResultDto(definition.Id, definition.Name, true, null, normalizedData, raw);
        }
        catch (Exception ex)
        {
            return new StockAgentResultDto(definition.Id, definition.Name, false, ex.Message, null, null);
        }
    }

    private sealed record StockAgentContextDto(
        StockQuoteDto Quote,
        IReadOnlyList<KLinePointDto> KLines,
        IReadOnlyList<MinuteLinePointDto> MinuteLines,
        IReadOnlyList<IntradayMessageDto> Messages,
        StockAgentNewsPolicyDto NewsPolicy,
        DateTime RequestTime
    );

    private static string SerializeContext(StockAgentKind kind, StockAgentContextDto context)
    {
        if (kind == StockAgentKind.TrendAnalysis)
        {
            return JsonSerializer.Serialize(context, JsonOptions);
        }

        var slimContext = new StockAgentSlimContextDto(
            context.Quote,
            context.Messages,
            context.NewsPolicy,
            context.RequestTime);

        return JsonSerializer.Serialize(slimContext, JsonOptions);
    }

    private sealed record StockAgentSlimContextDto(
        StockQuoteDto Quote,
        IReadOnlyList<IntradayMessageDto> Messages,
        StockAgentNewsPolicyDto NewsPolicy,
        DateTime RequestTime
    );
}

internal sealed record StockAgentNewsPolicyDto(
    int PreferredLookbackHours,
    int ActualLookbackHours,
    bool ExpandedWindow,
    int CandidateCount,
    int SelectedCount
);

internal static class StockAgentNewsContextPolicy
{
    private static readonly string[] TrustedSourceKeywords =
    {
        "交易所", "证监", "公告", "新华社", "证券", "新浪", "东方财富", "腾讯", "财联社", "上交所", "深交所"
    };

    private static readonly string[] RelevanceKeywords =
    {
        "公告", "业绩", "增持", "减持", "回购", "中标", "签约", "诉讼", "被罚", "停牌", "复牌", "订单", "评级", "研报"
    };

    private const int PreferredLookbackHours = 72;
    private const int ExtendedLookbackHours = 168;
    private const int MinPreferredMessageCount = 6;
    private const int MaxSelectedMessages = 20;

    public static (IReadOnlyList<IntradayMessageDto> Messages, StockAgentNewsPolicyDto Policy) Apply(
        IReadOnlyList<IntradayMessageDto> messages,
        DateTime requestTime)
    {
        var normalized = messages
            .Where(item => item is not null)
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .ToList();

        var preferredCutoff = requestTime.AddHours(-PreferredLookbackHours);
        var preferredWindow = normalized
            .Where(item => item.PublishedAt >= preferredCutoff && item.PublishedAt <= requestTime)
            .ToList();

        var useExtended = preferredWindow.Count < MinPreferredMessageCount;
        var actualLookbackHours = useExtended ? ExtendedLookbackHours : PreferredLookbackHours;
        var actualCutoff = requestTime.AddHours(-actualLookbackHours);

        var candidates = normalized
            .Where(item => item.PublishedAt >= actualCutoff && item.PublishedAt <= requestTime)
            .ToList();

        var trustedCandidates = candidates.Where(item => IsTrustedSource(item.Source)).ToList();
        var candidatesToRank = trustedCandidates.Count > 0 ? trustedCandidates : candidates;

        var ranked = candidatesToRank
            .Select(item => new
            {
                Item = item,
                Score = ComputeScore(item, requestTime)
            })
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Item.PublishedAt)
            .Take(MaxSelectedMessages)
            .Select(item => item.Item)
            .ToArray();

        var policy = new StockAgentNewsPolicyDto(
            PreferredLookbackHours,
            actualLookbackHours,
            useExtended,
            candidates.Count,
            ranked.Length);

        return (ranked, policy);
    }

    private static decimal ComputeScore(IntradayMessageDto item, DateTime requestTime)
    {
        var recencyScore = ComputeRecencyScore(item.PublishedAt, requestTime);
        var sourceScore = ComputeSourceScore(item.Source);
        var relevanceScore = ComputeRelevanceScore(item.Title);
        return recencyScore * 0.55m + sourceScore * 0.30m + relevanceScore * 0.15m;
    }

    private static decimal ComputeRecencyScore(DateTime publishedAt, DateTime requestTime)
    {
        var ageHours = Math.Max(0, (requestTime - publishedAt).TotalHours);
        if (ageHours <= 6)
        {
            return 1.0m;
        }

        if (ageHours <= 24)
        {
            return 0.85m;
        }

        if (ageHours <= 72)
        {
            return 0.65m;
        }

        return 0.35m;
    }

    private static decimal ComputeSourceScore(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return 0.2m;
        }

        if (source.Contains("交易所", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("证监", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("公告", StringComparison.OrdinalIgnoreCase))
        {
            return 1.0m;
        }

        if (IsTrustedSource(source))
        {
            return 0.8m;
        }

        return 0.45m;
    }

    private static decimal ComputeRelevanceScore(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return 0.2m;
        }

        var hits = RelevanceKeywords.Count(keyword => title.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        return Math.Clamp(0.3m + hits * 0.15m, 0.3m, 1.0m);
    }

    private static bool IsTrustedSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        return TrustedSourceKeywords.Any(keyword => source.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}

internal enum StockAgentKind
{
    Commander,
    StockNews,
    SectorNews,
    FinancialAnalysis,
    TrendAnalysis
}

internal sealed record StockAgentDefinition(string Id, string Name);

internal static class StockAgentCatalog
{
    private static readonly IReadOnlyDictionary<StockAgentKind, StockAgentDefinition> Definitions =
        new Dictionary<StockAgentKind, StockAgentDefinition>
        {
            [StockAgentKind.Commander] = new("commander", "指挥Agent"),
            [StockAgentKind.StockNews] = new("stock_news", "个股资讯Agent"),
            [StockAgentKind.SectorNews] = new("sector_news", "板块资讯Agent"),
            [StockAgentKind.FinancialAnalysis] = new("financial_analysis", "个股分析Agent"),
            [StockAgentKind.TrendAnalysis] = new("trend_analysis", "走势分析Agent")
        };

    public static StockAgentDefinition GetDefinition(StockAgentKind kind)
    {
        return Definitions[kind];
    }

    public static bool TryGetKind(string? agentId, out StockAgentKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return false;
        }

        foreach (var entry in Definitions)
        {
            if (string.Equals(entry.Value.Id, agentId, StringComparison.OrdinalIgnoreCase))
            {
                kind = entry.Key;
                return true;
            }
        }

        return false;
    }
}

internal static class StockAgentPromptBuilder
{
    public static string BuildPrompt(
        StockAgentKind kind,
        string contextJson,
        IReadOnlyList<StockAgentResultDto> dependencyResults)
    {
        return kind switch
        {
            StockAgentKind.Commander => BuildCommanderPrompt(contextJson, dependencyResults),
            StockAgentKind.StockNews => BuildStockNewsPrompt(contextJson),
            StockAgentKind.SectorNews => BuildSectorNewsPrompt(contextJson),
            StockAgentKind.FinancialAnalysis => BuildFinancialPrompt(contextJson),
            StockAgentKind.TrendAnalysis => BuildTrendPrompt(contextJson),
            _ => BuildStockNewsPrompt(contextJson)
        };
    }

    private static string BuildCommanderPrompt(string contextJson, IReadOnlyList<StockAgentResultDto> dependencyResults)
    {
        var agentInputs = dependencyResults.Select(item => new
        {
            item.AgentId,
            item.AgentName,
            item.Success,
            item.Error,
            Data = item.Data
        });
        var agentsJson = JsonSerializer.Serialize(agentInputs, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        const string template =
            "你是指挥Agent。根据给定的股票上下文、以及其他Agent的输出，产出最终评估。\n" +
            "要求：\n" +
            "1. 必须输出严格JSON，不要Markdown，不要代码块，不要多余文字。\n" +
            "2. 所有字段必须存在；没有数据用null或空数组。\n" +
            "3. 百分比字段用数值，不带%符号。\n" +
            "4. 评估必须基于其他Agent输出汇总后再给出分数与结论。\n" +
            "5. 所有建议必须给出证据来源、触发条件、失效条件和风险上限。\n\n" +
            "输出JSON结构：\n" +
            "{\n" +
            "  \"agent\": \"commander\",\n" +
            "  \"summary\": \"string\",\n" +
            "  \"metrics\": {\n" +
            "    \"price\": number,\n" +
            "    \"changePercent\": number,\n" +
            "    \"turnoverRate\": number,\n" +
            "    \"innerVolume\": number|null,\n" +
            "    \"outerVolume\": number|null,\n" +
            "    \"sector\": \"string|null\",\n" +
            "    \"date\": \"YYYY-MM-DD\"\n" +
            "  },\n" +
            "  \"recommendation\": {\n" +
            "    \"action\": \"观察|试仓|加仓|减仓|清仓\",\n" +
            "    \"targetPrice\": number|null,\n" +
            "    \"takeProfitPrice\": number|null,\n" +
            "    \"stopLossPrice\": number|null,\n" +
            "    \"timeHorizon\": \"string|null\",\n" +
            "    \"positionPercent\": number|null,\n" +
            "    \"entryScore\": number,\n" +
            "    \"valuationScore\": number,\n" +
            "    \"confidence\": number,\n" +
            "    \"rating\": \"string\"\n" +
            "  },\n" +
            "  \"reasons\": [\"string\"],\n" +
            "  \"evidence\": [\n" +
            "    {\n" +
            "      \"point\": \"string\",\n" +
            "      \"source\": \"string\",\n" +
            "      \"publishedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
            "      \"url\": \"string|null\"\n" +
            "    }\n" +
            "  ],\n" +
            "  \"triggers\": [\"string\"],\n" +
            "  \"invalidations\": [\"string\"],\n" +
            "  \"riskLimits\": [\"string\"],\n" +
            "  \"signals\": [\"string\"],\n" +
            "  \"risks\": [\"string\"],\n" +
            "  \"chart\": null\n" +
            "}\n\n" +
            "股票上下文JSON：\n";

        return string.Concat(template, contextJson, "\n\n其他Agent输出JSON：\n", agentsJson);
    }

    private static string BuildStockNewsPrompt(string contextJson)
    {
        const string template =
            "你是个股资讯Agent。请联网获取当前及近期该股票的重要消息，并做情绪统计。\n" +
            "要求：\n" +
            "1. 必须输出严格JSON，不要Markdown，不要代码块，不要多余文字。\n" +
            "2. 所有字段必须存在；没有数据用null或空数组。\n" +
            "3. 百分比字段用数值，不带%符号。\n" +
            "4. 证据默认只允许最近72小时；若有效证据不足可扩窗到7天，并在summary中明确标注“扩窗到7天”。\n" +
            "5. 禁止将无来源或无发布时间（publishedAt）的内容作为核心证据。\n" +
            "6. evidence中每条都必须包含source、publishedAt、crawledAt（抓取时间）。\n\n" +
            "输出JSON结构：\n" +
            "{\n" +
            "  \"agent\": \"stock_news\",\n" +
            "  \"summary\": \"string\",\n" +
            "  \"confidence\": number,\n" +
            "  \"sentiment\": {\n" +
            "    \"positive\": number,\n" +
            "    \"neutral\": number,\n" +
            "    \"negative\": number,\n" +
            "    \"overall\": \"string\"\n" +
            "  },\n" +
            "  \"events\": [\n" +
            "    {\n" +
            "      \"title\": \"string\",\n" +
            "      \"category\": \"利好|中性|利空\",\n" +
            "      \"publishedAt\": \"YYYY-MM-DD HH:mm\",\n" +
            "      \"source\": \"string\",\n" +
            "      \"impact\": number,\n" +
            "      \"url\": \"string|null\"\n" +
            "    }\n" +
            "  ],\n" +
            "  \"evidence\": [\n" +
            "    {\n" +
            "      \"point\": \"string\",\n" +
            "      \"source\": \"string\",\n" +
            "      \"publishedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
            "      \"crawledAt\": \"YYYY-MM-DD HH:mm\",\n" +
            "      \"url\": \"string|null\"\n" +
            "    }\n" +
            "  ],\n" +
            "  \"signals\": [\"string\"],\n" +
            "  \"triggers\": [\"string\"],\n" +
            "  \"invalidations\": [\"string\"],\n" +
            "  \"riskLimits\": [\"string\"],\n" +
            "  \"risks\": [\"string\"],\n" +
            "  \"chart\": null\n" +
            "}\n\n" +
            "股票上下文JSON：\n";

        return string.Concat(template, contextJson);
    }

    private static string BuildSectorNewsPrompt(string contextJson)
    {
        const string template =
            "你是板块资讯Agent。请联网获取该股票所属板块的最新资讯和同板块个股涨跌。\n" +
            "要求：\n" +
            "1. 必须输出严格JSON，不要Markdown，不要代码块，不要多余文字。\n" +
            "2. 所有字段必须存在；没有数据用null或空数组。\n" +
            "3. 百分比字段用数值，不带%符号。\n" +
            "4. 证据默认只允许最近72小时；若有效证据不足可扩窗到7天，并在summary中明确标注“扩窗到7天”。\n" +
            "5. 禁止将无来源或无发布时间（publishedAt）的内容作为核心证据。\n" +
            "6. evidence中每条都必须包含source、publishedAt、crawledAt（抓取时间）。\n\n" +
            "输出JSON结构：\n" +
            "{\n" +
            "  \"agent\": \"sector_news\",\n" +
            "  \"sector\": \"string\",\n" +
            "  \"summary\": \"string\",\n" +
            "  \"confidence\": number,\n" +
            "  \"sectorChangePercent\": number,\n" +
            "  \"topMovers\": [\n" +
            "    {\n" +
            "      \"symbol\": \"string\",\n" +
            "      \"name\": \"string\",\n" +
            "      \"changePercent\": number,\n" +
            "      \"reason\": \"string\"\n" +
            "    }\n" +
            "  ],\n" +
            "  \"evidence\": [\n" +
            "    {\n" +
            "      \"point\": \"string\",\n" +
            "      \"source\": \"string\",\n" +
            "      \"publishedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
            "      \"crawledAt\": \"YYYY-MM-DD HH:mm\",\n" +
            "      \"url\": \"string|null\"\n" +
            "    }\n" +
            "  ],\n" +
            "  \"signals\": [\"string\"],\n" +
            "  \"triggers\": [\"string\"],\n" +
            "  \"invalidations\": [\"string\"],\n" +
            "  \"riskLimits\": [\"string\"],\n" +
            "  \"risks\": [\"string\"],\n" +
            "  \"chart\": null\n" +
            "}\n\n" +
            "股票上下文JSON：\n";

        return string.Concat(template, contextJson);
    }

    private static string BuildFinancialPrompt(string contextJson)
    {
        const string template =
            "你是个股分析Agent。请联网获取近期财报并重点分析扣非利润、机构持仓、估值。\n" +
            "要求：\n" +
            "1. 必须输出严格JSON，不要Markdown，不要代码块，不要多余文字。\n" +
            "2. 所有字段必须存在；没有数据用null或空数组。\n" +
            "3. 百分比字段用数值，不带%符号。\n\n" +
            "输出JSON结构：\n" +
            "{\n" +
            "  \"agent\": \"financial_analysis\",\n" +
            "  \"summary\": \"string\",\n" +
            "  \"confidence\": number,\n" +
            "  \"metrics\": {\n" +
            "    \"revenue\": number|null,\n" +
            "    \"revenueYoY\": number|null,\n" +
            "    \"netProfit\": number|null,\n" +
            "    \"netProfitYoY\": number|null,\n" +
            "    \"nonRecurringProfit\": number|null,\n" +
            "    \"institutionHoldingPercent\": number|null,\n" +
            "    \"institutionTargetPrice\": number|null\n" +
            "  },\n" +
            "  \"highlights\": [\"string\"],\n" +
            "  \"evidence\": [\n" +
            "    {\n" +
            "      \"point\": \"string\",\n" +
            "      \"source\": \"string\",\n" +
            "      \"publishedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
            "      \"url\": \"string|null\"\n" +
            "    }\n" +
            "  ],\n" +
            "  \"signals\": [\"string\"],\n" +
            "  \"triggers\": [\"string\"],\n" +
            "  \"invalidations\": [\"string\"],\n" +
            "  \"riskLimits\": [\"string\"],\n" +
            "  \"risks\": [\"string\"],\n" +
            "  \"chart\": null\n" +
            "}\n\n" +
            "股票上下文JSON：\n";

        return string.Concat(template, contextJson);
    }

    private static string BuildTrendPrompt(string contextJson)
    {
        const string template =
            "你是股票走势分析Agent。基于日K、分时、成交量数据分析未来走势。\n" +
            "要求：\n" +
            "1. 必须输出严格JSON，不要Markdown，不要代码块，不要多余文字。\n" +
            "2. 所有字段必须存在；没有数据用null或空数组。\n" +
            "3. 百分比字段用数值，不带%符号。\n\n" +
            "输出JSON结构：\n" +
            "{\n" +
            "  \"agent\": \"trend_analysis\",\n" +
            "  \"summary\": \"string\",\n" +
            "  \"confidence\": number,\n" +
            "  \"timeframeSignals\": [\n" +
            "    {\n" +
            "      \"timeframe\": \"1D|1W|1M\",\n" +
            "      \"trend\": \"上涨|震荡|下跌\",\n" +
            "      \"confidence\": number\n" +
            "    }\n" +
            "  ],\n" +
            "  \"forecast\": [\n" +
            "    {\n" +
            "      \"label\": \"T+1\",\n" +
            "      \"price\": number,\n" +
            "      \"confidence\": number\n" +
            "    }\n" +
            "  ],\n" +
            "  \"evidence\": [\n" +
            "    {\n" +
            "      \"point\": \"string\",\n" +
            "      \"source\": \"string\",\n" +
            "      \"publishedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
            "      \"url\": \"string|null\"\n" +
            "    }\n" +
            "  ],\n" +
            "  \"signals\": [\"string\"],\n" +
            "  \"triggers\": [\"string\"],\n" +
            "  \"invalidations\": [\"string\"],\n" +
            "  \"riskLimits\": [\"string\"],\n" +
            "  \"risks\": [\"string\"],\n" +
            "  \"chart\": {\n" +
            "    \"type\": \"line\",\n" +
            "    \"title\": \"未来价格走势\",\n" +
            "    \"labels\": [\"string\"],\n" +
            "    \"values\": [number]\n" +
            "  }\n" +
            "}\n\n" +
            "股票上下文JSON：\n";

        return string.Concat(template, contextJson);
    }

    public static string BuildRepairPrompt(StockAgentKind kind, string raw)
    {
        var schema = GetSchemaTemplate(kind);
        return
            "你刚才的输出不是严格JSON。请只输出一个JSON对象，不要任何解释、Markdown或代码块。\n" +
            "必须严格符合以下JSON结构，字段必须完整，没有数据用null或空数组。\n\n" +
            "JSON结构：\n" + schema + "\n\n" +
            "原始输出：\n" + raw;
    }

    private static string GetSchemaTemplate(StockAgentKind kind)
    {
        return kind switch
        {
            StockAgentKind.Commander =>
                "{\n" +
                "  \"agent\": \"commander\",\n" +
                "  \"summary\": \"string\",\n" +
                "  \"metrics\": {\n" +
                "    \"price\": number,\n" +
                "    \"changePercent\": number,\n" +
                "    \"turnoverRate\": number,\n" +
                "    \"innerVolume\": number|null,\n" +
                "    \"outerVolume\": number|null,\n" +
                "    \"sector\": \"string|null\",\n" +
                "    \"date\": \"YYYY-MM-DD\"\n" +
                "  },\n" +
                "  \"recommendation\": {\n" +
                "    \"action\": \"观察|试仓|加仓|减仓|清仓\",\n" +
                "    \"targetPrice\": number|null,\n" +
                "    \"takeProfitPrice\": number|null,\n" +
                "    \"stopLossPrice\": number|null,\n" +
                "    \"timeHorizon\": \"string|null\",\n" +
                "    \"positionPercent\": number|null,\n" +
                "    \"entryScore\": number,\n" +
                "    \"valuationScore\": number,\n" +
                "    \"confidence\": number,\n" +
                "    \"rating\": \"string\"\n" +
                "  },\n" +
                "  \"reasons\": [\"string\"],\n" +
                "  \"evidence\": [\n" +
                "    {\n" +
                "      \"point\": \"string\",\n" +
                "      \"source\": \"string\",\n" +
                "      \"publishedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
                "      \"url\": \"string|null\"\n" +
                "    }\n" +
                "  ],\n" +
                "  \"triggers\": [\"string\"],\n" +
                "  \"invalidations\": [\"string\"],\n" +
                "  \"riskLimits\": [\"string\"],\n" +
                "  \"signals\": [\"string\"],\n" +
                "  \"risks\": [\"string\"],\n" +
                "  \"chart\": null\n" +
                "}",
            StockAgentKind.StockNews =>
                "{\n" +
                "  \"agent\": \"stock_news\",\n" +
                "  \"summary\": \"string\",\n" +
                "  \"confidence\": number,\n" +
                "  \"sentiment\": {\n" +
                "    \"positive\": number,\n" +
                "    \"neutral\": number,\n" +
                "    \"negative\": number,\n" +
                "    \"overall\": \"string\"\n" +
                "  },\n" +
                "  \"events\": [\n" +
                "    {\n" +
                "      \"title\": \"string\",\n" +
                "      \"category\": \"利好|中性|利空\",\n" +
                "      \"publishedAt\": \"YYYY-MM-DD HH:mm\",\n" +
                "      \"source\": \"string\",\n" +
                "      \"impact\": number,\n" +
                "      \"url\": \"string|null\"\n" +
                "    }\n" +
                "  ],\n" +
                "  \"evidence\": [\n" +
                "    {\n" +
                "      \"point\": \"string\",\n" +
                "      \"source\": \"string\",\n" +
                "      \"publishedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
                "      \"crawledAt\": \"YYYY-MM-DD HH:mm\",\n" +
                "      \"url\": \"string|null\"\n" +
                "    }\n" +
                "  ],\n" +
                "  \"signals\": [\"string\"],\n" +
                "  \"triggers\": [\"string\"],\n" +
                "  \"invalidations\": [\"string\"],\n" +
                "  \"riskLimits\": [\"string\"],\n" +
                "  \"risks\": [\"string\"],\n" +
                "  \"chart\": null\n" +
                "}",
            StockAgentKind.SectorNews =>
                "{\n" +
                "  \"agent\": \"sector_news\",\n" +
                "  \"sector\": \"string\",\n" +
                "  \"summary\": \"string\",\n" +
                "  \"confidence\": number,\n" +
                "  \"sectorChangePercent\": number,\n" +
                "  \"topMovers\": [\n" +
                "    {\n" +
                "      \"symbol\": \"string\",\n" +
                "      \"name\": \"string\",\n" +
                "      \"changePercent\": number,\n" +
                "      \"reason\": \"string\"\n" +
                "    }\n" +
                "  ],\n" +
                "  \"evidence\": [\n" +
                "    {\n" +
                "      \"point\": \"string\",\n" +
                "      \"source\": \"string\",\n" +
                "      \"publishedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
                "      \"crawledAt\": \"YYYY-MM-DD HH:mm\",\n" +
                "      \"url\": \"string|null\"\n" +
                "    }\n" +
                "  ],\n" +
                "  \"signals\": [\"string\"],\n" +
                "  \"triggers\": [\"string\"],\n" +
                "  \"invalidations\": [\"string\"],\n" +
                "  \"riskLimits\": [\"string\"],\n" +
                "  \"risks\": [\"string\"],\n" +
                "  \"chart\": null\n" +
                "}",
            StockAgentKind.FinancialAnalysis =>
                "{\n" +
                "  \"agent\": \"financial_analysis\",\n" +
                "  \"summary\": \"string\",\n" +
                "  \"confidence\": number,\n" +
                "  \"metrics\": {\n" +
                "    \"revenue\": number|null,\n" +
                "    \"revenueYoY\": number|null,\n" +
                "    \"netProfit\": number|null,\n" +
                "    \"netProfitYoY\": number|null,\n" +
                "    \"nonRecurringProfit\": number|null,\n" +
                "    \"institutionHoldingPercent\": number|null,\n" +
                "    \"institutionTargetPrice\": number|null\n" +
                "  },\n" +
                "  \"highlights\": [\"string\"],\n" +
                "  \"evidence\": [\n" +
                "    {\n" +
                "      \"point\": \"string\",\n" +
                "      \"source\": \"string\",\n" +
                "      \"publishedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
                "      \"url\": \"string|null\"\n" +
                "    }\n" +
                "  ],\n" +
                "  \"signals\": [\"string\"],\n" +
                "  \"triggers\": [\"string\"],\n" +
                "  \"invalidations\": [\"string\"],\n" +
                "  \"riskLimits\": [\"string\"],\n" +
                "  \"risks\": [\"string\"],\n" +
                "  \"chart\": null\n" +
                "}",
            StockAgentKind.TrendAnalysis =>
                "{\n" +
                "  \"agent\": \"trend_analysis\",\n" +
                "  \"summary\": \"string\",\n" +
                "  \"confidence\": number,\n" +
                "  \"timeframeSignals\": [\n" +
                "    {\n" +
                "      \"timeframe\": \"1D|1W|1M\",\n" +
                "      \"trend\": \"上涨|震荡|下跌\",\n" +
                "      \"confidence\": number\n" +
                "    }\n" +
                "  ],\n" +
                "  \"forecast\": [\n" +
                "    {\n" +
                "      \"label\": \"T+1\",\n" +
                "      \"price\": number,\n" +
                "      \"confidence\": number\n" +
                "    }\n" +
                "  ],\n" +
                "  \"evidence\": [\n" +
                "    {\n" +
                "      \"point\": \"string\",\n" +
                "      \"source\": \"string\",\n" +
                "      \"publishedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
                "      \"url\": \"string|null\"\n" +
                "    }\n" +
                "  ],\n" +
                "  \"signals\": [\"string\"],\n" +
                "  \"triggers\": [\"string\"],\n" +
                "  \"invalidations\": [\"string\"],\n" +
                "  \"riskLimits\": [\"string\"],\n" +
                "  \"risks\": [\"string\"],\n" +
                "  \"chart\": {\n" +
                "    \"type\": \"line\",\n" +
                "    \"title\": \"未来价格走势\",\n" +
                "    \"labels\": [\"string\"],\n" +
                "    \"values\": [number]\n" +
                "  }\n" +
                "}",
            _ => "{}"
        };
    }
}

internal static class StockAgentJsonParser
{
    public static bool TryParse(string? content, out JsonElement? data, out string? error)
    {
        data = null;
        error = null;

        if (string.IsNullOrWhiteSpace(content))
        {
            error = "LLM 返回为空";
            return false;
        }

        var jsonText = ExtractJson(content);
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            error = "未找到JSON内容";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "JSON根节点不是对象";
                return false;
            }
            data = doc.RootElement.Clone();
            return true;
        }
        catch (Exception ex)
        {
            error = $"JSON解析失败: {ex.Message}";
            return false;
        }
    }

    internal static string? ExtractJson(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            var fenceIndex = trimmed.IndexOf('\n');
            if (fenceIndex >= 0)
            {
                trimmed = trimmed[(fenceIndex + 1)..];
            }
            var endFence = trimmed.LastIndexOf("```", StringComparison.OrdinalIgnoreCase);
            if (endFence >= 0)
            {
                trimmed = trimmed[..endFence];
            }
            trimmed = trimmed.Trim();
        }

        var start = trimmed.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escape = false;
        var end = -1;

        for (var i = start; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escape = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                depth++;
                continue;
            }

            if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    end = i;
                    break;
                }
            }
        }

        if (end < 0 || end <= start)
        {
            return null;
        }

        return trimmed[start..(end + 1)];
    }
}

internal static class StockAgentResultNormalizer
{
    public static JsonElement Normalize(StockAgentKind kind, JsonElement data)
    {
        var root = JsonNode.Parse(data.GetRawText()) as JsonObject ?? new JsonObject();
        var definition = StockAgentCatalog.GetDefinition(kind);

        EnsureString(root, "agent", definition.Id);
        EnsureString(root, "summary", string.Empty);
        EnsureArray(root, "signals");
        EnsureArray(root, "risks");
        EnsureArray(root, "triggers");
        EnsureArray(root, "invalidations");
        EnsureArray(root, "riskLimits");
        EnsureEvidenceArray(root);
        EnsureProperty(root, "chart", null);

        switch (kind)
        {
            case StockAgentKind.Commander:
                NormalizeCommander(root);
                break;
            case StockAgentKind.StockNews:
                NormalizeStockNews(root);
                break;
            case StockAgentKind.SectorNews:
                NormalizeSectorNews(root);
                break;
            case StockAgentKind.FinancialAnalysis:
                NormalizeFinancial(root);
                break;
            case StockAgentKind.TrendAnalysis:
                NormalizeTrend(root);
                break;
        }

        using var doc = JsonDocument.Parse(root.ToJsonString());
        return doc.RootElement.Clone();
    }

    private static void NormalizeCommander(JsonObject root)
    {
        var metrics = EnsureObject(root, "metrics");
        EnsureProperty(metrics, "price", null);
        EnsureProperty(metrics, "changePercent", null);
        EnsureProperty(metrics, "turnoverRate", null);
        EnsureProperty(metrics, "innerVolume", null);
        EnsureProperty(metrics, "outerVolume", null);
        EnsureProperty(metrics, "sector", null);
        EnsureProperty(metrics, "date", null);

        var recommendation = EnsureObject(root, "recommendation");
        EnsureProperty(recommendation, "action", null);
        EnsureProperty(recommendation, "targetPrice", null);
        EnsureProperty(recommendation, "takeProfitPrice", null);
        EnsureProperty(recommendation, "stopLossPrice", null);
        EnsureProperty(recommendation, "timeHorizon", null);
        EnsureProperty(recommendation, "positionPercent", null);
        EnsureProperty(recommendation, "entryScore", null);
        EnsureProperty(recommendation, "valuationScore", null);
        EnsureProperty(recommendation, "confidence", null);
        EnsureProperty(recommendation, "rating", null);

        EnsureArray(root, "reasons");
    }

    private static void NormalizeStockNews(JsonObject root)
    {
        EnsureProperty(root, "confidence", null);
        var sentiment = EnsureObject(root, "sentiment");
        EnsureProperty(sentiment, "positive", null);
        EnsureProperty(sentiment, "neutral", null);
        EnsureProperty(sentiment, "negative", null);
        EnsureProperty(sentiment, "overall", null);

        EnsureArray(root, "events");
        EnforceNewsEvidenceGuardrail(root);
    }

    private static void NormalizeSectorNews(JsonObject root)
    {
        EnsureProperty(root, "sector", null);
        EnsureProperty(root, "confidence", null);
        EnsureProperty(root, "sectorChangePercent", null);
        EnsureArray(root, "topMovers");
        EnforceNewsEvidenceGuardrail(root);
    }

    private static void NormalizeFinancial(JsonObject root)
    {
        EnsureProperty(root, "confidence", null);
        var metrics = EnsureObject(root, "metrics");
        EnsureProperty(metrics, "revenue", null);
        EnsureProperty(metrics, "revenueYoY", null);
        EnsureProperty(metrics, "netProfit", null);
        EnsureProperty(metrics, "netProfitYoY", null);
        EnsureProperty(metrics, "nonRecurringProfit", null);
        EnsureProperty(metrics, "institutionHoldingPercent", null);
        EnsureProperty(metrics, "institutionTargetPrice", null);

        EnsureArray(root, "highlights");
    }

    private static void NormalizeTrend(JsonObject root)
    {
        EnsureProperty(root, "confidence", null);
        EnsureArray(root, "timeframeSignals");
        EnsureArray(root, "forecast");
    }

    private static void EnsureEvidenceArray(JsonObject root)
    {
        var evidence = EnsureArray(root, "evidence");
        for (var i = 0; i < evidence.Count; i++)
        {
            if (evidence[i] is not JsonObject item)
            {
                item = new JsonObject();
                evidence[i] = item;
            }

            EnsureProperty(item, "point", null);
            EnsureProperty(item, "source", null);
            EnsureProperty(item, "publishedAt", null);
            EnsureProperty(item, "crawledAt", null);
            EnsureProperty(item, "url", null);
        }
    }

    private static void EnforceNewsEvidenceGuardrail(JsonObject root)
    {
        if (HasUsableEvidence(root))
        {
            return;
        }

        root["summary"] = "信息不足：缺少可验证来源或发布时间，建议观望。";
        root["confidence"] = 20;
        root["signals"] = new JsonArray("观望，等待高质量新证据");
        root["triggers"] = new JsonArray("补充最近72小时且来源可追溯的证据>=3条");
        root["invalidations"] = new JsonArray("当前证据缺少来源或发布时间");
        root["riskLimits"] = new JsonArray("证据缺失时不依据该结论执行买卖");

        if (root["sentiment"] is JsonObject sentiment)
        {
            sentiment["positive"] = 0;
            sentiment["neutral"] = 0;
            sentiment["negative"] = 0;
            sentiment["overall"] = "中性";
        }
    }

    private static bool HasUsableEvidence(JsonObject root)
    {
        if (root["evidence"] is not JsonArray evidence || evidence.Count == 0)
        {
            return false;
        }

        foreach (var node in evidence)
        {
            if (node is not JsonObject item)
            {
                continue;
            }

            if (!TryReadString(item, "source", out var source) || string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            if (!TryReadString(item, "publishedAt", out var publishedText) || string.IsNullOrWhiteSpace(publishedText))
            {
                continue;
            }

            if (DateTime.TryParse(publishedText, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadString(JsonObject item, string key, out string? value)
    {
        value = null;
        if (item[key] is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var text))
        {
            return false;
        }

        value = text;
        return true;
    }

    private static JsonObject EnsureObject(JsonObject root, string key)
    {
        if (root[key] is JsonObject obj)
        {
            return obj;
        }

        var created = new JsonObject();
        root[key] = created;
        return created;
    }

    private static JsonArray EnsureArray(JsonObject root, string key)
    {
        if (root[key] is JsonArray arr)
        {
            return arr;
        }

        var created = new JsonArray();
        root[key] = created;
        return created;
    }

    private static void EnsureString(JsonObject root, string key, string defaultValue)
    {
        if (root[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        root[key] = defaultValue;
    }

    private static void EnsureProperty(JsonObject root, string key, object? defaultValue)
    {
        if (root.ContainsKey(key))
        {
            return;
        }

        root[key] = defaultValue is null ? null : JsonValue.Create(defaultValue);
    }
}
