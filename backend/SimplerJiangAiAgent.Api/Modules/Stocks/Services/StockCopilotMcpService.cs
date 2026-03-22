using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IStockCopilotMcpService
{
    Task<StockCopilotMcpEnvelopeDto<StockCopilotKlineDataDto>> GetKlineAsync(string symbol, string interval, int count, string? source, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotMinuteDataDto>> GetMinuteAsync(string symbol, string? source, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotStrategyDataDto>> GetStrategyAsync(string symbol, string interval, int count, string? source, IReadOnlyList<string>? strategies, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotNewsDataDto>> GetNewsAsync(string symbol, string level, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotSearchDataDto>> SearchAsync(string query, bool trustedOnly, string? taskId, CancellationToken cancellationToken = default);
}

public sealed class StockCopilotMcpService : IStockCopilotMcpService
{
    private const string Version = "v1";
    private readonly IStockDataService _dataService;
    private readonly IQueryLocalFactDatabaseTool _queryLocalFactDatabaseTool;
    private readonly IStockMarketContextService _marketContextService;
    private readonly IStockAgentFeatureEngineeringService _featureEngineeringService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly StockCopilotSearchOptions _searchOptions;

    public StockCopilotMcpService(
        IStockDataService dataService,
        IQueryLocalFactDatabaseTool queryLocalFactDatabaseTool,
        IStockMarketContextService marketContextService,
        IStockAgentFeatureEngineeringService featureEngineeringService,
        IHttpClientFactory httpClientFactory,
        IOptions<StockCopilotSearchOptions> searchOptions)
    {
        _dataService = dataService;
        _queryLocalFactDatabaseTool = queryLocalFactDatabaseTool;
        _marketContextService = marketContextService;
        _featureEngineeringService = featureEngineeringService;
        _httpClientFactory = httpClientFactory;
        _searchOptions = searchOptions.Value;
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotKlineDataDto>> GetKlineAsync(string symbol, string interval, int count, string? source, string? taskId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var safeInterval = string.IsNullOrWhiteSpace(interval) ? "day" : interval.Trim();
        var safeCount = Math.Clamp(count, 20, 240);

        var quoteTask = _dataService.GetQuoteAsync(normalizedSymbol, source, cancellationToken);
        var klineTask = _dataService.GetKLineAsync(normalizedSymbol, safeInterval, safeCount, source, cancellationToken);
        var messageTask = _dataService.GetIntradayMessagesAsync(normalizedSymbol, source, cancellationToken);
        var minuteTask = _dataService.GetMinuteLineAsync(normalizedSymbol, source, cancellationToken);
        var newsTask = _queryLocalFactDatabaseTool.QueryAsync(normalizedSymbol, cancellationToken);

        await Task.WhenAll(quoteTask, klineTask, messageTask, minuteTask, newsTask);

        var quote = await quoteTask;
        var kLines = await klineTask;
        var messages = await messageTask;
        var minuteLines = await minuteTask;
        var localFacts = await newsTask;
        var newsPolicy = StockAgentNewsContextPolicy.Apply(messages, DateTime.Now).Policy;
        var prepared = _featureEngineeringService.Prepare(normalizedSymbol, quote, kLines, minuteLines, messages, newsPolicy, StockAgentLocalFactProjection.Create(localFacts), DateTime.Now);

        var latestClose = kLines.LastOrDefault()?.Close ?? quote.Price;
        var support = kLines.TakeLast(20).DefaultIfEmpty().Min(item => item?.Low ?? latestClose);
        var resistance = kLines.TakeLast(20).DefaultIfEmpty().Max(item => item?.High ?? latestClose);
        var trend = prepared.Features.Trend;
        var data = new StockCopilotKlineDataDto(
            normalizedSymbol,
            safeInterval,
            kLines.Count,
            kLines,
            new StockCopilotKeyLevelsDto(
                support,
                resistance,
                trend.Ma5,
                trend.Ma20,
                resistance,
                support),
            trend.TrendState,
            trend.Return5dPercent,
            trend.Return20dPercent,
            trend.AtrPercent,
            trend.BreakoutDistancePercent);

        stopwatch.Stop();
        return BuildEnvelope(
            toolName: "StockKlineMcp",
            policyClass: "local_required",
            taskId: taskId,
            latencyMs: stopwatch.ElapsedMilliseconds,
            data: data,
            evidence: ToEvidence(prepared.LocalFacts.StockNews.Take(4)),
            features: BuildFeatureList(prepared.Features),
            symbol: normalizedSymbol,
            interval: safeInterval,
            query: null,
            marketContext: await _marketContextService.GetLatestAsync(normalizedSymbol, cancellationToken),
            degradedFlags: prepared.Features.DegradedFlags,
            warnings: Array.Empty<string>());
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotMinuteDataDto>> GetMinuteAsync(string symbol, string? source, string? taskId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);

        var quoteTask = _dataService.GetQuoteAsync(normalizedSymbol, source, cancellationToken);
        var klineTask = _dataService.GetKLineAsync(normalizedSymbol, "day", 60, source, cancellationToken);
        var minuteTask = _dataService.GetMinuteLineAsync(normalizedSymbol, source, cancellationToken);
        var messageTask = _dataService.GetIntradayMessagesAsync(normalizedSymbol, source, cancellationToken);
        var newsTask = _queryLocalFactDatabaseTool.QueryAsync(normalizedSymbol, cancellationToken);

        await Task.WhenAll(quoteTask, klineTask, minuteTask, messageTask, newsTask);

        var quote = await quoteTask;
        var kLines = await klineTask;
        var minuteLines = await minuteTask;
        var messages = await messageTask;
        var localFacts = await newsTask;
        var prepared = _featureEngineeringService.Prepare(
            normalizedSymbol,
            quote,
            kLines,
            minuteLines,
            messages,
            StockAgentNewsContextPolicy.Apply(messages, DateTime.Now).Policy,
            StockAgentLocalFactProjection.Create(localFacts),
            DateTime.Now);

        var opening = minuteLines.FirstOrDefault()?.Price;
        var middayAnchor = minuteLines.LastOrDefault(item => item.Time <= new TimeSpan(11, 30, 0))?.Price;
        var close = minuteLines.LastOrDefault()?.Price;
        var high = minuteLines.Count == 0 ? (decimal?)null : minuteLines.Max(item => item.Price);
        var low = minuteLines.Count == 0 ? (decimal?)null : minuteLines.Min(item => item.Price);
        var openingDrive = opening is > 0m && high.HasValue
            ? decimal.Round((high.Value - opening.Value) / opening.Value * 100m, 2)
            : (decimal?)null;
        var afternoonDrift = middayAnchor is > 0m && close is > 0m
            ? decimal.Round((close.Value - middayAnchor.Value) / middayAnchor.Value * 100m, 2)
            : (decimal?)null;
        var rangePercent = low is > 0m && high.HasValue
            ? decimal.Round((high.Value - low.Value) / low.Value * 100m, 2)
            : (decimal?)null;

        var data = new StockCopilotMinuteDataDto(
            normalizedSymbol,
            prepared.Features.Trend.SessionPhase,
            minuteLines.Count,
            minuteLines,
            prepared.Features.Trend.Vwap > 0m ? prepared.Features.Trend.Vwap : (decimal?)null,
            openingDrive,
            afternoonDrift,
            rangePercent);

        stopwatch.Stop();
        return BuildEnvelope(
            toolName: "StockMinuteMcp",
            policyClass: "local_required",
            taskId: taskId,
            latencyMs: stopwatch.ElapsedMilliseconds,
            data: data,
            evidence: ToEvidence(prepared.LocalFacts.StockNews.Take(3)),
            features: BuildFeatureList(prepared.Features),
            symbol: normalizedSymbol,
            interval: "minute",
            query: null,
            marketContext: await _marketContextService.GetLatestAsync(normalizedSymbol, cancellationToken),
            degradedFlags: prepared.Features.DegradedFlags,
            warnings: Array.Empty<string>());
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotStrategyDataDto>> GetStrategyAsync(string symbol, string interval, int count, string? source, IReadOnlyList<string>? strategies, string? taskId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var safeInterval = string.IsNullOrWhiteSpace(interval) ? "day" : interval.Trim();
        var safeCount = Math.Clamp(count, 30, 180);

        var quoteTask = _dataService.GetQuoteAsync(normalizedSymbol, source, cancellationToken);
        var klineTask = _dataService.GetKLineAsync(normalizedSymbol, safeInterval, safeCount, source, cancellationToken);
        var minuteTask = _dataService.GetMinuteLineAsync(normalizedSymbol, source, cancellationToken);
        var messageTask = _dataService.GetIntradayMessagesAsync(normalizedSymbol, source, cancellationToken);
        var newsTask = _queryLocalFactDatabaseTool.QueryAsync(normalizedSymbol, cancellationToken);

        await Task.WhenAll(quoteTask, klineTask, minuteTask, messageTask, newsTask);

        var quote = await quoteTask;
        var kLines = await klineTask;
        var minuteLines = await minuteTask;
        var messages = await messageTask;
        var localFacts = await newsTask;
        var prepared = _featureEngineeringService.Prepare(
            normalizedSymbol,
            quote,
            kLines,
            minuteLines,
            messages,
            StockAgentNewsContextPolicy.Apply(messages, DateTime.Now).Policy,
            StockAgentLocalFactProjection.Create(localFacts),
            DateTime.Now);

        var requested = NormalizeStrategies(strategies);
        var signals = BuildStrategySignals(kLines, minuteLines, requested, safeInterval);

        stopwatch.Stop();
        return BuildEnvelope(
            toolName: "StockStrategyMcp",
            policyClass: "local_required",
            taskId: taskId,
            latencyMs: stopwatch.ElapsedMilliseconds,
            data: new StockCopilotStrategyDataDto(normalizedSymbol, safeInterval, requested, signals),
            evidence: ToEvidence(prepared.LocalFacts.StockNews.Take(2).Concat(prepared.LocalFacts.SectorReports.Take(2))),
            features: BuildFeatureList(prepared.Features),
            symbol: normalizedSymbol,
            interval: safeInterval,
            query: null,
            marketContext: await _marketContextService.GetLatestAsync(normalizedSymbol, cancellationToken),
            degradedFlags: prepared.Features.DegradedFlags,
            warnings: Array.Empty<string>());
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotNewsDataDto>> GetNewsAsync(string symbol, string level, string? taskId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedLevel = string.IsNullOrWhiteSpace(level) ? "stock" : level.Trim().ToLowerInvariant();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);

        LocalNewsBucketDto bucket = normalizedLevel == "market"
            ? await _queryLocalFactDatabaseTool.QueryMarketAsync(cancellationToken)
            : await _queryLocalFactDatabaseTool.QueryLevelAsync(normalizedSymbol, normalizedLevel, cancellationToken);

        var evidence = bucket.Items.Select(ToEvidence).ToArray();
        var warnings = Array.Empty<string>();
        var degradedFlags = evidence.Length == 0 ? new[] { "no_local_news_evidence" } : Array.Empty<string>();
        stopwatch.Stop();

        return BuildEnvelope(
            toolName: "StockNewsMcp",
            policyClass: "local_required",
            taskId: taskId,
            latencyMs: stopwatch.ElapsedMilliseconds,
            data: new StockCopilotNewsDataDto(bucket.Symbol, bucket.Level, bucket.Items.Count, bucket.Items.OrderByDescending(item => item.PublishTime).FirstOrDefault()?.PublishTime),
            evidence: evidence,
            features: BuildNewsFeatureList(bucket),
            symbol: bucket.Symbol,
            interval: null,
            query: normalizedLevel,
            marketContext: normalizedLevel == "market" ? null : await _marketContextService.GetLatestAsync(normalizedSymbol, cancellationToken),
            degradedFlags: degradedFlags,
            warnings: warnings);
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotSearchDataDto>> SearchAsync(string query, bool trustedOnly, string? taskId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var warnings = new List<string>();
        var degradedFlags = new List<string>();
        var results = new List<StockCopilotSearchResultDto>();
        var evidence = new List<StockCopilotMcpEvidenceDto>();

        if (!_searchOptions.Enabled || string.IsNullOrWhiteSpace(_searchOptions.ApiKey))
        {
            warnings.Add("外部搜索未启用，StockSearchMcp 当前只返回空结果。请配置 Tavily API Key。");
            degradedFlags.Add("external_search_unavailable");
        }
        else
        {
            var httpClient = _httpClientFactory.CreateClient();
            using var response = await httpClient.PostAsJsonAsync(
                _searchOptions.BaseUrl.TrimEnd('/') + "/search",
                new
                {
                    api_key = _searchOptions.ApiKey,
                    query,
                    max_results = trustedOnly ? 5 : 8,
                    search_depth = "advanced",
                    include_answer = false,
                    include_raw_content = false
                },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                warnings.Add($"外部搜索返回失败状态: {(int)response.StatusCode}");
                degradedFlags.Add("external_search_failed");
            }
            else
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                if (doc.RootElement.TryGetProperty("results", out var node) && node.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in node.EnumerateArray())
                    {
                        var title = ReadString(item, "title") ?? string.Empty;
                        var url = ReadString(item, "url") ?? string.Empty;
                        var source = ReadString(item, "source") ?? ExtractHost(url) ?? "external";
                        var content = ReadString(item, "content");
                        var publishedAt = DateTime.TryParse(ReadString(item, "published_date"), out var parsed) ? parsed : (DateTime?)null;
                        var score = ReadDecimal(item, "score");
                        results.Add(new StockCopilotSearchResultDto(title, url, source, score, publishedAt, content));
                        evidence.Add(new StockCopilotMcpEvidenceDto(
                            title,
                            title,
                            source,
                            publishedAt,
                            null,
                            url,
                            content,
                            content,
                            "url_fetched",
                            string.IsNullOrWhiteSpace(content) ? "metadata_only" : "summary_only",
                            DateTime.UtcNow,
                            null,
                            null,
                            "external",
                            null,
                            null,
                            Array.Empty<string>()));
                    }
                }
            }
        }

        stopwatch.Stop();
        return BuildEnvelope(
            toolName: "StockSearchMcp",
            policyClass: "external_gated",
            taskId: taskId,
            latencyMs: stopwatch.ElapsedMilliseconds,
            data: new StockCopilotSearchDataDto(query, _searchOptions.Provider, trustedOnly, results.Count, results),
            evidence: evidence,
            features: new[]
            {
                new StockCopilotMcpFeatureDto("trustedOnly", "Trusted Sources Only", "text", null, trustedOnly ? "true" : "false", null, "Governor gate for external search."),
                new StockCopilotMcpFeatureDto("provider", "Search Provider", "text", null, _searchOptions.Provider, null, "Current external fallback provider.")
            },
            symbol: null,
            interval: null,
            query: query,
            marketContext: null,
            degradedFlags: degradedFlags,
            warnings: warnings);
    }

    private static IReadOnlyList<string> NormalizeStrategies(IReadOnlyList<string>? strategies)
    {
        var normalized = strategies is null || strategies.Count == 0
            ? new[] { "ma", "macd", "rsi", "kdj", "vwap", "td", "breakout", "gap" }
            : strategies
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        return normalized;
    }

    private static IReadOnlyList<StockCopilotStrategySignalDto> BuildStrategySignals(IReadOnlyList<KLinePointDto> kLines, IReadOnlyList<MinuteLinePointDto> minuteLines, IReadOnlyList<string> strategies, string interval)
    {
        var signals = new List<StockCopilotStrategySignalDto>();
        var closes = kLines.Select(item => item.Close).ToArray();
        if (closes.Length == 0)
        {
            return signals;
        }

        if (strategies.Contains("ma"))
        {
            var ma5 = Average(closes.TakeLast(5));
            var ma10 = Average(closes.TakeLast(10));
            var state = ma5 > ma10 ? "golden" : ma5 < ma10 ? "death" : "flat";
            signals.Add(new StockCopilotStrategySignalDto("ma", interval, state, decimal.Round(ma5 - ma10, 4), state, $"MA5={ma5:0.00}, MA10={ma10:0.00}"));
        }

        if (strategies.Contains("macd"))
        {
            var macd = CalculateMacd(closes);
            signals.Add(new StockCopilotStrategySignalDto("macd", interval, macd.Signal, macd.Diff - macd.Dea, macd.Signal, $"DIFF={macd.Diff:0.000}, DEA={macd.Dea:0.000}"));
        }

        if (strategies.Contains("rsi"))
        {
            var rsi6 = CalculateRsi(closes, 6);
            var state = rsi6 >= 70m ? "overbought" : rsi6 <= 30m ? "oversold" : "neutral";
            signals.Add(new StockCopilotStrategySignalDto("rsi", interval, state, rsi6, state, $"RSI6={rsi6:0.00}"));
        }

        if (strategies.Contains("kdj"))
        {
            var kdj = CalculateKdj(kLines);
            var state = kdj.K > kdj.D ? "golden" : kdj.K < kdj.D ? "death" : "flat";
            signals.Add(new StockCopilotStrategySignalDto("kdj", interval, state, kdj.J, state, $"K={kdj.K:0.00}, D={kdj.D:0.00}, J={kdj.J:0.00}"));
        }

        if (strategies.Contains("vwap") && minuteLines.Count > 0)
        {
            var vwap = CalculateVwap(minuteLines);
            var lastPrice = minuteLines[^1].Price;
            var state = lastPrice >= vwap ? "strength" : "weakness";
            signals.Add(new StockCopilotStrategySignalDto("vwap", "minute", state, decimal.Round(lastPrice - vwap, 4), state, $"Last={lastPrice:0.00}, VWAP={vwap:0.00}"));
        }

        if (strategies.Contains("td"))
        {
            var td = CalculateTdSequential(closes);
            signals.Add(new StockCopilotStrategySignalDto("td", interval, td.State, td.Count, td.State, $"TD setup count={td.Count:0}"));
        }

        if (strategies.Contains("breakout") && kLines.Count >= 20)
        {
            var recentHigh = kLines.TakeLast(20).Max(item => item.High);
            var lastClose = kLines[^1].Close;
            var signal = lastClose >= recentHigh ? "breakout" : "inside_range";
            signals.Add(new StockCopilotStrategySignalDto("breakout", interval, signal, decimal.Round(recentHigh - lastClose, 4), signal, $"20日高点={recentHigh:0.00}, 收盘={lastClose:0.00}"));
        }

        if (strategies.Contains("gap") && kLines.Count >= 2)
        {
            var previous = kLines[^2];
            var current = kLines[^1];
            var signal = current.Low > previous.High ? "gap_up" : current.High < previous.Low ? "gap_down" : "none";
            signals.Add(new StockCopilotStrategySignalDto("gap", interval, signal, signal == "gap_up" ? current.Low - previous.High : signal == "gap_down" ? previous.Low - current.High : 0m, signal, "跳空缺口检测"));
        }

        return signals;
    }

    private StockCopilotMcpEnvelopeDto<T> BuildEnvelope<T>(
        string toolName,
        string policyClass,
        string? taskId,
        long latencyMs,
        T data,
        IReadOnlyList<StockCopilotMcpEvidenceDto> evidence,
        IReadOnlyList<StockCopilotMcpFeatureDto> features,
        string? symbol,
        string? interval,
        string? query,
        SimplerJiangAiAgent.Api.Modules.Market.Models.StockMarketContextDto? marketContext,
        IReadOnlyList<string> degradedFlags,
        IReadOnlyList<string> warnings)
    {
        return new StockCopilotMcpEnvelopeDto<T>(
            Guid.NewGuid().ToString("N"),
            string.IsNullOrWhiteSpace(taskId) ? Guid.NewGuid().ToString("N") : taskId.Trim(),
            toolName,
            latencyMs,
            new StockCopilotMcpCacheDto(false, "live", DateTime.UtcNow),
            warnings,
            degradedFlags,
            data,
            evidence,
            features,
            new StockCopilotMcpMetaDto(Version, policyClass, toolName, symbol, interval, query, marketContext));
    }

    private static IReadOnlyList<StockCopilotMcpFeatureDto> BuildFeatureList(StockAgentDeterministicFeaturesDto features)
    {
        return new[]
        {
            new StockCopilotMcpFeatureDto("coverageScore", "Evidence Coverage", "number", features.Evidence.CoverageScore, null, null, "Higher means the agent has better recent traceable evidence coverage."),
            new StockCopilotMcpFeatureDto("conflictScore", "Evidence Conflict", "number", features.Evidence.ConflictScore, null, null, "Higher means bullish and bearish evidence are fighting each other."),
            new StockCopilotMcpFeatureDto("freshnessHours", "Evidence Freshness", "number", features.Evidence.FreshnessHours, null, "h", "Hours since the latest retained evidence was published."),
            new StockCopilotMcpFeatureDto("trendState", "Trend State", "text", null, features.Trend.TrendState, null, "Deterministic trend-state computed from MA and return windows."),
            new StockCopilotMcpFeatureDto("atrPercent", "ATR Percent", "number", features.Trend.AtrPercent, null, "%", "Rolling ATR percent of price."),
            new StockCopilotMcpFeatureDto("peBand", "PE Band", "text", null, features.Valuation.PeBand, null, "Valuation bucket used by commander guardrails."),
            new StockCopilotMcpFeatureDto("volatilityScore", "Volatility Score", "number", features.Risk.VolatilityScore, null, null, "Risk score synthesized from ATR and turnover.")
        };
    }

    private static IReadOnlyList<StockCopilotMcpFeatureDto> BuildNewsFeatureList(LocalNewsBucketDto bucket)
    {
        var items = bucket.Items;
        return new[]
        {
            new StockCopilotMcpFeatureDto("itemCount", "News Count", "number", items.Count, null, null, "Number of retained local evidence items for this level."),
            new StockCopilotMcpFeatureDto("latestPublishedAt", "Latest Publish Time", "text", null, items.OrderByDescending(item => item.PublishTime).FirstOrDefault()?.PublishTime.ToString("yyyy-MM-dd HH:mm:ss"), null, "Latest published time across retained local evidence."),
            new StockCopilotMcpFeatureDto("sectorName", "Sector Name", "text", null, bucket.SectorName, null, "Sector inferred by local fact query."),
        };
    }

    private static IReadOnlyList<StockCopilotMcpEvidenceDto> ToEvidence(IEnumerable<StockAgentLocalNewsItemDto> items)
    {
        return items.Select(item => new StockCopilotMcpEvidenceDto(
            item.Title,
            item.TranslatedTitle ?? item.Title,
            item.Source,
            item.PublishTime,
            item.CrawledAt,
            item.Url,
            item.Excerpt,
            item.Summary,
            item.ReadMode,
            item.ReadStatus,
            item.IngestedAt,
            item.LocalFactId,
            item.SourceRecordId,
            item.Category,
            item.Sentiment,
            item.AiTarget,
            item.AiTags)).ToArray();
    }

    private static StockCopilotMcpEvidenceDto ToEvidence(LocalNewsItemDto item)
    {
        return new StockCopilotMcpEvidenceDto(
            item.Title,
            item.TranslatedTitle ?? item.Title,
            item.Source,
            item.PublishTime,
            item.CrawledAt,
            item.Url,
            item.Excerpt,
            item.Summary,
            item.ReadMode,
            item.ReadStatus,
            item.IngestedAt,
            item.LocalFactId,
            item.SourceRecordId,
            item.Category,
            item.Sentiment,
            item.AiTarget,
            item.AiTags);
    }

    private static decimal Average(IEnumerable<decimal> values)
    {
        var items = values.ToArray();
        return items.Length == 0 ? 0m : items.Average();
    }

    private static (decimal Diff, decimal Dea, string Signal) CalculateMacd(IReadOnlyList<decimal> closes)
    {
        if (closes.Count == 0)
        {
            return (0m, 0m, "flat");
        }

        var ema12 = CalculateEma(closes, 12);
        var ema26 = CalculateEma(closes, 26);
        var diffSeries = ema12.Zip(ema26, (fast, slow) => fast - slow).ToArray();
        var deaSeries = CalculateEma(diffSeries, 9);
        var diff = diffSeries[^1];
        var dea = deaSeries[^1];
        var signal = diff > dea ? "golden" : diff < dea ? "death" : "flat";
        return (decimal.Round(diff, 4), decimal.Round(dea, 4), signal);
    }

    private static decimal CalculateRsi(IReadOnlyList<decimal> closes, int period)
    {
        if (closes.Count <= period)
        {
            return 50m;
        }

        decimal gains = 0m;
        decimal losses = 0m;
        for (var index = closes.Count - period; index < closes.Count; index++)
        {
            if (index == 0)
            {
                continue;
            }

            var change = closes[index] - closes[index - 1];
            if (change > 0m)
            {
                gains += change;
            }
            else
            {
                losses -= change;
            }
        }

        if (losses == 0m)
        {
            return 100m;
        }

        var rs = gains / losses;
        return decimal.Round(100m - 100m / (1m + rs), 2);
    }

    private static (decimal K, decimal D, decimal J) CalculateKdj(IReadOnlyList<KLinePointDto> kLines)
    {
        if (kLines.Count < 9)
        {
            return (50m, 50m, 50m);
        }

        decimal k = 50m;
        decimal d = 50m;
        foreach (var index in Enumerable.Range(8, kLines.Count - 8))
        {
            var window = kLines.Skip(index - 8).Take(9).ToArray();
            var highest = window.Max(item => item.High);
            var lowest = window.Min(item => item.Low);
            var close = window[^1].Close;
            var rsv = highest == lowest ? 50m : (close - lowest) / (highest - lowest) * 100m;
            k = (2m * k + rsv) / 3m;
            d = (2m * d + k) / 3m;
        }

        var j = 3m * k - 2m * d;
        return (decimal.Round(k, 2), decimal.Round(d, 2), decimal.Round(j, 2));
    }

    private static decimal CalculateVwap(IReadOnlyList<MinuteLinePointDto> minuteLines)
    {
        decimal totalAmount = 0m;
        decimal totalVolume = 0m;
        foreach (var item in minuteLines)
        {
            totalAmount += item.Price * item.Volume;
            totalVolume += item.Volume;
        }

        return totalVolume == 0m ? 0m : decimal.Round(totalAmount / totalVolume, 4);
    }

    private static (decimal Count, string State) CalculateTdSequential(IReadOnlyList<decimal> closes)
    {
        if (closes.Count < 5)
        {
            return (0m, "insufficient");
        }

        var count = 0;
        var bullish = false;
        for (var index = 4; index < closes.Count; index++)
        {
            if (closes[index] > closes[index - 4])
            {
                count = bullish ? count + 1 : 1;
                bullish = true;
            }
            else if (closes[index] < closes[index - 4])
            {
                count = !bullish ? count + 1 : 1;
                bullish = false;
            }
            else
            {
                count = 0;
            }
        }

        var state = bullish ? "setup_up" : "setup_down";
        return (Math.Clamp(count, 0, 9), count == 0 ? "flat" : state);
    }

    private static decimal[] CalculateEma(IReadOnlyList<decimal> values, int period)
    {
        var result = new decimal[values.Count];
        if (values.Count == 0)
        {
            return result;
        }

        var multiplier = 2m / (period + 1m);
        result[0] = values[0];
        for (var index = 1; index < values.Count; index++)
        {
            result[index] = (values[index] - result[index - 1]) * multiplier + result[index - 1];
        }

        return result;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static decimal? ReadDecimal(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        return null;
    }

    private static string? ExtractHost(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;
    }
}