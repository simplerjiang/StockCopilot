using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Market.Services;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IStockCopilotMcpService
{
    Task<StockCopilotMcpEnvelopeDto<StockCopilotCompanyOverviewDataDto>> GetCompanyOverviewAsync(string symbol, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotProductDataDto>> GetProductAsync(string symbol, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotFundamentalsDataDto>> GetFundamentalsAsync(string symbol, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotShareholderDataDto>> GetShareholderAsync(string symbol, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotMarketContextDataDto>> GetMarketContextAsync(string symbol, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotSocialSentimentDataDto>> GetSocialSentimentAsync(string symbol, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotKlineDataDto>> GetKlineAsync(string symbol, string interval, int count, string? source, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotMinuteDataDto>> GetMinuteAsync(string symbol, string? source, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotStrategyDataDto>> GetStrategyAsync(string symbol, string interval, int count, string? source, IReadOnlyList<string>? strategies, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotNewsDataDto>> GetNewsAsync(string symbol, string level, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotSearchDataDto>> SearchAsync(string query, bool trustedOnly, string? taskId, CancellationToken cancellationToken = default);
}

public sealed class StockCopilotMcpService : IStockCopilotMcpService
{
    private const string Version = "v1";
    private static readonly Regex IntegerRegex = new("\\d+", RegexOptions.Compiled);
    private static readonly HashSet<string> ProductFactLabels =
    [
        "主营业务",
        "经营范围",
        "所属行业",
        "证监会行业",
        "所属地区"
    ];
    private readonly IStockDataService _dataService;
    private readonly IQueryLocalFactDatabaseTool _queryLocalFactDatabaseTool;
    private readonly IStockMarketContextService _marketContextService;
    private readonly ISectorRotationQueryService _sectorRotationQueryService;
    private readonly IStockAgentFeatureEngineeringService _featureEngineeringService;
    private readonly IStockFundamentalSnapshotService _fundamentalSnapshotService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly StockCopilotSearchOptions _searchOptions;

    public StockCopilotMcpService(
        IStockDataService dataService,
        IQueryLocalFactDatabaseTool queryLocalFactDatabaseTool,
        IStockMarketContextService marketContextService,
        ISectorRotationQueryService sectorRotationQueryService,
        IStockAgentFeatureEngineeringService featureEngineeringService,
        IStockFundamentalSnapshotService fundamentalSnapshotService,
        IHttpClientFactory httpClientFactory,
        IOptions<StockCopilotSearchOptions> searchOptions)
    {
        _dataService = dataService;
        _queryLocalFactDatabaseTool = queryLocalFactDatabaseTool;
        _marketContextService = marketContextService;
        _sectorRotationQueryService = sectorRotationQueryService;
        _featureEngineeringService = featureEngineeringService;
        _fundamentalSnapshotService = fundamentalSnapshotService;
        _httpClientFactory = httpClientFactory;
        _searchOptions = searchOptions.Value;
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotCompanyOverviewDataDto>> GetCompanyOverviewAsync(string symbol, string? taskId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var quote = await _dataService.GetQuoteAsync(normalizedSymbol, null, cancellationToken);
        var localFacts = await _queryLocalFactDatabaseTool.QueryAsync(normalizedSymbol, cancellationToken);
        var snapshotResolution = await ResolveFundamentalSnapshotAsync(normalizedSymbol, localFacts, cancellationToken);
        var overviewFacts = snapshotResolution.Snapshot?.Facts ?? ConvertFacts(localFacts.FundamentalFacts);
        var mainBusiness = TryResolveFactValue(overviewFacts, "主营业务");
        var businessScope = TryResolveFactValue(overviewFacts, "经营范围");
        var shareholderCount = quote.ShareholderCount
            ?? TryResolveShareholderCount(snapshotResolution.Snapshot?.Facts)
            ?? TryResolveShareholderCount(localFacts.FundamentalFacts);
        var warnings = BuildWarnings(snapshotResolution.Warning);
        var degradedFlags = BuildDegradedFlags(snapshotResolution.DegradedFlag);

        stopwatch.Stop();
        return BuildEnvelope(
            toolName: StockMcpToolNames.CompanyOverview,
            policyClass: "local_required",
            taskId: taskId,
            latencyMs: stopwatch.ElapsedMilliseconds,
            data: new StockCopilotCompanyOverviewDataDto(
                normalizedSymbol,
                FirstNonEmpty(localFacts.Name, quote.Name, normalizedSymbol),
                FirstNonEmpty(localFacts.SectorName, quote.SectorName),
                quote.Price,
                quote.ChangePercent,
                quote.FloatMarketCap > 0m ? quote.FloatMarketCap : null,
                quote.PeRatio > 0m ? quote.PeRatio : null,
                shareholderCount,
                quote.Timestamp,
                snapshotResolution.UpdatedAt,
                overviewFacts.Count,
                mainBusiness,
                businessScope),
            evidence: BuildCompanyOverviewEvidence(normalizedSymbol, quote, localFacts, snapshotResolution.UpdatedAt, shareholderCount, mainBusiness, businessScope),
            features: BuildCompanyOverviewFeatures(quote, localFacts, shareholderCount, overviewFacts.Count, snapshotResolution.UpdatedAt, mainBusiness, businessScope),
            symbol: normalizedSymbol,
            interval: null,
            query: null,
            marketContext: await _marketContextService.GetLatestAsync(normalizedSymbol, cancellationToken),
            degradedFlags: degradedFlags,
            warnings: warnings);
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotProductDataDto>> GetProductAsync(string symbol, string? taskId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var localFacts = await _queryLocalFactDatabaseTool.QueryAsync(normalizedSymbol, cancellationToken);
        var snapshotResolution = await ResolveFundamentalSnapshotAsync(normalizedSymbol, localFacts, cancellationToken);
        var allFacts = snapshotResolution.Snapshot?.Facts ?? ConvertFacts(localFacts.FundamentalFacts);
        var productFacts = FilterProductFacts(allFacts).ToList();
        var mainBusiness = TryResolveFactValue(productFacts, "主营业务");
        var businessScope = TryResolveFactValue(productFacts, "经营范围");
        var industry = TryResolveFactValue(productFacts, "所属行业");
        var csrcIndustry = TryResolveFactValue(productFacts, "证监会行业");
        var region = TryResolveFactValue(productFacts, "所属地区");
        var warnings = BuildWarnings(snapshotResolution.Warning);
        var degradedFlags = BuildDegradedFlags(snapshotResolution.DegradedFlag ?? (string.IsNullOrWhiteSpace(mainBusiness) && string.IsNullOrWhiteSpace(businessScope) ? "no_product_facts" : null));

        stopwatch.Stop();
        return BuildEnvelope(
            toolName: StockMcpToolNames.Product,
            policyClass: "local_required",
            taskId: taskId,
            latencyMs: stopwatch.ElapsedMilliseconds,
            data: new StockCopilotProductDataDto(
                normalizedSymbol,
                snapshotResolution.UpdatedAt,
                mainBusiness,
                businessScope,
                industry,
                csrcIndustry,
                region,
                productFacts.Count,
                BuildSourceSummary(productFacts),
                ConvertProductFacts(productFacts)),
            evidence: BuildProductEvidence(normalizedSymbol, productFacts, snapshotResolution.UpdatedAt, mainBusiness, businessScope, industry, region),
            features: BuildProductFeatures(productFacts.Count, snapshotResolution.UpdatedAt, mainBusiness, businessScope, industry, csrcIndustry, region),
            symbol: normalizedSymbol,
            interval: null,
            query: null,
            marketContext: await _marketContextService.GetLatestAsync(normalizedSymbol, cancellationToken),
            degradedFlags: degradedFlags,
            warnings: warnings);
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotFundamentalsDataDto>> GetFundamentalsAsync(string symbol, string? taskId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var localFacts = await _queryLocalFactDatabaseTool.QueryAsync(normalizedSymbol, cancellationToken);
        var snapshotResolution = await ResolveFundamentalSnapshotAsync(normalizedSymbol, localFacts, cancellationToken);
        var allFacts = snapshotResolution.Snapshot?.Facts ?? ConvertFacts(localFacts.FundamentalFacts);
        var facts = allFacts.Where(item => !IsShareholderFact(item)).ToList();
        var warnings = BuildWarnings(snapshotResolution.Warning);
        var degradedFlags = BuildDegradedFlags(snapshotResolution.DegradedFlag ?? (facts.Count == 0 ? "no_fundamental_facts" : null));

        stopwatch.Stop();
        return BuildEnvelope(
            toolName: StockMcpToolNames.Fundamentals,
            policyClass: "local_required",
            taskId: taskId,
            latencyMs: stopwatch.ElapsedMilliseconds,
            data: new StockCopilotFundamentalsDataDto(normalizedSymbol, snapshotResolution.UpdatedAt, facts.Count, facts),
            evidence: BuildFactEvidence(normalizedSymbol, facts, snapshotResolution.UpdatedAt, "fundamental"),
            features: BuildFundamentalFeatures(facts.Count, snapshotResolution.UpdatedAt),
            symbol: normalizedSymbol,
            interval: null,
            query: null,
            marketContext: await _marketContextService.GetLatestAsync(normalizedSymbol, cancellationToken),
            degradedFlags: degradedFlags,
            warnings: warnings);
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotShareholderDataDto>> GetShareholderAsync(string symbol, string? taskId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var quote = await _dataService.GetQuoteAsync(normalizedSymbol, null, cancellationToken);
        var localFacts = await _queryLocalFactDatabaseTool.QueryAsync(normalizedSymbol, cancellationToken);
        var snapshotResolution = await ResolveFundamentalSnapshotAsync(normalizedSymbol, localFacts, cancellationToken);
        var allFacts = snapshotResolution.Snapshot?.Facts ?? ConvertFacts(localFacts.FundamentalFacts);
        var shareholderFacts = FilterShareholderFacts(allFacts).ToList();
        var shareholderCount = quote.ShareholderCount
            ?? TryResolveShareholderCount(allFacts)
            ?? TryResolveShareholderCount(localFacts.FundamentalFacts);

        if (shareholderCount.HasValue && shareholderFacts.All(item => !string.Equals(item.Label, "股东户数", StringComparison.OrdinalIgnoreCase)))
        {
            shareholderFacts.Insert(0, new StockFundamentalFactDto("股东户数", shareholderCount.Value.ToString(), "公司画像缓存"));
        }

        var warnings = BuildWarnings(snapshotResolution.Warning);
        var degradedFlags = BuildDegradedFlags(snapshotResolution.DegradedFlag ?? (!shareholderCount.HasValue && shareholderFacts.Count == 0 ? "no_shareholder_facts" : null));

        stopwatch.Stop();
        return BuildEnvelope(
            toolName: StockMcpToolNames.Shareholder,
            policyClass: "local_required",
            taskId: taskId,
            latencyMs: stopwatch.ElapsedMilliseconds,
            data: new StockCopilotShareholderDataDto(normalizedSymbol, shareholderCount, snapshotResolution.UpdatedAt, shareholderFacts.Count, shareholderFacts),
            evidence: BuildFactEvidence(normalizedSymbol, shareholderFacts, snapshotResolution.UpdatedAt, "shareholder"),
            features: BuildShareholderFeatures(shareholderCount, shareholderFacts.Count, snapshotResolution.UpdatedAt),
            symbol: normalizedSymbol,
            interval: null,
            query: null,
            marketContext: await _marketContextService.GetLatestAsync(normalizedSymbol, cancellationToken),
            degradedFlags: degradedFlags,
            warnings: warnings);
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotMarketContextDataDto>> GetMarketContextAsync(string symbol, string? taskId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var marketContext = await _marketContextService.GetLatestAsync(normalizedSymbol, cancellationToken);
        var warnings = new List<string>();
        var degradedFlags = new List<string>();

        if (marketContext is null)
        {
            warnings.Add("MarketContextMcp 当前未获取到本地市场上下文。请先确认市场情绪快照与板块轮动数据已入库。");
            degradedFlags.Add("no_market_context_data");
        }

        stopwatch.Stop();
        return BuildEnvelope(
            toolName: StockMcpToolNames.MarketContext,
            policyClass: "local_required",
            taskId: taskId,
            latencyMs: stopwatch.ElapsedMilliseconds,
            data: new StockCopilotMarketContextDataDto(
                normalizedSymbol,
                marketContext is not null,
                marketContext?.StageLabel,
                marketContext?.StageConfidence,
                marketContext?.StockSectorName,
                marketContext?.MainlineSectorName,
                marketContext?.SectorCode,
                marketContext?.MainlineScore,
                marketContext?.SuggestedPositionScale,
                marketContext?.ExecutionFrequencyLabel,
                marketContext?.CounterTrendWarning ?? false,
                marketContext?.IsMainlineAligned ?? false),
            evidence: BuildMarketContextEvidence(normalizedSymbol, marketContext),
            features: BuildMarketContextFeatures(marketContext),
            symbol: normalizedSymbol,
            interval: null,
            query: null,
            marketContext: marketContext,
            degradedFlags: degradedFlags,
            warnings: warnings);
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotSocialSentimentDataDto>> GetSocialSentimentAsync(string symbol, string? taskId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var localFacts = await _queryLocalFactDatabaseTool.QueryAsync(normalizedSymbol, cancellationToken);
        var marketSummary = await _sectorRotationQueryService.GetLatestSummaryAsync(cancellationToken);
        var marketContext = await _marketContextService.GetLatestAsync(normalizedSymbol, cancellationToken);

        var stockNews = BuildSentimentCount(localFacts.StockNews);
        var sectorReports = BuildSentimentCount(localFacts.SectorReports);
        var marketReports = BuildSentimentCount(localFacts.MarketReports);
        var localEvidenceCount = stockNews.TotalCount + sectorReports.TotalCount + marketReports.TotalCount;
        var marketProxy = marketSummary is null ? null : BuildMarketProxy(marketSummary);
        var evidence = localFacts.StockNews.Take(4)
            .Concat(localFacts.SectorReports.Take(3))
            .Concat(localFacts.MarketReports.Take(3))
            .Select(ToEvidence)
            .ToList();

        if (marketSummary is not null)
        {
            evidence.Add(BuildMarketSummaryEvidence(marketSummary));
        }

        var warnings = new List<string>();
        var degradedFlags = new List<string>();
        var blocked = false;
        var blockedReason = default(string?);
        string status;
        var approximationMode = "none";

        if (localEvidenceCount == 0 && marketProxy is null)
        {
            status = "blocked";
            blocked = true;
            blockedReason = "no_data";
            degradedFlags.Add("blocked.no_data");
            warnings.Add("SocialSentimentMcp 当前既没有本地新闻情绪，也没有市场代理情绪，无法形成最低可用降级契约。");
        }
        else
        {
            status = "degraded";
            degradedFlags.Add("no_live_social_source");
            if (localEvidenceCount > 0 && marketProxy is not null)
            {
                approximationMode = "local_news_and_market_proxy";
                degradedFlags.Add("degraded.local_news_and_market_proxy");
            }
            else if (localEvidenceCount > 0)
            {
                approximationMode = "local_news_only";
                degradedFlags.Add("degraded.local_news_only");
            }
            else
            {
                approximationMode = "market_proxy_only";
                degradedFlags.Add("degraded.market_proxy_only");
            }

            warnings.Add("SocialSentimentMcp v1 仅基于本地新闻情绪与市场代理情绪，不代表真实社媒情绪覆盖。");
        }

        if (blocked)
        {
            approximationMode = "none";
        }

        var latestEvidenceAt = evidence
            .Select(item => item.PublishedAt ?? item.CrawledAt ?? item.IngestedAt)
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .DefaultIfEmpty()
            .Max();

        stopwatch.Stop();
        return BuildEnvelope(
            toolName: StockMcpToolNames.SocialSentiment,
            policyClass: "local_required",
            taskId: taskId,
            latencyMs: stopwatch.ElapsedMilliseconds,
            data: new StockCopilotSocialSentimentDataDto(
                normalizedSymbol,
                status,
                blocked,
                blockedReason,
                approximationMode,
                blocked ? null : ResolveSocialOverallSentiment(stockNews, sectorReports, marketReports, marketProxy),
                evidence.Count,
                latestEvidenceAt == default ? null : latestEvidenceAt,
                stockNews,
                sectorReports,
                marketReports,
                marketProxy),
            evidence: evidence,
            features: BuildSocialSentimentFeatures(stockNews, sectorReports, marketReports, marketProxy, blocked, approximationMode),
            symbol: normalizedSymbol,
            interval: null,
            query: null,
            marketContext: marketContext,
            degradedFlags: degradedFlags,
            warnings: warnings);
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotKlineDataDto>> GetKlineAsync(string symbol, string interval, int count, string? source, string? taskId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var safeInterval = string.IsNullOrWhiteSpace(interval) ? "day" : interval.Trim();
        var safeCount = Math.Clamp(count, 20, 240);

        var quote = await _dataService.GetQuoteAsync(normalizedSymbol, source, cancellationToken);
        var kLines = await _dataService.GetKLineAsync(normalizedSymbol, safeInterval, safeCount, source, cancellationToken);
        var messages = await _dataService.GetIntradayMessagesAsync(normalizedSymbol, source, cancellationToken);
        var minuteLines = await _dataService.GetMinuteLineAsync(normalizedSymbol, source, cancellationToken);
        var localFacts = await _queryLocalFactDatabaseTool.QueryAsync(normalizedSymbol, cancellationToken);
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

        var quote = await _dataService.GetQuoteAsync(normalizedSymbol, source, cancellationToken);
        var kLines = await _dataService.GetKLineAsync(normalizedSymbol, "day", 60, source, cancellationToken);
        var minuteLines = await _dataService.GetMinuteLineAsync(normalizedSymbol, source, cancellationToken);
        var messages = await _dataService.GetIntradayMessagesAsync(normalizedSymbol, source, cancellationToken);
        var localFacts = await _queryLocalFactDatabaseTool.QueryAsync(normalizedSymbol, cancellationToken);
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

        var quote = await _dataService.GetQuoteAsync(normalizedSymbol, source, cancellationToken);
        var kLines = await _dataService.GetKLineAsync(normalizedSymbol, safeInterval, safeCount, source, cancellationToken);
        var minuteLines = await _dataService.GetMinuteLineAsync(normalizedSymbol, source, cancellationToken);
        var messages = await _dataService.GetIntradayMessagesAsync(normalizedSymbol, source, cancellationToken);
        var localFacts = await _queryLocalFactDatabaseTool.QueryAsync(normalizedSymbol, cancellationToken);
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
                        var readableSnippet = LocalFactDisplayPolicy.SanitizeEvidenceSnippet(content, title);
                        evidence.Add(new StockCopilotMcpEvidenceDto(
                            title,
                            title,
                            source,
                            publishedAt,
                            null,
                            url,
                            readableSnippet,
                            readableSnippet,
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
        var cache = new StockCopilotMcpCacheDto(false, "live", DateTime.UtcNow);
        return new StockCopilotMcpEnvelopeDto<T>(
            Guid.NewGuid().ToString("N"),
            string.IsNullOrWhiteSpace(taskId) ? Guid.NewGuid().ToString("N") : taskId.Trim(),
            toolName,
            latencyMs,
            cache,
            warnings,
            degradedFlags,
            data,
            evidence,
            features,
            new StockCopilotMcpMetaDto(Version, policyClass, toolName, symbol, interval, query, marketContext),
            ResolveErrorCode(degradedFlags, warnings),
            ResolveFreshnessTag(evidence, cache.GeneratedAt),
            ResolveSourceTier(policyClass, evidence, cache.Source),
            cache.Hit,
            policyClass);
    }

    private static string? ResolveErrorCode(IReadOnlyList<string> degradedFlags, IReadOnlyList<string> warnings)
    {
        if (degradedFlags.Count > 0)
        {
            return degradedFlags[0];
        }

        if (warnings.Count > 0)
        {
            return "tool.warning_present";
        }

        return null;
    }

    private static string ResolveFreshnessTag(IReadOnlyList<StockCopilotMcpEvidenceDto> evidence, DateTime generatedAt)
    {
        if (evidence.Count == 0)
        {
            return "no_data";
        }

        var latestTimestamp = evidence
            .Select(item => item.PublishedAt ?? item.CrawledAt ?? item.IngestedAt)
            .Where(item => item.HasValue)
            .Select(item => item!.Value.ToUniversalTime())
            .DefaultIfEmpty(generatedAt)
            .Max();

        var age = generatedAt - latestTimestamp;
        if (age <= TimeSpan.FromHours(6))
        {
            return "fresh";
        }

        if (age <= TimeSpan.FromHours(72))
        {
            return "recent";
        }

        return "stale";
    }

    private static string ResolveSourceTier(string policyClass, IReadOnlyList<StockCopilotMcpEvidenceDto> evidence, string cacheSource)
    {
        if (string.Equals(policyClass, "external_gated", StringComparison.OrdinalIgnoreCase))
        {
            return "external";
        }

        if (evidence.Any(item => item.LocalFactId.HasValue || !string.IsNullOrWhiteSpace(item.SourceRecordId)))
        {
            return "local";
        }

        return string.Equals(cacheSource, "live", StringComparison.OrdinalIgnoreCase) ? "live" : "cache";
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

    private static IReadOnlyList<StockCopilotMcpEvidenceDto> BuildMarketContextEvidence(string symbol, StockMarketContextDto? marketContext)
    {
        if (marketContext is null)
        {
            return Array.Empty<StockCopilotMcpEvidenceDto>();
        }

        return new[]
        {
            new StockCopilotMcpEvidenceDto(
                $"市场阶段={marketContext.StageLabel}",
                "市场阶段",
                "IStockMarketContextService",
                null,
                null,
                null,
                $"阶段={marketContext.StageLabel}，置信度={marketContext.StageConfidence:0.##}。",
                $"阶段={marketContext.StageLabel}，置信度={marketContext.StageConfidence:0.##}。",
                "local_fact",
                "summary_only",
                null,
                null,
                $"market_context:{symbol}:stage",
                "market_context",
                null,
                symbol,
                Array.Empty<string>()),
            new StockCopilotMcpEvidenceDto(
                $"主线={marketContext.MainlineSectorName ?? "无"}",
                "板块对齐",
                "IStockMarketContextService",
                null,
                null,
                null,
                $"个股行业={marketContext.StockSectorName ?? "未知"}，主线={marketContext.MainlineSectorName ?? "无"}，主线对齐={(marketContext.IsMainlineAligned ? "是" : "否")}。",
                $"个股行业={marketContext.StockSectorName ?? "未知"}，主线={marketContext.MainlineSectorName ?? "无"}，主线对齐={(marketContext.IsMainlineAligned ? "是" : "否")}。",
                "local_fact",
                "summary_only",
                null,
                null,
                $"market_context:{symbol}:alignment",
                "market_context",
                null,
                symbol,
                Array.Empty<string>())
        };
    }

    private static IReadOnlyList<StockCopilotMcpFeatureDto> BuildMarketContextFeatures(StockMarketContextDto? marketContext)
    {
        if (marketContext is null)
        {
            return new[]
            {
                new StockCopilotMcpFeatureDto("available", "Context Available", "text", null, "false", null, "Whether a local market-context snapshot was available.")
            };
        }

        return new[]
        {
            new StockCopilotMcpFeatureDto("stageConfidence", "Stage Confidence", "number", marketContext.StageConfidence, null, null, "Confidence attached to the current market stage."),
            new StockCopilotMcpFeatureDto("suggestedPositionScale", "Suggested Position Scale", "number", marketContext.SuggestedPositionScale, null, null, "Position sizing multiplier derived from the market context."),
            new StockCopilotMcpFeatureDto("isMainlineAligned", "Mainline Aligned", "text", null, marketContext.IsMainlineAligned ? "true" : "false", null, "Whether the stock's sector is aligned with the current mainline.")
        };
    }

    private static IReadOnlyList<StockCopilotMcpFeatureDto> BuildSocialSentimentFeatures(
        StockCopilotSentimentCountDto stockNews,
        StockCopilotSentimentCountDto sectorReports,
        StockCopilotSentimentCountDto marketReports,
        StockCopilotSocialSentimentMarketProxyDto? marketProxy,
        bool blocked,
        string approximationMode)
    {
        return new[]
        {
            new StockCopilotMcpFeatureDto("status", "Contract Status", "text", null, blocked ? "blocked" : "degraded", null, "SocialSentimentMcp v1 is either blocked or degraded because real social sources are not wired yet."),
            new StockCopilotMcpFeatureDto("approximationMode", "Approximation Mode", "text", null, approximationMode, null, "Explains whether the contract is driven by local news, market proxy, or both."),
            new StockCopilotMcpFeatureDto("stockNewsCount", "Stock News Count", "number", stockNews.TotalCount, null, null, "Count of local stock-news items included in the approximation."),
            new StockCopilotMcpFeatureDto("sectorReportCount", "Sector Report Count", "number", sectorReports.TotalCount, null, null, "Count of local sector-report items included in the approximation."),
            new StockCopilotMcpFeatureDto("marketReportCount", "Market Report Count", "number", marketReports.TotalCount, null, null, "Count of local market-report items included in the approximation."),
            new StockCopilotMcpFeatureDto("marketProxyAvailable", "Market Proxy Available", "text", null, marketProxy is null ? "false" : "true", null, "Whether a market sentiment snapshot was available as a proxy signal.")
        };
    }

    private static StockCopilotSentimentCountDto BuildSentimentCount(IEnumerable<LocalNewsItemDto> items)
    {
        var rows = items.ToArray();
        return new StockCopilotSentimentCountDto(
            rows.Count(item => string.Equals(item.Sentiment, "利好", StringComparison.OrdinalIgnoreCase)),
            rows.Count(item => string.Equals(item.Sentiment, "中性", StringComparison.OrdinalIgnoreCase)),
            rows.Count(item => string.Equals(item.Sentiment, "利空", StringComparison.OrdinalIgnoreCase)),
            rows.Length,
            rows.OrderByDescending(item => item.PublishTime).FirstOrDefault()?.PublishTime);
    }

    private static StockCopilotSocialSentimentMarketProxyDto BuildMarketProxy(MarketSentimentSummaryDto summary)
    {
        var stageLabel = string.IsNullOrWhiteSpace(summary.StageLabelV2)
            ? summary.StageLabel
            : summary.StageLabelV2;
        return new StockCopilotSocialSentimentMarketProxyDto(
            stageLabel,
            summary.StageConfidence > 0 ? summary.StageConfidence : summary.StageScore,
            MapStageToSentiment(stageLabel),
            summary.SnapshotTime);
    }

    private static StockCopilotMcpEvidenceDto BuildMarketSummaryEvidence(MarketSentimentSummaryDto summary)
    {
        var stageLabel = string.IsNullOrWhiteSpace(summary.StageLabelV2)
            ? summary.StageLabel
            : summary.StageLabelV2;
        var sentiment = MapStageToSentiment(stageLabel);
        var summaryText = $"市场阶段={stageLabel}，置信度={(summary.StageConfidence > 0 ? summary.StageConfidence : summary.StageScore):0.##}，扩散={summary.DiffusionScore:0.##}，延续={summary.ContinuationScore:0.##}。";
        return new StockCopilotMcpEvidenceDto(
            $"市场代理情绪={sentiment}",
            "市场情绪快照",
            "本地市场情绪快照",
            summary.SnapshotTime,
            summary.SnapshotTime,
            null,
            summaryText,
            summaryText,
            "local_fact",
            "summary_only",
            summary.SnapshotTime,
            null,
            $"market_sentiment:{summary.SnapshotTime:yyyyMMddHHmmss}",
            "market",
            sentiment,
            "市场",
            new[] { "market_sentiment_proxy" });
    }

    private static string? ResolveSocialOverallSentiment(
        StockCopilotSentimentCountDto stockNews,
        StockCopilotSentimentCountDto sectorReports,
        StockCopilotSentimentCountDto marketReports,
        StockCopilotSocialSentimentMarketProxyDto? marketProxy)
    {
        var positive = stockNews.PositiveCount + sectorReports.PositiveCount + marketReports.PositiveCount;
        var neutral = stockNews.NeutralCount + sectorReports.NeutralCount + marketReports.NeutralCount;
        var negative = stockNews.NegativeCount + sectorReports.NegativeCount + marketReports.NegativeCount;
        var total = stockNews.TotalCount + sectorReports.TotalCount + marketReports.TotalCount;

        if (total == 0)
        {
            return marketProxy?.OverallSentiment;
        }

        if (positive > negative && positive > neutral)
        {
            return "利好";
        }

        if (negative > positive && negative > neutral)
        {
            return "利空";
        }

        if (neutral > positive && neutral > negative)
        {
            return "中性";
        }

        return marketProxy?.OverallSentiment ?? "分化";
    }

    private static string MapStageToSentiment(string? stageLabel)
    {
        return stageLabel switch
        {
            "主升" => "利好",
            "退潮" => "利空",
            "分歧" => "分化",
            _ => "中性"
        };
    }

    private static IReadOnlyList<StockCopilotMcpEvidenceDto> ToEvidence(IEnumerable<StockAgentLocalNewsItemDto> items)
    {
        return items.Select(item => new StockCopilotMcpEvidenceDto(
            item.Title,
            LocalFactDisplayPolicy.SanitizeTranslatedTitle(item.Title, item.TranslatedTitle) ?? item.Title,
            item.Source,
            item.PublishTime,
            item.CrawledAt,
            item.Url,
            LocalFactDisplayPolicy.SanitizeEvidenceSnippet(item.Summary, item.Excerpt, item.Title),
            LocalFactDisplayPolicy.SanitizeEvidenceSnippet(item.Summary, item.Excerpt, item.Title),
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
        var readableSnippet = LocalFactDisplayPolicy.SanitizeEvidenceSnippet(item.Summary, item.Excerpt, item.Title);
        return new StockCopilotMcpEvidenceDto(
            item.Title,
            LocalFactDisplayPolicy.SanitizeTranslatedTitle(item.Title, item.TranslatedTitle) ?? item.Title,
            item.Source,
            item.PublishTime,
            item.CrawledAt,
            item.Url,
            readableSnippet,
            readableSnippet,
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
        string? activeDirection = null;
        for (var index = 4; index < closes.Count; index++)
        {
            var nextDirection = ResolveTdSetupDirection(closes[index], closes[index - 4], activeDirection);
            if (nextDirection is null)
            {
                count = 0;
                activeDirection = null;
            }
            else if (string.Equals(nextDirection, activeDirection, StringComparison.Ordinal))
            {
                count = Math.Min(count + 1, 9);
                activeDirection = nextDirection;
            }
            else
            {
                count = 1;
                activeDirection = nextDirection;
            }
        }

        var state = activeDirection switch
        {
            "sell" => "setup_up",
            "buy" => "setup_down",
            _ => "flat"
        };
        return (Math.Clamp(count, 0, 9), count == 0 ? "flat" : state);
    }

    private static string? ResolveTdSetupDirection(decimal currentClose, decimal referenceClose, string? activeDirection)
    {
        if (currentClose > referenceClose)
        {
            return "sell";
        }

        if (currentClose < referenceClose)
        {
            return "buy";
        }

        return activeDirection;
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

    private async Task<(StockFundamentalSnapshotDto? Snapshot, DateTime? UpdatedAt, string? DegradedFlag, string? Warning)> ResolveFundamentalSnapshotAsync(
        string symbol,
        LocalFactPackageDto localFacts,
        CancellationToken cancellationToken)
    {
        var localSnapshot = CreateLocalFundamentalSnapshot(localFacts);

        try
        {
            var snapshot = await _fundamentalSnapshotService.GetSnapshotAsync(symbol, cancellationToken);
            if (snapshot is null)
            {
                return localSnapshot is null
                    ? (null, null, "no_fundamental_facts", null)
                    : (localSnapshot, localSnapshot.UpdatedAt, null, "基本面快照缺失，已回退本地事实");
            }

            var mergedSnapshot = MergeFundamentalSnapshots(localSnapshot, snapshot);
            return (mergedSnapshot, mergedSnapshot.UpdatedAt, null, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            return localSnapshot is null
                ? (null, null, "fundamental_snapshot_unavailable", $"基本面快照抓取失败: {ex.Message}")
                : (localSnapshot, localSnapshot.UpdatedAt, "fundamental_snapshot_unavailable", $"基本面快照抓取失败，已回退本地事实: {ex.Message}");
        }
    }

    private static StockFundamentalSnapshotDto? CreateLocalFundamentalSnapshot(LocalFactPackageDto localFacts)
    {
        return localFacts.FundamentalFacts.Count == 0
            ? null
            : new StockFundamentalSnapshotDto(localFacts.FundamentalUpdatedAt ?? DateTime.UtcNow, ConvertFacts(localFacts.FundamentalFacts));
    }

    private static StockFundamentalSnapshotDto MergeFundamentalSnapshots(StockFundamentalSnapshotDto? localSnapshot, StockFundamentalSnapshotDto snapshot)
    {
        if (localSnapshot is null)
        {
            return snapshot;
        }

        var mergedFacts = localSnapshot.Facts.ToDictionary(item => item.Label, StringComparer.OrdinalIgnoreCase);
        var snapshotIsNewer = snapshot.UpdatedAt >= localSnapshot.UpdatedAt;

        foreach (var fact in snapshot.Facts)
        {
            if (!mergedFacts.TryGetValue(fact.Label, out var existing) || ShouldPreferSnapshotFact(existing, fact, snapshotIsNewer))
            {
                mergedFacts[fact.Label] = fact;
            }
        }

        return new StockFundamentalSnapshotDto(
            snapshot.UpdatedAt >= localSnapshot.UpdatedAt ? snapshot.UpdatedAt : localSnapshot.UpdatedAt,
            mergedFacts.Values.ToArray());
    }

    private static bool ShouldPreferSnapshotFact(StockFundamentalFactDto existing, StockFundamentalFactDto candidate, bool snapshotIsNewer)
    {
        if (string.IsNullOrWhiteSpace(existing.Value))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(candidate.Value))
        {
            return false;
        }

        return snapshotIsNewer || candidate.Value.Length > existing.Value.Length;
    }

    private static IReadOnlyList<StockFundamentalFactDto> ConvertFacts(IReadOnlyList<LocalFundamentalFactDto> facts)
    {
        return facts.Select(item => new StockFundamentalFactDto(item.Label, item.Value, item.Source)).ToArray();
    }

    private static IReadOnlyList<StockFundamentalFactDto> FilterShareholderFacts(IReadOnlyList<StockFundamentalFactDto> facts)
    {
        return facts.Where(IsShareholderFact).ToArray();
    }

    private static IReadOnlyList<StockFundamentalFactDto> FilterProductFacts(IReadOnlyList<StockFundamentalFactDto> facts)
    {
        return facts
            .Where(IsProductFact)
            .GroupBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Value.Length).First())
            .ToArray();
    }

    private static bool IsShareholderFact(StockFundamentalFactDto item)
    {
        return item.Label.Contains("股东", StringComparison.OrdinalIgnoreCase)
               || item.Label.Contains("户均", StringComparison.OrdinalIgnoreCase)
               || item.Label.Contains("集中度", StringComparison.OrdinalIgnoreCase)
               || item.Label.Contains("持股", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProductFact(StockFundamentalFactDto item)
    {
        return ProductFactLabels.Contains(item.Label);
    }

    private static int? TryResolveShareholderCount(IReadOnlyList<StockFundamentalFactDto>? facts)
    {
        var value = facts?.FirstOrDefault(item => item.Label.Contains("股东户数", StringComparison.OrdinalIgnoreCase))?.Value;
        return ParseInteger(value);
    }

    private static string? TryResolveFactValue(IReadOnlyList<StockFundamentalFactDto>? facts, params string[] labels)
    {
        if (facts is null || facts.Count == 0)
        {
            return null;
        }

        foreach (var label in labels)
        {
            var value = facts.FirstOrDefault(item => string.Equals(item.Label, label, StringComparison.OrdinalIgnoreCase))?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static int? TryResolveShareholderCount(IReadOnlyList<LocalFundamentalFactDto>? facts)
    {
        var value = facts?.FirstOrDefault(item => item.Label.Contains("股东户数", StringComparison.OrdinalIgnoreCase))?.Value;
        return ParseInteger(value);
    }

    private static int? ParseInteger(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = IntegerRegex.Match(value.Replace(",", string.Empty, StringComparison.Ordinal));
        return match.Success && int.TryParse(match.Value, out var parsed) ? parsed : null;
    }

    private static IReadOnlyList<string> BuildWarnings(string? warning)
    {
        return string.IsNullOrWhiteSpace(warning) ? Array.Empty<string>() : new[] { warning };
    }

    private static IReadOnlyList<string> BuildDegradedFlags(string? degradedFlag)
    {
        return string.IsNullOrWhiteSpace(degradedFlag) ? Array.Empty<string>() : new[] { degradedFlag };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static IReadOnlyList<StockCopilotMcpEvidenceDto> BuildCompanyOverviewEvidence(
        string symbol,
        StockQuoteDto quote,
        LocalFactPackageDto localFacts,
        DateTime? fundamentalUpdatedAt,
        int? shareholderCount,
        string? mainBusiness,
        string? businessScope)
    {
        var summaryParts = new List<string>
        {
            $"所属板块={FirstNonEmpty(localFacts.SectorName, quote.SectorName, "未知")}",
            $"股东户数={(shareholderCount?.ToString() ?? "未知")}",
            $"现价={quote.Price:0.00}"
        };

        if (!string.IsNullOrWhiteSpace(mainBusiness))
        {
            summaryParts.Add($"主营业务={mainBusiness}");
        }

        if (!string.IsNullOrWhiteSpace(businessScope))
        {
            summaryParts.Add($"经营范围={businessScope}");
        }

        var summary = string.Join("; ", summaryParts);
        var evidence = new List<StockCopilotMcpEvidenceDto>
        {
            new(
                $"{FirstNonEmpty(localFacts.Name, quote.Name, symbol)} 公司概览",
                FirstNonEmpty(localFacts.Name, quote.Name, symbol),
                "公司画像缓存",
                quote.Timestamp,
                fundamentalUpdatedAt,
                null,
                summary,
                summary,
                "local_fact",
                "full",
                fundamentalUpdatedAt ?? quote.Timestamp,
                null,
                $"company_profile:{symbol}",
                "overview",
                null,
                symbol,
                new[] { "company_overview" })
        };

        evidence.AddRange(localFacts.StockNews.Take(2).Select(ToEvidence));
        return evidence;
    }

    private static IReadOnlyList<StockCopilotMcpEvidenceDto> BuildFactEvidence(string symbol, IReadOnlyList<StockFundamentalFactDto> facts, DateTime? updatedAt, string level)
    {
        return facts.Select(item => new StockCopilotMcpEvidenceDto(
            $"{item.Label}: {item.Value}",
            item.Label,
            item.Source,
            updatedAt,
            updatedAt,
            null,
            item.Value,
            item.Value,
            "local_fact",
            "full",
            updatedAt,
            null,
            $"company_profile:{symbol}",
            level,
            null,
            symbol,
            new[] { level })).ToArray();
    }

    private static IReadOnlyList<StockCopilotMcpEvidenceDto> BuildProductEvidence(
        string symbol,
        IReadOnlyList<StockFundamentalFactDto> productFacts,
        DateTime? updatedAt,
        string? mainBusiness,
        string? businessScope,
        string? industry,
        string? region)
    {
        if (productFacts.Count == 0)
        {
            return Array.Empty<StockCopilotMcpEvidenceDto>();
        }

        var summaryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(mainBusiness))
        {
            summaryParts.Add($"主营业务={mainBusiness}");
        }

        if (!string.IsNullOrWhiteSpace(businessScope))
        {
            summaryParts.Add($"经营范围={businessScope}");
        }

        if (!string.IsNullOrWhiteSpace(industry))
        {
            summaryParts.Add($"所属行业={industry}");
        }

        if (!string.IsNullOrWhiteSpace(region))
        {
            summaryParts.Add($"所属地区={region}");
        }

        var evidence = new List<StockCopilotMcpEvidenceDto>();
        if (summaryParts.Count > 0)
        {
            var summary = string.Join("; ", summaryParts);
            evidence.Add(new StockCopilotMcpEvidenceDto(
                $"{symbol} 产品业务概览",
                "产品业务概览",
                BuildSourceSummary(productFacts),
                updatedAt,
                updatedAt,
                null,
                summary,
                summary,
                "local_fact",
                "full",
                updatedAt,
                null,
                $"company_profile:{symbol}",
                "product",
                null,
                symbol,
                new[] { "product" }));
        }

        evidence.AddRange(BuildFactEvidence(symbol, productFacts, updatedAt, "product"));
        return evidence;
    }

    private static IReadOnlyList<StockCopilotMcpFeatureDto> BuildCompanyOverviewFeatures(StockQuoteDto quote, LocalFactPackageDto localFacts, int? shareholderCount, int factCount, DateTime? updatedAt, string? mainBusiness, string? businessScope)
    {
        return new[]
        {
            new StockCopilotMcpFeatureDto("sectorName", "Sector Name", "text", null, FirstNonEmpty(localFacts.SectorName, quote.SectorName), null, "Sector inferred from local company profile or latest quote."),
            new StockCopilotMcpFeatureDto("price", "Latest Price", "number", quote.Price, null, null, "Latest cached or live quote price."),
            new StockCopilotMcpFeatureDto("changePercent", "Change Percent", "number", quote.ChangePercent, null, "%", "Current price change percent."),
            new StockCopilotMcpFeatureDto("shareholderCount", "Shareholder Count", "number", shareholderCount, null, null, "Latest shareholder count available to the system."),
            new StockCopilotMcpFeatureDto("fundamentalFactCount", "Fundamental Fact Count", "number", factCount, null, null, "Number of structured company facts retained for this symbol."),
            new StockCopilotMcpFeatureDto("mainBusiness", "Main Business", "text", null, mainBusiness, null, "Main business description extracted from company profile facts."),
            new StockCopilotMcpFeatureDto("businessScope", "Business Scope", "text", null, businessScope, null, "Business scope description extracted from company profile facts."),
            new StockCopilotMcpFeatureDto("fundamentalUpdatedAt", "Fundamental Updated At", "text", null, updatedAt?.ToString("yyyy-MM-dd HH:mm:ss"), null, "Timestamp of the latest structured company-profile update.")
        };
    }

    private static IReadOnlyList<StockCopilotProductFactDto> ConvertProductFacts(IReadOnlyList<StockFundamentalFactDto> facts)
    {
        return facts
            .Select(item => new StockCopilotProductFactDto(item.Label, item.Value, item.Source))
            .ToArray();
    }

    private static string BuildSourceSummary(IReadOnlyList<StockFundamentalFactDto> facts)
    {
        var sources = facts
            .Select(item => item.Source)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return sources.Length == 0 ? "unknown" : string.Join(" + ", sources);
    }

    private static IReadOnlyList<StockCopilotMcpFeatureDto> BuildProductFeatures(int factCount, DateTime? updatedAt, string? mainBusiness, string? businessScope, string? industry, string? csrcIndustry, string? region)
    {
        return new[]
        {
            new StockCopilotMcpFeatureDto("factCount", "Product Fact Count", "number", factCount, null, null, "Number of product/business facts retained by this MCP."),
            new StockCopilotMcpFeatureDto("mainBusiness", "Main Business", "text", null, mainBusiness, null, "Main business description extracted from company profile facts."),
            new StockCopilotMcpFeatureDto("businessScope", "Business Scope", "text", null, businessScope, null, "Registered business scope extracted from company profile facts."),
            new StockCopilotMcpFeatureDto("industry", "Industry", "text", null, industry, null, "Industry field extracted from company profile facts."),
            new StockCopilotMcpFeatureDto("csrcIndustry", "CSRC Industry", "text", null, csrcIndustry, null, "CSRC industry classification extracted from company profile facts."),
            new StockCopilotMcpFeatureDto("region", "Region", "text", null, region, null, "Registered region extracted from company profile facts."),
            new StockCopilotMcpFeatureDto("updatedAt", "Updated At", "text", null, updatedAt?.ToString("yyyy-MM-dd HH:mm:ss"), null, "Timestamp of the latest product/business snapshot.")
        };
    }

    private static IReadOnlyList<StockCopilotMcpFeatureDto> BuildFundamentalFeatures(int factCount, DateTime? updatedAt)
    {
        return new[]
        {
            new StockCopilotMcpFeatureDto("factCount", "Fact Count", "number", factCount, null, null, "Number of structured fundamental facts returned by this MCP."),
            new StockCopilotMcpFeatureDto("updatedAt", "Updated At", "text", null, updatedAt?.ToString("yyyy-MM-dd HH:mm:ss"), null, "Timestamp of the latest fundamental snapshot.")
        };
    }

    private static IReadOnlyList<StockCopilotMcpFeatureDto> BuildShareholderFeatures(int? shareholderCount, int factCount, DateTime? updatedAt)
    {
        return new[]
        {
            new StockCopilotMcpFeatureDto("shareholderCount", "Shareholder Count", "number", shareholderCount, null, null, "Latest shareholder count extracted from cache or upstream snapshot."),
            new StockCopilotMcpFeatureDto("factCount", "Shareholder Fact Count", "number", factCount, null, null, "Number of shareholder-specific facts retained in the response."),
            new StockCopilotMcpFeatureDto("updatedAt", "Updated At", "text", null, updatedAt?.ToString("yyyy-MM-dd HH:mm:ss"), null, "Timestamp of the latest shareholder-related snapshot.")
        };
    }
}