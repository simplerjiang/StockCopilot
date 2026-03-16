using System.Text.Json;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface ITradingPlanReviewService
{
    Task<int> EvaluateAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

public sealed class TradingPlanReviewService : ITradingPlanReviewService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _dbContext;
    private readonly ILlmService _llmService;
    private readonly TradingPlanReviewOptions _options;
    private readonly ILogger<TradingPlanReviewService> _logger;

    public TradingPlanReviewService(
        AppDbContext dbContext,
        ILlmService llmService,
        IOptions<TradingPlanReviewOptions> options,
        ILogger<TradingPlanReviewService> logger)
    {
        _dbContext = dbContext;
        _llmService = llmService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> EvaluateAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || !ChinaAStockMarketClock.IsTradingSession(now))
        {
            return 0;
        }

        var activeSymbols = await _dbContext.ActiveWatchlists
            .AsNoTracking()
            .Where(item => item.IsEnabled)
            .Select(item => item.Symbol)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        if (activeSymbols.Length == 0)
        {
            return 0;
        }

        var pendingPlans = await _dbContext.TradingPlans
            .Where(item => item.Status == TradingPlanStatus.Pending && activeSymbols.Contains(item.Symbol))
            .OrderBy(item => item.CreatedAt)
            .Take(Math.Max(1, _options.MaxPlansPerPass))
            .ToListAsync(cancellationToken);

        if (pendingPlans.Count == 0)
        {
            return 0;
        }

        var symbols = pendingPlans
            .Select(item => item.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var lookbackCutoff = now.UtcDateTime.AddMinutes(-Math.Max(15, _options.LookbackMinutes));

        var newsRows = await _dbContext.LocalStockNews
            .AsNoTracking()
            .Where(item => symbols.Contains(item.Symbol) && item.PublishTime >= lookbackCutoff)
            .OrderByDescending(item => item.PublishTime)
            .ToListAsync(cancellationToken);

        if (newsRows.Count == 0)
        {
            return 0;
        }

        var latestQuotes = (await _dbContext.StockQuoteSnapshots
                .AsNoTracking()
                .Where(item => symbols.Contains(item.Symbol))
                .OrderByDescending(item => item.Timestamp)
                .ToListAsync(cancellationToken))
            .GroupBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(item => item.Symbol, item => item, StringComparer.OrdinalIgnoreCase);

        var planIds = pendingPlans.Select(item => item.Id).ToArray();
        var processedKeys = (await _dbContext.TradingPlanEvents
                .AsNoTracking()
                .Where(item => planIds.Contains(item.PlanId)
                    && (item.EventType == TradingPlanEventType.NewsReviewed || item.EventType == TradingPlanEventType.ReviewRequired))
                .ToListAsync(cancellationToken))
            .Select(item => BuildReviewKey(item.PlanId, TryReadLocalNewsId(item.MetadataJson)))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changes = 0;
        foreach (var plan in pendingPlans)
        {
            var candidateNews = newsRows
                .Where(item => string.Equals(item.Symbol, plan.Symbol, StringComparison.OrdinalIgnoreCase)
                    && item.PublishTime >= plan.CreatedAt)
                .Take(Math.Max(1, _options.MaxNewsPerSymbol))
                .ToArray();

            foreach (var news in candidateNews)
            {
                var reviewKey = BuildReviewKey(plan.Id, news.Id);
                if (processedKeys.Contains(reviewKey))
                {
                    continue;
                }

                TradingPlanThreatReviewResult review;
                try
                {
                    var llmResult = await _llmService.ChatAsync(
                        _options.LlmProvider,
                        new LlmChatRequest(BuildPrompt(plan, news), _options.LlmModel, 0.1, false),
                        cancellationToken);
                    review = ParseResult(llmResult.Content);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "交易计划突发新闻语义复核失败，symbol={Symbol}, planId={PlanId}, newsId={NewsId}", plan.Symbol, plan.Id, news.Id);
                    continue;
                }

                processedKeys.Add(reviewKey);
                var latestPrice = latestQuotes.TryGetValue(plan.Symbol, out var latestQuote) ? latestQuote.Price : (decimal?)null;
                var occurredAt = news.PublishTime == default ? now.UtcDateTime : news.PublishTime;
                var metadataJson = JsonSerializer.Serialize(new
                {
                    localNewsId = news.Id,
                    newsTitle = news.Title,
                    translatedTitle = news.TranslatedTitle,
                    source = news.Source,
                    sourceTag = news.SourceTag,
                    publishTime = news.PublishTime,
                    aiSentiment = news.AiSentiment,
                    aiTarget = news.AiTarget,
                    aiTags = ParseTags(news.AiTags),
                    isPlanThreatened = review.IsPlanThreatened,
                    reason = review.Reason,
                    confidence = review.Confidence
                });

                if (review.IsPlanThreatened && review.Confidence >= _options.ThreatConfidenceThreshold)
                {
                    plan.Status = TradingPlanStatus.ReviewRequired;
                    plan.UpdatedAt = now.UtcDateTime;
                    _dbContext.TradingPlanEvents.Add(new TradingPlanEvent
                    {
                        PlanId = plan.Id,
                        Symbol = plan.Symbol,
                        EventType = TradingPlanEventType.ReviewRequired,
                        Strategy = "news-review",
                        Reason = review.Reason,
                        CreatedAt = now.UtcDateTime,
                        Severity = TradingPlanEventSeverity.Critical,
                        Message = $"突发新闻「{news.Title}」威胁原计划，需人工复核：{review.Reason}",
                        SnapshotPrice = latestPrice,
                        MetadataJson = metadataJson,
                        OccurredAt = occurredAt
                    });
                    changes++;
                    break;
                }

                _dbContext.TradingPlanEvents.Add(new TradingPlanEvent
                {
                    PlanId = plan.Id,
                    Symbol = plan.Symbol,
                    EventType = TradingPlanEventType.NewsReviewed,
                    Strategy = "news-review",
                    Reason = review.Reason,
                    CreatedAt = now.UtcDateTime,
                    Severity = TradingPlanEventSeverity.Info,
                    Message = $"突发新闻「{news.Title}」已复核，当前未构成计划威胁：{review.Reason}",
                    SnapshotPrice = latestPrice,
                    MetadataJson = metadataJson,
                    OccurredAt = occurredAt
                });
                changes++;
            }
        }

        if (changes > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return changes;
    }

    private static string BuildPrompt(TradingPlan plan, LocalStockNews news)
    {
        var payload = JsonSerializer.Serialize(new
        {
            plan = new
            {
                planId = plan.Id,
                symbol = plan.Symbol,
                name = plan.Name,
                direction = plan.Direction.ToString(),
                analysisSummary = plan.AnalysisSummary,
                invalidConditions = plan.InvalidConditions,
                expectedCatalyst = plan.ExpectedCatalyst,
                riskLimits = plan.RiskLimits,
                triggerPrice = plan.TriggerPrice,
                invalidPrice = plan.InvalidPrice,
                stopLossPrice = plan.StopLossPrice
            },
            news = new
            {
                id = news.Id,
                title = news.Title,
                translatedTitle = news.TranslatedTitle,
                source = news.Source,
                sourceTag = news.SourceTag,
                publishTime = news.PublishTime,
                aiSentiment = news.AiSentiment,
                aiTarget = news.AiTarget,
                aiTags = ParseTags(news.AiTags)
            }
        }, JsonOptions);

        return "你是交易计划突发新闻复核器。只返回 JSON 对象，不要 Markdown，不要解释。" +
               "\n任务：判断输入的单条突发新闻是否显著威胁当前 Pending 交易计划。" +
               "\n约束：" +
               "\n1. 你只能输出结构化辅助判断，不能输出买入/卖出/加仓/减仓等动作建议。" +
               "\n2. 你只判断该新闻是否削弱原计划成立前提，重点参考 invalidConditions、riskLimits、analysisSummary。" +
               "\n3. 只返回一个 JSON object，字段固定为 isPlanThreatened(boolean), reason(string), confidence(number 0-100)。" +
               "\n4. 若信息不足，isPlanThreatened 返回 false，reason 说明不足原因，confidence 降低。" +
               "\n输入：\n" + payload;
    }

    private static TradingPlanThreatReviewResult ParseResult(string content)
    {
        var cleaned = ExtractJson(content);
        using var document = JsonDocument.Parse(cleaned);
        var root = document.RootElement;
        var threatened = root.TryGetProperty("isPlanThreatened", out var threatenedElement)
            && threatenedElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            && threatenedElement.GetBoolean();
        var reason = root.TryGetProperty("reason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String
            ? reasonElement.GetString()?.Trim()
            : null;
        var confidence = root.TryGetProperty("confidence", out var confidenceElement)
            ? ParseConfidence(confidenceElement)
            : 0;

        return new TradingPlanThreatReviewResult(
            threatened,
            string.IsNullOrWhiteSpace(reason) ? "未提供明确复核原因" : reason!,
            confidence);
    }

    private static string ExtractJson(string content)
    {
        var span = content.AsSpan().Trim();
        if (span.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            span = span[7..];
        }
        else if (span.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            span = span[3..];
        }

        if (span.EndsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            span = span[..^3];
        }

        var cleaned = span.Trim().ToString();
        var objectStart = cleaned.IndexOf('{');
        var objectEnd = cleaned.LastIndexOf('}');
        if (objectStart >= 0 && objectEnd >= objectStart)
        {
            return cleaned.Substring(objectStart, objectEnd - objectStart + 1);
        }

        return cleaned;
    }

    private static int ParseConfidence(JsonElement element)
    {
        decimal? rawValue = element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.String when decimal.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var stringValue) => stringValue,
            _ => null
        };

        if (!rawValue.HasValue)
        {
            return 0;
        }

        return (int)Math.Clamp(decimal.Round(rawValue.Value, MidpointRounding.AwayFromZero), 0m, 100m);
    }

    private static string? TryReadLocalNewsId(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (document.RootElement.TryGetProperty("localNewsId", out var property) && property.ValueKind == JsonValueKind.Number)
            {
                return property.GetInt64().ToString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string BuildReviewKey(long planId, long localNewsId)
    {
        return $"{planId}:{localNewsId}";
    }

    private static string? BuildReviewKey(long planId, string? localNewsId)
    {
        return long.TryParse(localNewsId, out var parsed) ? BuildReviewKey(planId, parsed) : null;
    }

    private static string[] ParseTags(string? aiTags)
    {
        if (string.IsNullOrWhiteSpace(aiTags))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(aiTags, JsonOptions)
                ?.Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record TradingPlanThreatReviewResult(bool IsPlanThreatened, string Reason, int Confidence);
}