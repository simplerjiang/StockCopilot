using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

public interface ILocalFactAiEnrichmentService
{
    Task ProcessMarketPendingAsync(CancellationToken cancellationToken = default);
    Task ProcessSymbolPendingAsync(string symbol, CancellationToken cancellationToken = default);
}

public sealed class LocalFactAiEnrichmentService : ILocalFactAiEnrichmentService
{
    private const string NeutralSentiment = "中性";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _dbContext;
    private readonly ILlmService _llmService;
    private readonly ILlmSettingsStore _settingsStore;
    private readonly StockSyncOptions _options;
    private readonly ILogger<LocalFactAiEnrichmentService> _logger;

    public LocalFactAiEnrichmentService(
        AppDbContext dbContext,
        ILlmService llmService,
        ILlmSettingsStore settingsStore,
        IOptions<StockSyncOptions> options,
        ILogger<LocalFactAiEnrichmentService> logger)
    {
        _dbContext = dbContext;
        _llmService = llmService;
        _settingsStore = settingsStore;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProcessMarketPendingAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _dbContext.LocalSectorReports
            .Where(item => item.Level == "market" && !item.IsAiProcessed)
            .OrderBy(item => item.PublishTime)
            .Take(500)
            .ToListAsync(cancellationToken);

        await ProcessBatchesAsync(
            pending.Select(item => new PendingNewsEnvelope(
                $"market:{item.Id}",
                item.Title,
                item.Source,
                item.SourceTag,
                item.Level,
                null,
                item.SectorName,
                item.PublishTime,
                Apply: result => Apply(item, result))).ToList(),
            cancellationToken);
    }

    public async Task ProcessSymbolPendingAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = StockSymbolNormalizer.Normalize(symbol);

        var stockItems = await _dbContext.LocalStockNews
            .Where(item => item.Symbol == normalized && !item.IsAiProcessed)
            .OrderBy(item => item.PublishTime)
            .Take(200)
            .ToListAsync(cancellationToken);

        var sectorItems = await _dbContext.LocalSectorReports
            .Where(item => item.Symbol == normalized && item.Level == "sector" && !item.IsAiProcessed)
            .OrderBy(item => item.PublishTime)
            .Take(200)
            .ToListAsync(cancellationToken);

        var envelopes = new List<PendingNewsEnvelope>();
        envelopes.AddRange(stockItems.Select(item => new PendingNewsEnvelope(
            $"stock:{item.Id}",
            item.Title,
            item.Source,
            item.SourceTag,
            item.Category,
            item.Symbol,
            item.SectorName,
            item.PublishTime,
            Apply: result => Apply(item, result))));
        envelopes.AddRange(sectorItems.Select(item => new PendingNewsEnvelope(
            $"sector:{item.Id}",
            item.Title,
            item.Source,
            item.SourceTag,
            item.Level,
            item.Symbol,
            item.SectorName,
            item.PublishTime,
            Apply: result => Apply(item, result))));

        await ProcessBatchesAsync(envelopes, cancellationToken);
    }

    private async Task ProcessBatchesAsync(IReadOnlyList<PendingNewsEnvelope> pending, CancellationToken cancellationToken)
    {
        if (pending.Count == 0)
        {
            return;
        }

        // Resolve news cleansing settings (fallback to StockSyncOptions defaults)
        var (cleansingProvider, cleansingModel, cleansingBatchSize) = await _settingsStore.GetNewsCleansingSettingsAsync(cancellationToken);
        var aiProvider = string.IsNullOrWhiteSpace(cleansingProvider) || cleansingProvider == "active"
            ? _options.AiProvider
            : cleansingProvider;
        var aiModel = string.IsNullOrWhiteSpace(cleansingModel) ? _options.AiModel : cleansingModel;
        var aiBatchSize = cleansingBatchSize > 0 ? cleansingBatchSize : _options.AiBatchSize;

        var batchSize = Math.Clamp(aiBatchSize, 5, 20);

        // Resolve provider type for concurrency adaptation
        var resolvedProviderKey = await _settingsStore.ResolveProviderKeyAsync(aiProvider, cancellationToken);
        var providerSettings = await _settingsStore.GetProviderAsync(resolvedProviderKey, cancellationToken);
        var providerType = providerSettings?.ProviderType ?? "openai";
        var maxConcurrency = string.Equals(providerType, "ollama", StringComparison.OrdinalIgnoreCase) ? 1 : 3;

        // Build all batches upfront
        var batches = new List<PendingNewsEnvelope[]>();
        for (var index = 0; index < pending.Count; index += batchSize)
            batches.Add(pending.Skip(index).Take(batchSize).ToArray());

        // Fire LLM calls in parallel with concurrency limit; cancel remaining on rate limit
        using var semaphore = new SemaphoreSlim(maxConcurrency);
        using var rateLimitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = batches.Select(async batch =>
        {
            await semaphore.WaitAsync(rateLimitCts.Token);
            try
            {
                var prompt = BuildPrompt(batch);
                var result = await _llmService.ChatAsync(
                    aiProvider,
                    new LlmChatRequest(prompt, aiModel, 0.1, false),
                    rateLimitCts.Token);
                return (batch, content: result.Content, error: (Exception?)null);
            }
            catch (OperationCanceledException) when (rateLimitCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // Aborted due to rate limit in a sibling task — not an error
                return (batch, content: (string?)null, error: (Exception?)null);
            }
            catch (Exception ex)
            {
                if (IsRateLimit(ex))
                {
                    // Signal other tasks to stop
                    try { rateLimitCts.Cancel(); } catch (ObjectDisposedException) { }
                }
                return (batch, content: (string?)null, error: (Exception?)ex);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);

        // Apply results and save sequentially (DbContext is not thread-safe)
        var anyApplied = false;
        foreach (var (batch, content, error) in results)
        {
            if (error != null)
            {
                _logger.LogWarning(error, "本地事实 AI 清洗失败，保留未处理状态以便下轮重试");
                continue;
            }

            if (content is null) continue;

            try
            {
                var parsed = ParseBatchResult(content);
                foreach (var item in batch)
                {
                    if (parsed.TryGetValue(item.Id, out var enrichment))
                    {
                        item.Apply(enrichment);
                        anyApplied = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "本地事实 AI 清洗结果解析失败，跳过该批次");
            }
        }

        if (anyApplied)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static string BuildPrompt(IReadOnlyList<PendingNewsEnvelope> batch)
    {
        var payload = JsonSerializer.Serialize(batch.Select(item => new
        {
            id = item.Id,
            title = item.Title,
            source = item.Source,
            sourceTag = item.SourceTag,
            scope = item.Scope,
            symbol = item.Symbol,
            sectorName = item.SectorName,
            publishedAt = item.PublishTime
        }), JsonOptions);

        return "你是财经资讯清洗器。只返回 JSON 数组，不要 Markdown，不要解释。" +
               "\n任务：对输入新闻做中文财经标题翻译/提炼、多维标签、情绪与影响目标判断。" +
               "\n规则：" +
               "\n1. 输出必须是 JSON array，每个元素包含 id, translatedTitle, aiSentiment, aiTarget, aiTags。" +
               "\n2. aiSentiment 只能是 利好 / 中性 / 利空。" +
               "\n3. aiTarget 用中文描述主要影响目标，例如 大盘、板块:银行、板块:半导体、个股:浦发银行；不明确时填 无明确靶点。" +
               "\n4. aiTags 必须是数组，可从以下标签中选择多个：紧急消息、突发事件、宏观货币、地缘政治、行业周期、政策红利、财报业绩、资金面、监管政策、海外映射、商品价格、风险预警。" +
               "\n5. 若原文是英文或不利于中文阅读，translatedTitle 输出专业中文财经标题；若原文已是清晰中文，可返回 null。" +
               "\n6. 不要编造不存在的事实，不要输出数组外的任何文字。" +
               "\n输入：\n" + payload;
    }

    private static Dictionary<string, NewsEnrichmentResult> ParseBatchResult(string content)
    {
        var jsonSpan = content.AsSpan().Trim();
        if (jsonSpan.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            jsonSpan = jsonSpan.Slice(7);
        }
        else if (jsonSpan.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            jsonSpan = jsonSpan.Slice(3);
        }

        if (jsonSpan.EndsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            jsonSpan = jsonSpan.Slice(0, jsonSpan.Length - 3);
        }

        jsonSpan = jsonSpan.Trim();
        var cleaned = jsonSpan.ToString();

        // 进一步尝试定位第一个 '[' 和最后一个 ']'
        var arrayStart = cleaned.IndexOf('[');
        var arrayEnd = cleaned.LastIndexOf(']');
        if (arrayStart >= 0 && arrayEnd >= arrayStart)
        {
            cleaned = cleaned.Substring(arrayStart, arrayEnd - arrayStart + 1);
        }

        using var document = JsonDocument.Parse(cleaned);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, NewsEnrichmentResult>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, NewsEnrichmentResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var id = GetString(element, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var tags = new List<string>();
            if (element.TryGetProperty("aiTags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tagsElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            tags.Add(value.Trim());
                        }
                    }
                }
            }

            result[id] = new NewsEnrichmentResult(
                GetString(element, "translatedTitle"),
                NormalizeSentiment(GetString(element, "aiSentiment")),
                NormalizeText(GetString(element, "aiTarget")),
                tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                true);
        }

        return result;
    }

    private static void Apply(LocalStockNews entity, NewsEnrichmentResult result)
    {
        entity.TranslatedTitle = NormalizeTranslatedTitle(entity.Title, result.TranslatedTitle);
        entity.AiSentiment = result.AiSentiment;
        entity.AiTarget = result.AiTarget;
        entity.AiTags = SerializeTags(result.AiTags);
        entity.IsAiProcessed = result.IsAiProcessed;
    }

    private static void Apply(LocalSectorReport entity, NewsEnrichmentResult result)
    {
        entity.TranslatedTitle = NormalizeTranslatedTitle(entity.Title, result.TranslatedTitle);
        entity.AiSentiment = result.AiSentiment;
        entity.AiTarget = result.AiTarget;
        entity.AiTags = SerializeTags(result.AiTags);
        entity.IsAiProcessed = result.IsAiProcessed;
    }

    private static string SerializeTags(IReadOnlyList<string> tags)
    {
        return JsonSerializer.Serialize(tags.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), JsonOptions);
    }

    private static string? NormalizeTranslatedTitle(string originalTitle, string? translatedTitle)
    {
        return LocalFactDisplayPolicy.SanitizeTranslatedTitle(originalTitle, translatedTitle);
    }

    private static string NormalizeSentiment(string? value)
    {
        return value is "利好" or "利空" or "中性" ? value : NeutralSentiment;
    }

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static bool IsRateLimit(Exception ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("429", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private sealed record PendingNewsEnvelope(
        string Id,
        string Title,
        string Source,
        string SourceTag,
        string Scope,
        string? Symbol,
        string? SectorName,
        DateTime PublishTime,
        Action<NewsEnrichmentResult> Apply);

    private sealed record NewsEnrichmentResult(
        string? TranslatedTitle,
        string AiSentiment,
        string? AiTarget,
        IReadOnlyList<string> AiTags,
        bool IsAiProcessed);
}