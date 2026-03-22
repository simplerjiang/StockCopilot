using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using SimplerJiangAiAgent.Api.Data.Entities;
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
    private readonly IStockAgentHistoryService _agentHistoryService;
    private readonly IQueryLocalFactDatabaseTool _queryLocalFactDatabaseTool;
    private readonly IStockAgentFeatureEngineeringService _featureEngineeringService;

    public StockAgentOrchestrator(
        IStockDataService dataService,
        ILlmService llmService,
        IFileLogWriter fileLogWriter,
        IStockAgentHistoryService agentHistoryService,
        IQueryLocalFactDatabaseTool queryLocalFactDatabaseTool,
        IStockAgentFeatureEngineeringService featureEngineeringService)
    {
        _dataService = dataService;
        _llmService = llmService;
        _fileLogWriter = fileLogWriter;
        _agentHistoryService = agentHistoryService;
        _queryLocalFactDatabaseTool = queryLocalFactDatabaseTool;
        _featureEngineeringService = featureEngineeringService;
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

        var context = await BuildContextAsync(symbol, interval, count, request.Source, request.UseInternet, cancellationToken);
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

        var commanderHistory = await BuildCommanderHistoryAsync(symbol, cancellationToken);
        var commanderContextJson = SerializeCommanderContext(context, commanderHistory);
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

        var context = await BuildContextAsync(symbol, interval, count, request.Source, request.UseInternet, cancellationToken);
        var contextJson = kind == StockAgentKind.Commander
            ? SerializeCommanderContext(context, await BuildCommanderHistoryAsync(symbol, cancellationToken))
            : SerializeContext(kind, context);
        var dependencies = request.DependencyResults ?? Array.Empty<StockAgentResultDto>();

        return await RunAgentAsync(kind, contextJson, new StockAgentRequestDto(
            symbol,
            request.Source,
            request.Provider,
            request.Model,
            request.Interval,
            request.Count,
            request.UseInternet,
            request.IsPro), dependencies, cancellationToken);
    }

    private async Task<StockAgentContextDto> BuildContextAsync(
        string symbol,
        string interval,
        int count,
        string? source,
        bool requestedUseInternet,
        CancellationToken cancellationToken)
    {
        var quote = await _dataService.GetQuoteAsync(symbol, source);
        var kLines = await _dataService.GetKLineAsync(symbol, interval, count, source);
        var minuteLines = await _dataService.GetMinuteLineAsync(symbol, source);
        var messages = await _dataService.GetIntradayMessagesAsync(symbol, source);
        var newsContext = StockAgentNewsContextPolicy.Apply(messages, DateTime.Now);
        var projectedLocalFacts = StockAgentLocalFactProjection.Create(await _queryLocalFactDatabaseTool.QueryAsync(symbol, cancellationToken));
        var queryPolicy = StockAgentInternetRoutingPolicy.Build(symbol, requestedUseInternet);
        var prepared = _featureEngineeringService.Prepare(symbol, quote, kLines, minuteLines, newsContext.Messages, newsContext.Policy, projectedLocalFacts, DateTime.Now);

        return new StockAgentContextDto(
            quote,
            kLines.OrderBy(item => item.Date).TakeLast(60).ToArray(),
            minuteLines.OrderBy(item => item.Date).ThenBy(item => item.Time).TakeLast(120).ToArray(),
            newsContext.Messages,
            newsContext.Policy,
            prepared.LocalFacts,
            prepared.Features,
            queryPolicy,
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
        var provider = string.IsNullOrWhiteSpace(request.Provider) ? "active" : request.Provider.Trim();
        var allowInternet = StockAgentInternetRoutingPolicy.ResolveUseInternet(request.Symbol, kind, request.UseInternet);
        var model = StockAgentModelRoutingPolicy.ResolveModel(request.Model, request.IsPro);

        try
        {
            var result = await _llmService.ChatAsync(
                provider,
                new LlmChatRequest(prompt, model, 0.4, allowInternet),
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
                        new LlmChatRequest(repairPrompt, model, 0.2, false),
                        cancellationToken);

                    var repairRaw = repair.Content?.Trim() ?? string.Empty;
                    if (StockAgentJsonParser.TryParse(repairRaw, out var repairData, out _))
                    {
                        var normalizedRepairData = StockAgentResultNormalizer.Normalize(kind, repairData!.Value);
                        if (kind == StockAgentKind.Commander)
                        {
                            normalizedRepairData = StockAgentCommanderConsistencyGuardrails.Apply(normalizedRepairData, dependencyResults, contextJson);
                        }
                        return new StockAgentResultDto(definition.Id, definition.Name, true, null, normalizedRepairData, repairRaw, repair.TraceId);
                    }

                    _fileLogWriter.Write("LLM", $"parse_error agent={definition.Id} stage=repair attempt={attempt} raw={repairRaw}");
                    currentRaw = repairRaw;
                }

                return new StockAgentResultDto(definition.Id, definition.Name, false, parseError, null, currentRaw, result.TraceId);
            }

            var normalizedData = StockAgentResultNormalizer.Normalize(kind, data!.Value);
            if (kind == StockAgentKind.Commander)
            {
                normalizedData = StockAgentCommanderConsistencyGuardrails.Apply(normalizedData, dependencyResults, contextJson);
            }
            return new StockAgentResultDto(definition.Id, definition.Name, true, null, normalizedData, raw, result.TraceId);
        }
        catch (Exception ex)
        {
            return new StockAgentResultDto(definition.Id, definition.Name, false, ex.Message, null, null);
        }
    }

    private async Task<StockAgentCommanderHistoryPackageDto> BuildCommanderHistoryAsync(
        string symbol,
        CancellationToken cancellationToken)
    {
        var list = await _agentHistoryService.GetListAsync(symbol, cancellationToken);
        return StockAgentCommanderHistoryPolicy.Build(list, DateTime.Now);
    }

    private sealed record StockAgentContextDto(
        StockQuoteDto Quote,
        IReadOnlyList<KLinePointDto> KLines,
        IReadOnlyList<MinuteLinePointDto> MinuteLines,
        IReadOnlyList<IntradayMessageDto> Messages,
        StockAgentNewsPolicyDto NewsPolicy,
        StockAgentLocalFactPackageDto LocalFacts,
        StockAgentDeterministicFeaturesDto DeterministicFeatures,
        StockAgentQueryPolicyDto QueryPolicy,
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
            context.LocalFacts,
            context.DeterministicFeatures,
            context.QueryPolicy,
            context.RequestTime);

        return JsonSerializer.Serialize(slimContext, JsonOptions);
    }

    private static string SerializeCommanderContext(StockAgentContextDto context, StockAgentCommanderHistoryPackageDto commanderHistory)
    {
        var commanderContext = new StockAgentCommanderContextDto(
            context.Quote,
            context.Messages,
            context.NewsPolicy,
            context.LocalFacts,
            context.DeterministicFeatures,
            context.QueryPolicy,
            commanderHistory,
            context.KLines.TakeLast(30).ToArray(),
            context.RequestTime);

        return JsonSerializer.Serialize(commanderContext, JsonOptions);
    }

    private sealed record StockAgentSlimContextDto(
        StockQuoteDto Quote,
        IReadOnlyList<IntradayMessageDto> Messages,
        StockAgentNewsPolicyDto NewsPolicy,
        StockAgentLocalFactPackageDto LocalFacts,
        StockAgentDeterministicFeaturesDto DeterministicFeatures,
        StockAgentQueryPolicyDto QueryPolicy,
        DateTime RequestTime
    );

    private sealed record StockAgentCommanderContextDto(
        StockQuoteDto Quote,
        IReadOnlyList<IntradayMessageDto> Messages,
        StockAgentNewsPolicyDto NewsPolicy,
        StockAgentLocalFactPackageDto LocalFacts,
        StockAgentDeterministicFeaturesDto DeterministicFeatures,
        StockAgentQueryPolicyDto QueryPolicy,
        StockAgentCommanderHistoryPackageDto CommanderHistory,
        IReadOnlyList<KLinePointDto> KLines,
        DateTime RequestTime
    );
}

internal static class StockAgentModelRoutingPolicy
{
    internal const string DefaultModel = "gemini-3.1-flash-lite-preview-thinking-high";
    internal const string ProModel = "gemini-3.1-pro-preview-thinking-medium";

    public static string ResolveModel(string? requestedModel, bool isPro)
    {
        if (isPro)
        {
            return ProModel;
        }

        var normalized = requestedModel?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized) && !string.Equals(normalized, ProModel, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return DefaultModel;
    }
}

public sealed record StockAgentNewsPolicyDto(
    int PreferredLookbackHours,
    int ActualLookbackHours,
    bool ExpandedWindow,
    int CandidateCount,
    int SelectedCount
);

internal sealed record StockAgentCommanderHistoryItemDto(
    DateTime CreatedAt,
    string Direction,
    decimal? Confidence,
    IReadOnlyList<string> Triggers,
    IReadOnlyList<string> Invalidations,
    IReadOnlyList<string> RiskLimits,
    IReadOnlyList<string> EvidenceSummary,
    string? FinalResultTag,
    string? Summary
);

internal sealed record StockAgentCommanderHistoryPackageDto(
    int LookbackDays,
    int MaxItems,
    int IncludedCount,
    IReadOnlyList<StockAgentCommanderHistoryItemDto> Items
);

internal static class StockAgentCommanderHistoryPolicy
{
    private const int DefaultLookbackDays = 5;
    private const int MinLookbackDays = 3;
    private const int MaxLookbackDays = 7;
    private const int DefaultMaxItems = 8;

    public static StockAgentCommanderHistoryPackageDto Build(
        IReadOnlyList<StockAgentAnalysisHistory> list,
        DateTime requestTime,
        int lookbackDays = DefaultLookbackDays,
        int maxItems = DefaultMaxItems)
    {
        var safeLookbackDays = Math.Clamp(lookbackDays, MinLookbackDays, MaxLookbackDays);
        var safeMaxItems = Math.Clamp(maxItems, 5, 10);
        var cutoff = requestTime.Date.AddDays(-safeLookbackDays);

        var items = list
            .Where(item => item.CreatedAt >= cutoff)
            .OrderByDescending(item => item.CreatedAt)
            .Take(safeMaxItems)
            .Select(ParseCommanderHistoryItem)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();

        return new StockAgentCommanderHistoryPackageDto(
            safeLookbackDays,
            safeMaxItems,
            items.Length,
            items);
    }

    private static StockAgentCommanderHistoryItemDto? ParseCommanderHistoryItem(StockAgentAnalysisHistory entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ResultJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(entry.ResultJson);
            if (!TryGetCommanderAgentData(doc.RootElement, out var commanderData))
            {
                return null;
            }

            var direction = GetDirection(commanderData);
            var confidence = TryGetNumber(commanderData, "confidence_score");
            var triggers = ReadCommanderConditionList(commanderData, "trigger_conditions", "triggers");
            var invalidations = ReadCommanderConditionList(commanderData, "invalid_conditions", "invalidations");
            var riskLimits = ReadCommanderConditionList(commanderData, "risk_warning", "riskLimits");
            var evidence = ReadEvidencePoints(commanderData);
            var summary = TryGetString(commanderData, "analysis_opinion") ?? TryGetString(commanderData, "summary");

            return new StockAgentCommanderHistoryItemDto(
                entry.CreatedAt,
                direction,
                confidence,
                triggers,
                invalidations,
                riskLimits,
                evidence,
                direction,
                summary);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetCommanderAgentData(JsonElement root, out JsonElement commanderData)
    {
        commanderData = default;

        if (!TryGetPropertyIgnoreCase(root, "agents", out var agents) || agents.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var agent in agents.EnumerateArray())
        {
            if (!TryGetPropertyIgnoreCase(agent, "agentId", out var agentIdNode))
            {
                continue;
            }

            var agentId = agentIdNode.GetString();
            if (!string.Equals(agentId, "commander", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryGetPropertyIgnoreCase(agent, "data", out var dataNode) && dataNode.ValueKind == JsonValueKind.Object)
            {
                commanderData = dataNode;
                return true;
            }
        }

        return false;
    }

    private static string GetDirection(JsonElement commanderData)
    {
        var explicitBias = TryGetString(commanderData, "directional_bias");
        if (!string.IsNullOrWhiteSpace(explicitBias))
        {
            return explicitBias;
        }

        var opinion = TryGetString(commanderData, "analysis_opinion") ?? TryGetString(commanderData, "summary");
        if (string.IsNullOrWhiteSpace(opinion))
        {
            return "未知";
        }

        if (ContainsAny(opinion, "清仓", "减仓", "看空", "回避", "转弱", "下行", "破位"))
        {
            return "减仓";
        }

        if (ContainsAny(opinion, "加仓", "试仓", "看多", "偏多", "突破", "转强", "上行"))
        {
            return "加仓";
        }

        if (ContainsAny(opinion, "观察", "观望", "等待", "震荡", "中性"))
        {
            return "观察";
        }

        return "未知";
    }

    private static IReadOnlyList<string> ReadEvidencePoints(JsonElement commanderData)
    {
        if (!TryGetPropertyIgnoreCase(commanderData, "evidence", out var evidence) || evidence.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var list = new List<string>();
        foreach (var item in evidence.EnumerateArray())
        {
            var point = TryGetString(item, "point")
                ?? TryGetString(item, "title")
                ?? TryGetString(item, "excerpt")
                ?? TryGetString(item, "summary");
            if (string.IsNullOrWhiteSpace(point))
            {
                continue;
            }

            list.Add(point);
            if (list.Count >= 3)
            {
                break;
            }
        }

        return list;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string property)
    {
        if (!TryGetPropertyIgnoreCase(root, property, out var arrayNode) || arrayNode.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return arrayNode
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .Take(5)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadCommanderConditionList(JsonElement root, string singularProperty, string fallbackArrayProperty)
    {
        var single = TryGetString(root, singularProperty);
        if (!string.IsNullOrWhiteSpace(single))
        {
            return new[] { single };
        }

        return ReadStringArray(root, fallbackArrayProperty);
    }

    private static decimal? TryGetNumber(JsonElement root, string property)
    {
        if (!TryGetPropertyIgnoreCase(root, property, out var node) || node.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return node.TryGetDecimal(out var value) ? value : null;
    }

    private static decimal? TryGetNumber(JsonElement root, string parentProperty, string property)
    {
        if (!TryGetPropertyIgnoreCase(root, parentProperty, out var parent) || parent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!TryGetPropertyIgnoreCase(parent, property, out var node) || node.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return node.TryGetDecimal(out var value) ? value : null;
    }

    private static string? TryGetString(JsonElement root, params string[] properties)
    {
        var current = root;
        foreach (var property in properties)
        {
            if (!TryGetPropertyIgnoreCase(current, property, out var next))
            {
                return null;
            }

            current = next;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string propertyName, out JsonElement value)
    {
        value = default;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        return keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}

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
            "5. 所有建议必须给出证据来源、触发条件、失效条件和风险上限。\n" +
            "6. 你会收到仅供指挥Agent使用的近3-7天历史结论（默认5天）。若本次方向/评级与最近一次明显变化，必须给出改判原因。\n\n" +
            "7. 必须执行多周期融合：综合1D/1W/1M信号；若短中周期冲突，consistency.status 必须为“分歧态”。\n" +
            "8. 必须执行状态机与滞后机制：状态=延续/震荡/反转；单日波动不应轻易翻转方向，除非出现强反证（如关键失效条件触发）。\n" +
            "9. 必须把流通市值、市盈率、量比、股东户数、所属板块纳入推理和结论。\n" +
            "10. evidence 中每条必须显式标注 readMode/readStatus；优先使用 full_text_read 或 summary_only 证据，metadata_only / unverified / fetch_failed 只能作为弱证据。\n\n" +
            "11. deterministicFeatures 是代码先算好的硬特征，必须优先引用，不要自行脑补数值。coverageScore/conflictScore/degradedFlags 是系统惩罚输入。\n" +
            "12. 只有 commander 可以输出方向、概率分布、触发条件、失效条件、仓位倾向；不得引入上游未引用的新证据。\n\n" +
            "输出JSON结构：\n" +
            "{\n" +
            "  \"agent\": \"commander\",\n" +
            "  \"summary\": \"string\",\n" +
            "  \"metrics\": {\n" +
            "    \"price\": number,\n" +
            "    \"changePercent\": number,\n" +
            "    \"turnoverRate\": number,\n" +
            "    \"peRatio\": number|null,\n" +
            "    \"floatMarketCap\": number|null,\n" +
            "    \"volumeRatio\": number|null,\n" +
            "    \"shareholderCount\": number|null,\n" +
            "    \"sector\": \"string|null\",\n" +
            "    \"date\": \"YYYY-MM-DD\"\n" +
            "  },\n" +
            "  \"directional_bias\": \"看多|观察|看空\",\n" +
            "  \"probabilities\": { \"bull\": number, \"base\": number, \"bear\": number },\n" +
            "  \"analysis_opinion\": \"深度的逻辑推理与走势判断\",\n" +
            "  \"confidence_score\": number,\n" +
            "  \"trigger_conditions\": \"明确写出什么价格/指标发生意味着看多/看空信号触发\",\n" +
            "  \"invalid_conditions\": \"什么事件发生意味着该逻辑失效\",\n" +
            "  \"risk_warning\": \"明确指出潜在风险点上限控制\",\n" +
            "  \"evidence\": [\n" +
            "    {\n" +
            "      \"point\": \"string\",\n" +
            "      \"title\": \"string|null\",\n" +
            "      \"source\": \"string\",\n" +
            "      \"publishedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
            "      \"crawledAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
            "      \"url\": \"string|null\",\n" +
            "      \"excerpt\": \"string|null\",\n" +
            "      \"readMode\": \"url_fetched|local_fact|url_unavailable|string\",\n" +
            "      \"readStatus\": \"full_text_read|summary_only|title_only|metadata_only|unverified|fetch_failed|string\",\n" +
            "      \"ingestedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
            "      \"localFactId\": number|null,\n" +
            "      \"sourceRecordId\": \"string|null\"\n" +
            "    }\n" +
            "  ],\n" +
            "  \"revision\": {\n" +
            "    \"required\": boolean,\n" +
            "    \"reason\": \"string|null\",\n" +
            "    \"previousDirection\": \"string|null\"\n" +
            "  },\n" +
            "  \"consistency\": {\n" +
            "    \"shortTermTrend\": \"上涨|震荡|下跌|null\",\n" +
            "    \"midTermTrend\": \"上涨|震荡|下跌|null\",\n" +
            "    \"status\": \"一致|分歧态\",\n" +
            "    \"note\": \"string|null\"\n" +
            "  },\n" +
            "  \"marketState\": {\n" +
            "    \"state\": \"延续|震荡|反转\",\n" +
            "    \"hysteresisApplied\": boolean,\n" +
            "    \"strongCounterEvidence\": boolean,\n" +
            "    \"overrideReason\": \"string|null\"\n" +
            "  },\n" +
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
            "你是个股资讯Agent。请优先使用上下文里的 localFacts.stockNews / localFacts.marketReports 本地事实库，处理当前及近期该股票的重要消息并做情绪统计。\n" +
            "要求：\n" +
            "1. 必须输出严格JSON，不要Markdown，不要代码块，不要多余文字。\n" +
            "2. 所有字段必须存在；没有数据用null或空数组。\n" +
            "3. 百分比字段用数值，不带%符号。\n" +
            "3.1 queryPolicy.allowInternet=false 时，禁止跳过本地事实库自行联网搜索 A 股公告/研报。\n" +
            "4. 证据默认只允许最近72小时；若有效证据不足可扩窗到7天，并在summary中明确标注“扩窗到7天”。\n" +
            "5. 禁止将无来源或无发布时间（publishedAt）的内容作为核心证据。\n" +
            "6. evidence中每条都必须包含source、publishedAt、crawledAt（抓取时间）、title、readMode、readStatus；如来自本地事实库，优先附带localFactId/sourceRecordId。\n\n" +
            "7. 你只负责个股事件事实、催化、情绪方向、证据覆盖率，不负责仓位建议、最终方向、触发条件、失效条件。triggers/invalidations/riskLimits 默认留空数组。\n" +
            "8. 必须优先引用 deterministicFeatures.evidence，不要伪造概率或价格路径。\n\n" +
            "输出JSON结构：\n" +
            "{\n" +
            "  \"agent\": \"stock_news\",\n" +
            "  \"summary\": \"string\",\n" +
            "  \"confidence\": number,\n" +
            "  \"eventBias\": \"利好|中性|利空\",\n" +
            "  \"coverage\": { \"highQualityCount\": number, \"recentCount\": number, \"note\": \"string|null\" },\n" +
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
            "      \"title\": \"string|null\",\n" +
            "      \"source\": \"string\",\n" +
            "      \"publishedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
            "      \"crawledAt\": \"YYYY-MM-DD HH:mm\",\n" +
            "      \"url\": \"string|null\",\n" +
            "      \"excerpt\": \"string|null\",\n" +
            "      \"readMode\": \"string\",\n" +
            "      \"readStatus\": \"string\",\n" +
            "      \"ingestedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
            "      \"localFactId\": number|null,\n" +
            "      \"sourceRecordId\": \"string|null\"\n" +
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
            "你是板块资讯Agent。请优先使用上下文里的 localFacts.sectorReports / localFacts.marketReports，本地分析该股票所属板块的最新资讯和市场环境。\n" +
            "要求：\n" +
            "1. 必须输出严格JSON，不要Markdown，不要代码块，不要多余文字。\n" +
            "2. 所有字段必须存在；没有数据用null或空数组。\n" +
            "3. 百分比字段用数值，不带%符号。\n" +
            "3.1 queryPolicy.allowInternet=false 时，只能基于本地事实库输出，不得擅自联网补齐 A 股板块消息。\n" +
            "4. 证据默认只允许最近72小时；若有效证据不足可扩窗到7天，并在summary中明确标注“扩窗到7天”。\n" +
            "5. 禁止将无来源或无发布时间（publishedAt）的内容作为核心证据。\n" +
            "6. evidence中每条都必须包含source、publishedAt、crawledAt（抓取时间）、title、readMode、readStatus；如来自本地事实库，优先附带localFactId/sourceRecordId。\n\n" +
            "7. 你只负责板块强弱、同类股联动、政策与资金环境，不负责最终方向、仓位、触发条件或失效条件。triggers/invalidations/riskLimits 默认留空数组。\n" +
            "8. deterministicFeatures 已经提供市场噪音过滤后的 coverage/conflict/degradedFlags，必须沿用。\n\n" +
            "输出JSON结构：\n" +
            "{\n" +
            "  \"agent\": \"sector_news\",\n" +
            "  \"sector\": \"string\",\n" +
            "  \"summary\": \"string\",\n" +
            "  \"confidence\": number,\n" +
            "  \"regime\": \"偏强|中性|偏弱\",\n" +
            "  \"linkageNote\": \"string|null\",\n" +
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
            "      \"title\": \"string|null\",\n" +
            "      \"source\": \"string\",\n" +
            "      \"publishedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
            "      \"crawledAt\": \"YYYY-MM-DD HH:mm\",\n" +
            "      \"url\": \"string|null\",\n" +
            "      \"excerpt\": \"string|null\",\n" +
            "      \"readMode\": \"string\",\n" +
            "      \"readStatus\": \"string\",\n" +
            "      \"ingestedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
            "      \"localFactId\": number|null,\n" +
            "      \"sourceRecordId\": \"string|null\"\n" +
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
            "你是个股分析Agent。请优先使用上下文里的 localFacts.stockNews / localFacts.sectorReports / localFacts.fundamentalFacts 进行 A 股个股事实分析；仅当 queryPolicy.allowInternet=true 时才允许补充海外/宏观信息。\n" +
            "如果 localFacts.fundamentalFacts 已提供营收、净利润、扣非利润、股东户数、机构目标价等事实，优先直接采用，不要无故留空。\n" +
            "要求：\n" +
            "1. 必须输出严格JSON，不要Markdown，不要代码块，不要多余文字。\n" +
            "2. 所有字段必须存在；没有数据用null或空数组。\n" +
            "3. 百分比字段用数值，不带%符号。\n" +
            "4. 你只负责财务质量、估值、预期差和慢变量，不负责最终方向、触发条件、失效条件、仓位建议。triggers/invalidations/riskLimits 默认留空数组。\n\n" +
            "输出JSON结构：\n" +
            "{\n" +
            "  \"agent\": \"financial_analysis\",\n" +
            "  \"summary\": \"string\",\n" +
            "  \"confidence\": number,\n" +
            "  \"qualityView\": \"改善|平稳|承压\",\n" +
            "  \"valuationView\": \"低估|合理|偏贵|未知\",\n" +
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
            "      \"title\": \"string|null\",\n" +
            "      \"source\": \"string\",\n" +
            "      \"publishedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
            "      \"crawledAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
            "      \"url\": \"string|null\",\n" +
            "      \"excerpt\": \"string|null\",\n" +
            "      \"readMode\": \"string\",\n" +
            "      \"readStatus\": \"string\",\n" +
            "      \"ingestedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
            "      \"localFactId\": number|null,\n" +
            "      \"sourceRecordId\": \"string|null\"\n" +
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
            "3. 百分比字段用数值，不带%符号。\n" +
            "4. 你只负责趋势状态、关键位、波动率、量价结构；不要输出最终仓位建议或新闻结论。triggers/invalidations/riskLimits 默认留空数组。\n" +
            "5. deterministicFeatures 已经提供 MA/ATR/VWAP/coverage/degradedFlags，必须优先参考。\n\n" +
            "输出JSON结构：\n" +
            "{\n" +
            "  \"agent\": \"trend_analysis\",\n" +
            "  \"summary\": \"string\",\n" +
            "  \"confidence\": number,\n" +
            "  \"trendState\": \"上涨|盘整|震荡|下跌\",\n" +
            "  \"keyLevels\": { \"support\": number|null, \"resistance\": number|null, \"vwap\": number|null },\n" +
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
            "      \"title\": \"string|null\",\n" +
            "      \"source\": \"string\",\n" +
            "      \"publishedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
            "      \"crawledAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
            "      \"url\": \"string|null\",\n" +
            "      \"excerpt\": \"string|null\",\n" +
            "      \"readMode\": \"string\",\n" +
            "      \"readStatus\": \"string\",\n" +
            "      \"ingestedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
            "      \"localFactId\": number|null,\n" +
            "      \"sourceRecordId\": \"string|null\"\n" +
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
                "    \"peRatio\": number|null,\n" +
                "    \"floatMarketCap\": number|null,\n" +
                "    \"volumeRatio\": number|null,\n" +
                "    \"shareholderCount\": number|null,\n" +
                "    \"sector\": \"string|null\",\n" +
                "    \"date\": \"YYYY-MM-DD\"\n" +
                "  },\n" +
                "  \"directional_bias\": \"看多|观察|看空\",\n" +
                "  \"probabilities\": { \"bull\": number, \"base\": number, \"bear\": number },\n" +
                "  \"analysis_opinion\": \"string\",\n" +
                "  \"confidence_score\": number,\n" +
                "  \"trigger_conditions\": \"string\",\n" +
                "  \"invalid_conditions\": \"string\",\n" +
                "  \"risk_warning\": \"string\",\n" +
                "  \"evidence\": [\n" +
                "    {\n" +
                "      \"point\": \"string\",\n" +
                "      \"title\": \"string|null\",\n" +
                "      \"source\": \"string\",\n" +
                "      \"publishedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
                "      \"crawledAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
                "      \"url\": \"string|null\",\n" +
                "      \"excerpt\": \"string|null\",\n" +
                "      \"readMode\": \"string\",\n" +
                "      \"readStatus\": \"string\",\n" +
                "      \"ingestedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
                "      \"localFactId\": number|null,\n" +
                "      \"sourceRecordId\": \"string|null\"\n" +
                "    }\n" +
                "  ],\n" +
                "  \"revision\": {\n" +
                "    \"required\": boolean,\n" +
                "    \"reason\": \"string|null\",\n" +
                "    \"previousDirection\": \"string|null\"\n" +
                "  },\n" +
                "  \"consistency\": {\n" +
                "    \"shortTermTrend\": \"上涨|震荡|下跌|null\",\n" +
                "    \"midTermTrend\": \"上涨|震荡|下跌|null\",\n" +
                "    \"status\": \"一致|分歧态\",\n" +
                "    \"note\": \"string|null\"\n" +
                "  },\n" +
                "  \"marketState\": {\n" +
                "    \"state\": \"延续|震荡|反转\",\n" +
                "    \"hysteresisApplied\": boolean,\n" +
                "    \"strongCounterEvidence\": boolean,\n" +
                "    \"overrideReason\": \"string|null\"\n" +
                "  },\n" +
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
                "      \"title\": \"string|null\",\n" +
                "      \"source\": \"string\",\n" +
                "      \"publishedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
                "      \"crawledAt\": \"YYYY-MM-DD HH:mm\",\n" +
                "      \"url\": \"string|null\",\n" +
                "      \"excerpt\": \"string|null\",\n" +
                "      \"readMode\": \"string\",\n" +
                "      \"readStatus\": \"string\",\n" +
                "      \"ingestedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
                "      \"localFactId\": number|null,\n" +
                "      \"sourceRecordId\": \"string|null\"\n" +
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
                "      \"title\": \"string|null\",\n" +
                "      \"source\": \"string\",\n" +
                "      \"publishedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
                "      \"crawledAt\": \"YYYY-MM-DD HH:mm\",\n" +
                "      \"url\": \"string|null\",\n" +
                "      \"excerpt\": \"string|null\",\n" +
                "      \"readMode\": \"string\",\n" +
                "      \"readStatus\": \"string\",\n" +
                "      \"ingestedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
                "      \"localFactId\": number|null,\n" +
                "      \"sourceRecordId\": \"string|null\"\n" +
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
                "      \"title\": \"string|null\",\n" +
                "      \"source\": \"string\",\n" +
                "      \"publishedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
                "      \"crawledAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
                "      \"url\": \"string|null\",\n" +
                "      \"excerpt\": \"string|null\",\n" +
                "      \"readMode\": \"string\",\n" +
                "      \"readStatus\": \"string\",\n" +
                "      \"ingestedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
                "      \"localFactId\": number|null,\n" +
                "      \"sourceRecordId\": \"string|null\"\n" +
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
                "      \"title\": \"string|null\",\n" +
                "      \"source\": \"string\",\n" +
                "      \"publishedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
                "      \"crawledAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
                "      \"url\": \"string|null\",\n" +
                "      \"excerpt\": \"string|null\",\n" +
                "      \"readMode\": \"string\",\n" +
                "      \"readStatus\": \"string\",\n" +
                "      \"ingestedAt\": \"YYYY-MM-DD HH:mm|null\",\n" +
                "      \"localFactId\": number|null,\n" +
                "      \"sourceRecordId\": \"string|null\"\n" +
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

        var trimmed = content.TrimStart();
        if (trimmed.StartsWith("<", StringComparison.Ordinal))
        {
            error = "LLM 返回了 HTML/网关错误页，已降级处理";
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

internal static class StockAgentCommanderConsistencyGuardrails
{
    public static JsonElement Apply(JsonElement commanderData, IReadOnlyList<StockAgentResultDto> dependencyResults, string contextJson)
    {
        var root = JsonNode.Parse(commanderData.GetRawText()) as JsonObject ?? new JsonObject();
        var revision = EnsureObject(root, "revision");
        var consistency = EnsureObject(root, "consistency");
        var marketState = EnsureObject(root, "marketState");
        var guardrails = EnsureObject(root, "guardrails");

        var history = ParseCommanderHistory(contextJson);
        var previousDirection = history.FirstOrDefault()?.Direction;
        var currentDirection = GetCurrentDirection(root);
        var confidence = TryReadDecimal(root, "confidence_score") ?? 50m;
        var contextFeatures = ParseContextFeatures(contextJson);
        var dependencyFailures = dependencyResults.Count(item => !item.Success);

        var (shortTrend, midTrend, divergence) = EvaluateTimeframeConsistency(dependencyResults);
        consistency["shortTermTrend"] = shortTrend;
        consistency["midTermTrend"] = midTrend;
        consistency["status"] = divergence ? "分歧态" : "一致";
        consistency["note"] = divergence ? "短中周期信号冲突，需等待确认" : null;

        if (divergence)
        {
            AppendUniqueSignal(root, "分歧态：短中周期冲突，避免单边重仓");
        }

        var (state, reversed) = EvaluateMarketState(contextJson);
        marketState["state"] = state;

        var strongCounterEvidence = HasStrongCounterEvidence(root);
        marketState["strongCounterEvidence"] = strongCounterEvidence;

        var changed = !string.IsNullOrWhiteSpace(previousDirection)
            && !string.Equals(previousDirection, currentDirection, StringComparison.OrdinalIgnoreCase);

        var hysteresisApplied = false;
        if (changed && confidence < 65m && !strongCounterEvidence)
        {
            currentDirection = previousDirection;
            hysteresisApplied = true;
            root["analysis_opinion"] = BuildHysteresisOpinion(previousDirection, TryReadString(root, "analysis_opinion"));
            marketState["overrideReason"] = "触发滞后机制：低置信度变更被抑制";
            AppendUniqueSignal(root, "滞后机制生效：方向暂不翻转");
        }
        else if (changed && strongCounterEvidence)
        {
            marketState["overrideReason"] = "强反证触发，允许方向变更";
        }

        marketState["hysteresisApplied"] = hysteresisApplied;
        if (reversed && string.Equals(state, "反转", StringComparison.Ordinal))
        {
            AppendUniqueSignal(root, "状态机识别：反转态");
        }

        var trustAssessment = AssessEvidenceTrust(root);
        var confidenceCap = trustAssessment.MaxConfidence;
        var guardrailSignals = new List<string>();

        if (contextFeatures.CoverageScore > 0m && contextFeatures.CoverageScore < 45m)
        {
            confidenceCap = Math.Min(confidenceCap, 58m);
            guardrailSignals.Add("coverage_penalty");
            AppendUniqueSignal(root, "证据覆盖不足：coverageScore 偏低，置信度已压低");
        }

        if (contextFeatures.ConflictScore >= 45m)
        {
            confidenceCap = Math.Min(confidenceCap, 60m);
            guardrailSignals.Add("conflict_penalty");
            AppendUniqueSignal(root, "证据分歧较大：conflictScore 偏高，倾向保守");
        }

        if (contextFeatures.ExpandedWindow)
        {
            confidenceCap = Math.Min(confidenceCap, 68m);
            guardrailSignals.Add("expanded_window_penalty");
            AppendUniqueSignal(root, "证据已扩窗到 7 天：时效性下降，置信度受限");
        }

        if (dependencyFailures > 0 || contextFeatures.DegradedFlags.Count > 0)
        {
            confidenceCap = Math.Min(confidenceCap, dependencyFailures > 0 ? 48m : 55m);
            guardrailSignals.Add("degraded_path_penalty");
            AppendUniqueSignal(root, "存在依赖失败或 degradedFlags，最终结论已主动降级");
        }

        if (confidence > confidenceCap)
        {
            root["confidence_score"] = confidenceCap;
            AppendUniqueSignal(root, trustAssessment.Signal);
        }

        guardrails["confidenceCap"] = confidenceCap;
        guardrails["coveragePenaltyApplied"] = guardrailSignals.Any(flag => string.Equals(flag, "coverage_penalty", StringComparison.Ordinal));
        guardrails["conflictPenaltyApplied"] = guardrailSignals.Any(flag => string.Equals(flag, "conflict_penalty", StringComparison.Ordinal));
        guardrails["degradedPenaltyApplied"] = guardrailSignals.Any(flag => string.Equals(flag, "degraded_path_penalty", StringComparison.Ordinal));
        guardrails["dependencyFailureCount"] = dependencyFailures;
        guardrails["degradedFlags"] = new JsonArray(contextFeatures.DegradedFlags.Select(flag => JsonValue.Create(flag)).ToArray());
        guardrails["notes"] = new JsonArray(guardrailSignals.Select(flag => JsonValue.Create(flag)).ToArray());

        root["directional_bias"] = currentDirection switch
        {
            "加仓" => "看多",
            "减仓" => "看空",
            _ => "观察"
        };
        NormalizeProbabilities(root, currentDirection ?? "观察", TryReadDecimal(root, "confidence_score") ?? confidenceCap);

        revision["required"] = changed;
        revision["previousDirection"] = previousDirection;
        if (changed && string.IsNullOrWhiteSpace(TryReadString(revision, "reason")))
        {
            revision["reason"] = BuildAutoRevisionReason(previousDirection, currentDirection, divergence, strongCounterEvidence, hysteresisApplied);
        }

        using var doc = JsonDocument.Parse(root.ToJsonString());
        return doc.RootElement.Clone();
    }

    private static string BuildAutoRevisionReason(string? previous, string? current, bool divergence, bool strongCounterEvidence, bool hysteresisApplied)
    {
        if (hysteresisApplied)
        {
            return $"由{previous}尝试改判为{current}，但置信度不足且无强反证，按滞后机制保持原方向";
        }

        var reasons = new List<string> { $"由{previous}改判为{current}" };
        if (divergence)
        {
            reasons.Add("短中周期存在分歧");
        }

        if (strongCounterEvidence)
        {
            reasons.Add("存在强反证触发失效条件");
        }

        return string.Join("；", reasons);
    }

    private static (string? ShortTrend, string? MidTrend, bool Divergence) EvaluateTimeframeConsistency(IReadOnlyList<StockAgentResultDto> dependencyResults)
    {
        var trendAgent = dependencyResults.FirstOrDefault(item =>
            string.Equals(item.AgentId, "trend_analysis", StringComparison.OrdinalIgnoreCase));
        if (trendAgent is null || trendAgent.Data is null)
        {
            return (null, null, false);
        }

        try
        {
            using var doc = JsonDocument.Parse(trendAgent.Data.Value.GetRawText());
            if (!TryGetPropertyIgnoreCase(doc.RootElement, "timeframeSignals", out var signals)
                || signals.ValueKind != JsonValueKind.Array)
            {
                return (null, null, false);
            }

            string? shortTrend = null;
            string? weekTrend = null;
            string? monthTrend = null;
            foreach (var item in signals.EnumerateArray())
            {
                var timeframe = TryReadString(item, "timeframe");
                var trend = TryReadString(item, "trend");
                if (string.IsNullOrWhiteSpace(timeframe) || string.IsNullOrWhiteSpace(trend))
                {
                    continue;
                }

                if (string.Equals(timeframe, "1D", StringComparison.OrdinalIgnoreCase))
                {
                    shortTrend = trend;
                }
                else if (string.Equals(timeframe, "1W", StringComparison.OrdinalIgnoreCase))
                {
                    weekTrend = trend;
                }
                else if (string.Equals(timeframe, "1M", StringComparison.OrdinalIgnoreCase))
                {
                    monthTrend = trend;
                }
            }

            var midTrend = weekTrend ?? monthTrend;
            var divergence = !string.IsNullOrWhiteSpace(shortTrend)
                && !string.IsNullOrWhiteSpace(midTrend)
                && !string.Equals(shortTrend, midTrend, StringComparison.OrdinalIgnoreCase);

            return (shortTrend, midTrend, divergence);
        }
        catch
        {
            return (null, null, false);
        }
    }

    private static (string State, bool Reversed) EvaluateMarketState(string contextJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(contextJson);
            if (!TryGetPropertyIgnoreCase(doc.RootElement, "kLines", out var kLines) || kLines.ValueKind != JsonValueKind.Array)
            {
                return ("震荡", false);
            }

            var closes = kLines
                .EnumerateArray()
                .Select(item => TryReadDecimal(item, "close"))
                .Where(item => item.HasValue)
                .Select(item => item!.Value)
                .ToList();
            if (closes.Count < 20)
            {
                return ("震荡", false);
            }

            var currentMa5 = closes.TakeLast(5).Average();
            var currentMa20 = closes.TakeLast(20).Average();
            var prevStart = Math.Max(0, closes.Count - 10);
            var prev20Start = Math.Max(0, closes.Count - 25);
            var prevMa5 = closes.Skip(prevStart).Take(5).DefaultIfEmpty(currentMa5).Average();
            var prevMa20 = closes.Skip(prev20Start).Take(20).DefaultIfEmpty(currentMa20).Average();

            var currentSign = Math.Sign(currentMa5 - currentMa20);
            var previousSign = Math.Sign(prevMa5 - prevMa20);
            var reversed = currentSign != 0 && previousSign != 0 && currentSign != previousSign;
            if (reversed)
            {
                return ("反转", true);
            }

            var latest = closes[^1];
            if (Math.Abs(currentMa5 - currentMa20) < Math.Max(0.01m, latest * 0.01m))
            {
                return ("震荡", false);
            }

            return ("延续", false);
        }
        catch
        {
            return ("震荡", false);
        }
    }

    private static bool HasStrongCounterEvidence(JsonObject root)
    {
        var invalidations = ReadCommanderConditionList(root, "invalid_conditions", "invalidations");
        if (invalidations.Count >= 2)
        {
            return true;
        }

        return invalidations.Any(item => item.Contains("跌破", StringComparison.OrdinalIgnoreCase)
            || item.Contains("破位", StringComparison.OrdinalIgnoreCase)
            || item.Contains("失效", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetCurrentDirection(JsonObject root)
    {
        var explicitBias = TryReadString(root, "directional_bias");
        if (!string.IsNullOrWhiteSpace(explicitBias))
        {
            return explicitBias;
        }

        var opinion = TryReadString(root, "analysis_opinion") ?? TryReadString(root, "summary") ?? string.Empty;
        if (opinion.Contains("减仓", StringComparison.OrdinalIgnoreCase)
            || opinion.Contains("清仓", StringComparison.OrdinalIgnoreCase)
            || opinion.Contains("看空", StringComparison.OrdinalIgnoreCase))
        {
            return "减仓";
        }

        if (opinion.Contains("加仓", StringComparison.OrdinalIgnoreCase)
            || opinion.Contains("试仓", StringComparison.OrdinalIgnoreCase)
            || opinion.Contains("看多", StringComparison.OrdinalIgnoreCase))
        {
            return "加仓";
        }

        if (opinion.Contains("观察", StringComparison.OrdinalIgnoreCase)
            || opinion.Contains("观望", StringComparison.OrdinalIgnoreCase)
            || opinion.Contains("等待", StringComparison.OrdinalIgnoreCase))
        {
            return "观察";
        }

        return "未知";
    }

    private static string BuildHysteresisOpinion(string? previousDirection, string? currentOpinion)
    {
        var preservedDirection = string.IsNullOrWhiteSpace(previousDirection) ? "原方向" : previousDirection;
        var rawOpinion = string.IsNullOrWhiteSpace(currentOpinion) ? "本次改判置信度不足。" : currentOpinion;
        return $"按滞后机制暂维持{preservedDirection}。{rawOpinion}";
    }

    private static (decimal MaxConfidence, string Signal) AssessEvidenceTrust(JsonObject root)
    {
        if (root["evidence"] is not JsonArray evidence || evidence.Count == 0)
        {
            return (45m, "证据质量不足：缺少可追溯高质量依据，置信度受限");
        }

        var hasStrongEvidence = false;
        var hasModerateEvidence = false;

        foreach (var node in evidence)
        {
            if (node is not JsonObject item)
            {
                continue;
            }

            var readStatus = TryReadString(item, "readStatus");
            if (string.Equals(readStatus, "full_text_read", StringComparison.OrdinalIgnoreCase))
            {
                hasStrongEvidence = true;
                break;
            }

            if (string.Equals(readStatus, "summary_only", StringComparison.OrdinalIgnoreCase)
                || string.Equals(readStatus, "title_only", StringComparison.OrdinalIgnoreCase))
            {
                hasModerateEvidence = true;
            }
        }

        if (hasStrongEvidence)
        {
            return (90m, "证据已包含全文核验，可维持较高置信度");
        }

        if (hasModerateEvidence)
        {
            return (72m, "证据以摘要或标题核验为主，置信度已下调");
        }

        return (55m, "证据仅为元数据或未核验，置信度受限");
    }

    private static void NormalizeProbabilities(JsonObject root, string direction, decimal confidence)
    {
        var probabilities = EnsureObject(root, "probabilities");
        var bull = TryReadDecimal(probabilities, "bull");
        var @base = TryReadDecimal(probabilities, "base");
        var bear = TryReadDecimal(probabilities, "bear");
        if (bull.HasValue && @base.HasValue && bear.HasValue && bull + @base + bear > 0m)
        {
            return;
        }

        var capped = Math.Clamp(confidence, 0m, 100m);
        if (string.Equals(direction, "加仓", StringComparison.OrdinalIgnoreCase))
        {
            var bullProbability = Math.Min(85m, 35m + capped * 0.5m);
            probabilities["bull"] = bullProbability;
            probabilities["base"] = 100m - bullProbability - 15m;
            probabilities["bear"] = 15m;
            return;
        }

        if (string.Equals(direction, "减仓", StringComparison.OrdinalIgnoreCase))
        {
            var bearProbability = Math.Min(85m, 35m + capped * 0.5m);
            probabilities["bull"] = 15m;
            probabilities["base"] = 100m - 15m - bearProbability;
            probabilities["bear"] = bearProbability;
            return;
        }

        probabilities["bull"] = Math.Max(15m, 30m - capped * 0.1m);
        probabilities["base"] = Math.Min(70m, 40m + capped * 0.2m);
        probabilities["bear"] = Math.Max(15m, 30m - capped * 0.1m);
    }

    private static (decimal CoverageScore, decimal ConflictScore, bool ExpandedWindow, IReadOnlyList<string> DegradedFlags) ParseContextFeatures(string contextJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(contextJson);
            if (!TryGetPropertyIgnoreCase(doc.RootElement, "deterministicFeatures", out var features)
                || features.ValueKind != JsonValueKind.Object)
            {
                return (0m, 0m, false, Array.Empty<string>());
            }

            decimal coverageScore = 0m;
            decimal conflictScore = 0m;
            var expandedWindow = false;
            if (TryGetPropertyIgnoreCase(features, "evidence", out var evidence) && evidence.ValueKind == JsonValueKind.Object)
            {
                coverageScore = TryReadDecimal(evidence, "coverageScore") ?? 0m;
                conflictScore = TryReadDecimal(evidence, "conflictScore") ?? 0m;
                expandedWindow = TryGetPropertyIgnoreCase(evidence, "expandedWindow", out var expanded) && expanded.ValueKind is JsonValueKind.True or JsonValueKind.False && expanded.GetBoolean();
            }

            var degradedFlags = TryGetPropertyIgnoreCase(features, "degradedFlags", out var degraded) && degraded.ValueKind == JsonValueKind.Array
                ? degraded.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToArray()
                : Array.Empty<string>();
            return (coverageScore, conflictScore, expandedWindow, degradedFlags);
        }
        catch
        {
            return (0m, 0m, false, Array.Empty<string>());
        }
    }

    private static IReadOnlyList<StockAgentCommanderHistoryItemDto> ParseCommanderHistory(string contextJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(contextJson);
            if (!TryGetPropertyIgnoreCase(doc.RootElement, "commanderHistory", out var history)
                || !TryGetPropertyIgnoreCase(history, "items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<StockAgentCommanderHistoryItemDto>();
            }

            var result = new List<StockAgentCommanderHistoryItemDto>();
            foreach (var item in items.EnumerateArray())
            {
                var direction = TryReadString(item, "direction") ?? "未知";
                var summary = TryReadString(item, "summary");
                var confidence = TryReadDecimal(item, "confidence");
                var triggers = ReadStringArray(item, "triggers");
                var invalidations = ReadStringArray(item, "invalidations");
                var riskLimits = ReadStringArray(item, "riskLimits");
                var evidenceSummary = ReadStringArray(item, "evidenceSummary");
                var finalResultTag = TryReadString(item, "finalResultTag");
                var createdAtText = TryReadString(item, "createdAt");
                var createdAt = DateTime.TryParse(createdAtText, out var parsed) ? parsed : DateTime.MinValue;

                result.Add(new StockAgentCommanderHistoryItemDto(
                    createdAt,
                    direction,
                    confidence,
                    triggers,
                    invalidations,
                    riskLimits,
                    evidenceSummary,
                    finalResultTag,
                    summary));
            }

            return result;
        }
        catch
        {
            return Array.Empty<StockAgentCommanderHistoryItemDto>();
        }
    }

    private static void AppendUniqueSignal(JsonObject root, string signal)
    {
        if (root["signals"] is not JsonArray signals)
        {
            signals = new JsonArray();
            root["signals"] = signals;
        }

        var exists = signals.Any(item => string.Equals(item?.GetValue<string>(), signal, StringComparison.Ordinal));
        if (!exists)
        {
            signals.Add(signal);
        }
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

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string propertyName, out JsonElement value)
    {
        value = default;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static string? TryReadString(JsonElement root, string key)
    {
        return TryGetPropertyIgnoreCase(root, key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static decimal? TryReadDecimal(JsonElement root, string key)
    {
        if (!TryGetPropertyIgnoreCase(root, key, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetDecimal(out var number) ? number : null;
    }

    private static string? TryReadString(JsonObject root, string key)
    {
        if (root[key] is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            return null;
        }

        return text;
    }

    private static decimal? TryReadDecimal(JsonObject root, string key)
    {
        if (root[key] is not JsonValue value || !value.TryGetValue<decimal>(out var number))
        {
            return null;
        }

        return number;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonObject root, string key)
    {
        if (root[key] is not JsonArray arr)
        {
            return Array.Empty<string>();
        }

        return arr
            .Select(node => node?.GetValue<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string key)
    {
        if (!TryGetPropertyIgnoreCase(root, key, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return arr
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadCommanderConditionList(JsonObject root, string singularKey, string fallbackArrayKey)
    {
        var single = TryReadString(root, singularKey);
        if (!string.IsNullOrWhiteSpace(single))
        {
            return new[] { single };
        }

        return ReadStringArray(root, fallbackArrayKey);
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
        EnsureProperty(metrics, "peRatio", null);
        EnsureProperty(metrics, "floatMarketCap", null);
        EnsureProperty(metrics, "volumeRatio", null);
        EnsureProperty(metrics, "shareholderCount", null);
        EnsureProperty(metrics, "sector", null);
        EnsureProperty(metrics, "date", null);

        EnsureProperty(root, "analysis_opinion", null);
        EnsureProperty(root, "confidence_score", null);
        EnsureProperty(root, "directional_bias", null);
        EnsureProperty(root, "trigger_conditions", null);
        EnsureProperty(root, "invalid_conditions", null);
        EnsureProperty(root, "risk_warning", null);

        var probabilities = EnsureObject(root, "probabilities");
        EnsureProperty(probabilities, "bull", null);
        EnsureProperty(probabilities, "base", null);
        EnsureProperty(probabilities, "bear", null);

        var revision = EnsureObject(root, "revision");
        EnsureProperty(revision, "required", false);
        EnsureProperty(revision, "reason", null);
        EnsureProperty(revision, "previousDirection", null);

        var consistency = EnsureObject(root, "consistency");
        EnsureProperty(consistency, "shortTermTrend", null);
        EnsureProperty(consistency, "midTermTrend", null);
        EnsureProperty(consistency, "status", "一致");
        EnsureProperty(consistency, "note", null);

        var marketState = EnsureObject(root, "marketState");
        EnsureProperty(marketState, "state", "震荡");
        EnsureProperty(marketState, "hysteresisApplied", false);
        EnsureProperty(marketState, "strongCounterEvidence", false);
        EnsureProperty(marketState, "overrideReason", null);

        var guardrails = EnsureObject(root, "guardrails");
        EnsureProperty(guardrails, "confidenceCap", null);
        EnsureProperty(guardrails, "coveragePenaltyApplied", false);
        EnsureProperty(guardrails, "conflictPenaltyApplied", false);
        EnsureProperty(guardrails, "degradedPenaltyApplied", false);
        EnsureProperty(guardrails, "dependencyFailureCount", 0);
        EnsureArray(guardrails, "degradedFlags");
        EnsureArray(guardrails, "notes");

        TryReadString(root, "analysis_opinion", out var analysisOpinion);
        TryReadString(root, "summary", out var summaryText);

        if (!string.IsNullOrWhiteSpace(analysisOpinion) && string.IsNullOrWhiteSpace(summaryText))
        {
            root["summary"] = analysisOpinion;
        }

        if (string.IsNullOrWhiteSpace(analysisOpinion) && !string.IsNullOrWhiteSpace(summaryText))
        {
            root["analysis_opinion"] = summaryText;
        }

        if ((!TryReadString(root, "directional_bias", out var biasText) || string.IsNullOrWhiteSpace(biasText)) && !string.IsNullOrWhiteSpace(analysisOpinion))
        {
            root["directional_bias"] = analysisOpinion.Contains("减仓", StringComparison.OrdinalIgnoreCase) || analysisOpinion.Contains("看空", StringComparison.OrdinalIgnoreCase)
                ? "看空"
                : analysisOpinion.Contains("加仓", StringComparison.OrdinalIgnoreCase) || analysisOpinion.Contains("试仓", StringComparison.OrdinalIgnoreCase) || analysisOpinion.Contains("看多", StringComparison.OrdinalIgnoreCase)
                    ? "看多"
                    : "观察";
        }

        if ((!TryReadString(root, "trigger_conditions", out var triggerText) || string.IsNullOrWhiteSpace(triggerText))
            && root["triggers"] is JsonArray triggers && triggers.Count > 0)
        {
            root["trigger_conditions"] = triggers[0]?.GetValue<string>();
        }

        if ((!TryReadString(root, "invalid_conditions", out var invalidText) || string.IsNullOrWhiteSpace(invalidText))
            && root["invalidations"] is JsonArray invalidations && invalidations.Count > 0)
        {
            root["invalid_conditions"] = invalidations[0]?.GetValue<string>();
        }

        if ((!TryReadString(root, "risk_warning", out var riskText) || string.IsNullOrWhiteSpace(riskText))
            && root["riskLimits"] is JsonArray riskLimits && riskLimits.Count > 0)
        {
            root["risk_warning"] = riskLimits[0]?.GetValue<string>();
        }
    }

    private static void NormalizeStockNews(JsonObject root)
    {
        EnsureProperty(root, "confidence", null);
        var metrics = EnsureObject(root, "metrics");
        var sentiment = EnsureObject(root, "sentiment");
        EnsureProperty(sentiment, "positive", null);
        EnsureProperty(sentiment, "neutral", null);
        EnsureProperty(sentiment, "negative", null);
        EnsureProperty(sentiment, "overall", null);

        EnsureArray(root, "events");
        NormalizeSharedMetrics(root, metrics);
        EnforceNewsEvidenceGuardrail(root);
    }

    private static void NormalizeSectorNews(JsonObject root)
    {
        EnsureProperty(root, "sector", null);
        EnsureProperty(root, "confidence", null);
        EnsureProperty(root, "sectorChangePercent", null);
        var metrics = EnsureObject(root, "metrics");
        EnsureArray(root, "topMovers");
        NormalizeSharedMetrics(root, metrics);
        PromoteMetric(metrics, "sectorChangePercent", ReadNumber(root, "sectorChangePercent"));
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
        EnsureProperty(metrics, "shareholderCount", null);

        EnsureArray(root, "highlights");
        NormalizeSharedMetrics(root, metrics);
    }

    private static void NormalizeTrend(JsonObject root)
    {
        EnsureProperty(root, "confidence", null);
        var metrics = EnsureObject(root, "metrics");
        EnsureArray(root, "timeframeSignals");
        EnsureArray(root, "forecast");
        NormalizeSharedMetrics(root, metrics);
    }

    private static void NormalizeSharedMetrics(JsonObject root, JsonObject metrics)
    {
        PromoteMetric(metrics, "entryScore", FindFirstNumber(root, "entryScore", ("analysis", "entryScore")));
        PromoteMetric(metrics, "valuationScore", FindFirstNumber(root, "valuationScore", ("analysis", "valuationScore")));
        PromoteMetric(metrics, "positionPercent", FindFirstNumber(root, "positionPercent", ("analysis", "positionPercent")));
        PromoteMetric(metrics, "targetPrice", FindFirstNumber(root, "targetPrice", ("chart", "targetPrice")));
        PromoteMetric(metrics, "takeProfitPrice", FindFirstNumber(root, "takeProfitPrice", ("chart", "takeProfitPrice")));
        PromoteMetric(metrics, "stopLossPrice", FindFirstNumber(root, "stopLossPrice", ("chart", "stopLossPrice")));
        PromoteMetric(metrics, "riseProbability", FindFirstNumber(
            root,
            "riseProbability",
            ("probabilities", "rise_probability"),
            ("probabilities", "up_probability"),
            ("probability_analysis", "rise_probability"),
            ("probability_analysis", "up_probability"),
            ("analysis", "rise_probability"),
            ("analysis", "probability_up")));
        PromoteMetric(metrics, "fallProbability", FindFirstNumber(
            root,
            "fallProbability",
            ("probabilities", "fall_probability"),
            ("probabilities", "down_probability"),
            ("probability_analysis", "fall_probability"),
            ("probability_analysis", "down_probability"),
            ("analysis", "fall_probability"),
            ("analysis", "probability_down")));
    }

    private static decimal? FindFirstNumber(JsonObject root, string topLevelKey, params (string Parent, string Child)[] nestedKeys)
    {
        var direct = ReadNumber(root, topLevelKey);
        if (direct.HasValue)
        {
            return direct;
        }

        foreach (var (parent, child) in nestedKeys)
        {
            if (root[parent] is not JsonObject parentObject)
            {
                continue;
            }

            var nested = ReadNumber(parentObject, child);
            if (nested.HasValue)
            {
                return nested;
            }
        }

        return null;
    }

    private static decimal? ReadNumber(JsonObject root, string key)
    {
        if (root[key] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<decimal>(out var decimalValue))
        {
            return decimalValue;
        }

        if (value.TryGetValue<double>(out var doubleValue))
        {
            return Convert.ToDecimal(doubleValue);
        }

        if (value.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        if (value.TryGetValue<string>(out var text) && decimal.TryParse(text, out decimalValue))
        {
            return decimalValue;
        }

        return null;
    }

    private static void PromoteMetric(JsonObject metrics, string key, decimal? value)
    {
        if (value.HasValue)
        {
            metrics[key] = value.Value;
            return;
        }

        EnsureProperty(metrics, key, null);
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
            EnsureProperty(item, "title", null);
            EnsureProperty(item, "source", null);
            EnsureProperty(item, "publishedAt", null);
            EnsureProperty(item, "crawledAt", null);
            EnsureProperty(item, "url", null);
            EnsureProperty(item, "excerpt", null);
            EnsureProperty(item, "readMode", null);
            EnsureProperty(item, "readStatus", null);
            EnsureProperty(item, "ingestedAt", null);
            EnsureProperty(item, "localFactId", null);
            EnsureProperty(item, "sourceRecordId", null);
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

            TryReadString(item, "readStatus", out var readStatus);
            if (string.Equals(readStatus, "metadata_only", StringComparison.OrdinalIgnoreCase)
                || string.Equals(readStatus, "unverified", StringComparison.OrdinalIgnoreCase)
                || string.Equals(readStatus, "fetch_failed", StringComparison.OrdinalIgnoreCase))
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
