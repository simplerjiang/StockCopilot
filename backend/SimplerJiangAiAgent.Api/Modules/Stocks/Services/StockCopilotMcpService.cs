using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Infrastructure.Serialization;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Market.Services;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IStockCopilotMcpService
{
    Task<StockCopilotMcpEnvelopeDto<StockCopilotCompanyOverviewDataDto>> GetCompanyOverviewAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotProductDataDto>> GetProductAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotFundamentalsDataDto>> GetFundamentalsAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotShareholderDataDto>> GetShareholderAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotMarketContextDataDto>> GetMarketContextAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotSocialSentimentDataDto>> GetSocialSentimentAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotKlineDataDto>> GetKlineAsync(string symbol, string interval, int count, string? source, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotMinuteDataDto>> GetMinuteAsync(string symbol, string? source, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotStrategyDataDto>> GetStrategyAsync(string symbol, string interval, int count, string? source, IReadOnlyList<string>? strategies, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotNewsDataDto>> GetNewsAsync(string symbol, string level, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotSearchDataDto>> SearchAsync(string query, bool trustedOnly, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotFinancialReportDataDto>> GetFinancialReportAsync(string symbol, int periods, string? taskId, CancellationToken cancellationToken = default);
    Task<StockCopilotMcpEnvelopeDto<StockCopilotFinancialTrendDataDto>> GetFinancialTrendAsync(string symbol, int periods, string? taskId, CancellationToken cancellationToken = default);
}

public sealed class StockCopilotMcpService : IStockCopilotMcpService
{
    private const string Version = "v1";
    private static readonly Regex IntegerRegex = new("\\d+", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions ProductMarketRecognitionJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly HashSet<string> ProductFactLabels =
    [
        "主营业务",
        "经营范围",
        "所属行业",
        "证监会行业",
        "所属地区"
    ];
    private static readonly Dictionary<string, int> FundamentalLabelOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["最新财报期"] = 0,
        ["营业收入"] = 1,
        ["归属净利润"] = 2,
        ["扣非净利润"] = 3,
        ["营收同比"] = 4,
        ["归属净利同比"] = 5,
        ["基本每股收益"] = 6,
        ["每股净资产"] = 7,
        ["净资产收益率(ROE)"] = 8,
        ["销售毛利率"] = 9,
        ["销售净利率"] = 10,
        ["资产负债率"] = 11
    };
    private const string LatestFinanceFactSource = "东方财富最新财报";
    private static readonly TimeSpan RefreshSkipWindow = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, DateTime> RecentRefreshTimestamps = new(StringComparer.OrdinalIgnoreCase);
    private readonly IStockDataService _dataService;
    private readonly IQueryLocalFactDatabaseTool _queryLocalFactDatabaseTool;
    private readonly IStockMarketContextService _marketContextService;
    private readonly ISectorRotationQueryService _sectorRotationQueryService;
    private readonly IStockAgentFeatureEngineeringService _featureEngineeringService;
    private readonly IStockFundamentalSnapshotService _fundamentalSnapshotService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly StockCopilotSearchOptions _searchOptions;
    private readonly ILlmService? _llmService;
    private readonly StockSyncOptions _stockSyncOptions;
    private readonly IRealtimeMarketOverviewService? _overviewService;
    private readonly ILocalFactIngestionService? _localFactIngestionService;
    private readonly ILlmSettingsStore? _llmSettingsStore;
    private readonly IPortfolioSnapshotService? _portfolioSnapshotService;
    private readonly IFinancialDataReadService? _financialDataReadService;
    private readonly ILogger<StockCopilotMcpService> _logger;

    public StockCopilotMcpService(
        IStockDataService dataService,
        IQueryLocalFactDatabaseTool queryLocalFactDatabaseTool,
        IStockMarketContextService marketContextService,
        ISectorRotationQueryService sectorRotationQueryService,
        IStockAgentFeatureEngineeringService featureEngineeringService,
        IStockFundamentalSnapshotService fundamentalSnapshotService,
        IHttpClientFactory httpClientFactory,
        IOptions<StockCopilotSearchOptions> searchOptions,
        ILlmService? llmService = null,
        IOptions<StockSyncOptions>? stockSyncOptions = null,
        IRealtimeMarketOverviewService? overviewService = null,
        ILocalFactIngestionService? localFactIngestionService = null,
        ILlmSettingsStore? llmSettingsStore = null,
        IPortfolioSnapshotService? portfolioSnapshotService = null,
        IFinancialDataReadService? financialDataReadService = null,
        ILogger<StockCopilotMcpService>? logger = null)
    {
        _dataService = dataService;
        _queryLocalFactDatabaseTool = queryLocalFactDatabaseTool;
        _marketContextService = marketContextService;
        _sectorRotationQueryService = sectorRotationQueryService;
        _featureEngineeringService = featureEngineeringService;
        _fundamentalSnapshotService = fundamentalSnapshotService;
        _httpClientFactory = httpClientFactory;
        _searchOptions = searchOptions.Value;
        _llmService = llmService;
        _stockSyncOptions = stockSyncOptions?.Value ?? new StockSyncOptions();
        _overviewService = overviewService;
        _localFactIngestionService = localFactIngestionService;
        _llmSettingsStore = llmSettingsStore;
        _portfolioSnapshotService = portfolioSnapshotService;
        _financialDataReadService = financialDataReadService;
        _logger = logger ?? NullLogger<StockCopilotMcpService>.Instance;
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotCompanyOverviewDataDto>> GetCompanyOverviewAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var windowOptions = NormalizeWindowOptions(window);
        await EnsureSymbolFactsRefreshedAsync(normalizedSymbol, cancellationToken);
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
        var evidence = ApplyWindow(
            BuildCompanyOverviewEvidence(normalizedSymbol, quote, localFacts, snapshotResolution.UpdatedAt, shareholderCount, mainBusiness, businessScope),
            windowOptions.EvidenceSkip,
            windowOptions.EvidenceTake);

        // Fetch portfolio context for the symbol (graceful degradation)
        PortfolioContextDto? portfolioContext = null;
        if (_portfolioSnapshotService is not null)
        {
            try
            {
                portfolioContext = await _portfolioSnapshotService.GetPortfolioContextAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CompanyOverviewMcp: GetPortfolioContextAsync failed, degrading portfolio features.");
            }
        }

        var features = BuildCompanyOverviewFeatures(quote, localFacts, shareholderCount, overviewFacts.Count, snapshotResolution.UpdatedAt, mainBusiness, businessScope);
        if (portfolioContext is not null)
        {
            features = AppendPortfolioFeatures(features, portfolioContext, normalizedSymbol);
        }

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
                quote.VolumeRatio > 0m ? quote.VolumeRatio : null,
                shareholderCount,
                quote.Timestamp,
                snapshotResolution.UpdatedAt,
                overviewFacts.Count,
                mainBusiness,
                businessScope),
            evidence: evidence,
            features: features,
            symbol: normalizedSymbol,
            interval: null,
            query: null,
            marketContext: await ResolveMcpMarketContextAsync(normalizedSymbol, cancellationToken),
            degradedFlags: degradedFlags,
            warnings: warnings);
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotProductDataDto>> GetProductAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var windowOptions = NormalizeWindowOptions(window);
        await EnsureSymbolFactsRefreshedAsync(normalizedSymbol, cancellationToken);
        var localFacts = await _queryLocalFactDatabaseTool.QueryAsync(normalizedSymbol, cancellationToken);
        var snapshotResolution = await ResolveFundamentalSnapshotAsync(normalizedSymbol, localFacts, cancellationToken);
        var allFacts = snapshotResolution.Snapshot?.Facts ?? ConvertFacts(localFacts.FundamentalFacts);
        var productFacts = FilterProductFacts(allFacts).ToList();
        var mainBusiness = TryResolveFactValue(productFacts, "主营业务");
        var registeredBusinessScope = TryResolveFactValue(productFacts, "经营范围");
        var businessScope = ResolveProductBusinessScope(mainBusiness, registeredBusinessScope, TryResolveFactValue(productFacts, "所属行业"), TryResolveFactValue(productFacts, "证监会行业"));
        var industry = TryResolveFactValue(productFacts, "所属行业");
        var csrcIndustry = TryResolveFactValue(productFacts, "证监会行业");
        var region = TryResolveFactValue(productFacts, "所属地区");
        var marketRecognitionDirections = await ResolveProductMarketRecognitionDirectionsAsync(
            normalizedSymbol,
            localFacts,
            mainBusiness,
            businessScope,
            registeredBusinessScope,
            industry,
            csrcIndustry,
            region,
            cancellationToken);
        var warnings = BuildWarnings(snapshotResolution.Warning);
        var degradedFlags = BuildDegradedFlags(snapshotResolution.DegradedFlag ?? (string.IsNullOrWhiteSpace(businessScope) && string.IsNullOrWhiteSpace(registeredBusinessScope) ? "no_product_facts" : null));
        var evidence = ApplyWindow(
            BuildProductEvidence(normalizedSymbol, productFacts, snapshotResolution.UpdatedAt, mainBusiness, businessScope, registeredBusinessScope, industry, csrcIndustry, region, marketRecognitionDirections),
            windowOptions.EvidenceSkip,
            windowOptions.EvidenceTake);

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
            evidence: evidence,
            features: BuildProductFeatures(productFacts.Count, snapshotResolution.UpdatedAt, mainBusiness, businessScope, registeredBusinessScope, industry, csrcIndustry, region),
            symbol: normalizedSymbol,
            interval: null,
            query: null,
            marketContext: await ResolveMcpMarketContextAsync(normalizedSymbol, cancellationToken),
            degradedFlags: degradedFlags,
            warnings: warnings);
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotFundamentalsDataDto>> GetFundamentalsAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var windowOptions = NormalizeWindowOptions(window);
        await EnsureSymbolFactsRefreshedAsync(normalizedSymbol, cancellationToken);
        var localFacts = await _queryLocalFactDatabaseTool.QueryAsync(normalizedSymbol, cancellationToken);
        var snapshotResolution = await ResolveFundamentalSnapshotAsync(normalizedSymbol, localFacts, cancellationToken);
        var allFacts = snapshotResolution.Snapshot?.Facts ?? ConvertFacts(localFacts.FundamentalFacts);
        var orderedFacts = OrderFundamentalFacts(allFacts.Where(item => !IsShareholderFact(item)).ToList());
        var facts = ApplyWindow(orderedFacts, windowOptions.FactSkip, windowOptions.FactTake);
        var warnings = BuildWarnings(snapshotResolution.Warning);
        var degradedFlags = BuildDegradedFlags(snapshotResolution.DegradedFlag ?? (orderedFacts.Count == 0 ? "no_fundamental_facts" : null));
        var evidence = ApplyWindow(
            BuildFactEvidence(normalizedSymbol, orderedFacts, snapshotResolution.UpdatedAt, "fundamental"),
            windowOptions.EvidenceSkip,
            windowOptions.EvidenceTake);

        stopwatch.Stop();
        return BuildEnvelope(
            toolName: StockMcpToolNames.Fundamentals,
            policyClass: "local_required",
            taskId: taskId,
            latencyMs: stopwatch.ElapsedMilliseconds,
            data: new StockCopilotFundamentalsDataDto(normalizedSymbol, snapshotResolution.UpdatedAt, orderedFacts.Count, facts),
            evidence: evidence,
            features: BuildFundamentalFeatures(orderedFacts.Count, snapshotResolution.UpdatedAt),
            symbol: normalizedSymbol,
            interval: null,
            query: null,
            marketContext: await ResolveMcpMarketContextAsync(normalizedSymbol, cancellationToken),
            degradedFlags: degradedFlags,
            warnings: warnings);
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotShareholderDataDto>> GetShareholderAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var windowOptions = NormalizeWindowOptions(window);
        await EnsureSymbolFactsRefreshedAsync(normalizedSymbol, cancellationToken);
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
        var evidence = ApplyWindow(
            BuildFactEvidence(normalizedSymbol, shareholderFacts, snapshotResolution.UpdatedAt, "shareholder"),
            windowOptions.EvidenceSkip,
            windowOptions.EvidenceTake);

        stopwatch.Stop();
        return BuildEnvelope(
            toolName: StockMcpToolNames.Shareholder,
            policyClass: "local_required",
            taskId: taskId,
            latencyMs: stopwatch.ElapsedMilliseconds,
            data: new StockCopilotShareholderDataDto(normalizedSymbol, shareholderCount, snapshotResolution.UpdatedAt, shareholderFacts.Count, shareholderFacts),
            evidence: evidence,
            features: BuildShareholderFeatures(shareholderCount, shareholderFacts.Count, snapshotResolution.UpdatedAt),
            symbol: normalizedSymbol,
            interval: null,
            query: null,
            marketContext: await ResolveMcpMarketContextAsync(normalizedSymbol, cancellationToken),
            degradedFlags: degradedFlags,
            warnings: warnings);
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotMarketContextDataDto>> GetMarketContextAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var windowOptions = NormalizeWindowOptions(window);
        var rawMarketContext = await _marketContextService.GetLatestAsync(normalizedSymbol, cancellationToken);
        var marketContext = RedactProgrammaticMarketContext(rawMarketContext);
        var warnings = new List<string>();
        var degradedFlags = new List<string>();

        if (rawMarketContext is null)
        {
            warnings.Add("MarketContextMcp 当前未获取到本地市场上下文。请先确认市场情绪快照与板块轮动数据已入库。");
            degradedFlags.Add("no_market_context_data");
        }

        // Fetch realtime market overview (graceful degradation)
        MarketRealtimeOverviewDto? overview = null;
        if (_overviewService is not null)
        {
            try
            {
                overview = await _overviewService.GetOverviewAsync(cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MarketContextMcp: GetOverviewAsync failed, degrading overview fields to null.");
                degradedFlags.Add("overview_fetch_failed");
            }
        }

        // Map overview data to compact DTOs
        var indices = overview?.Indices?.Select(q => new MarketContextIndexDto(q.Symbol, q.Name, q.Price, q.ChangePercent)).ToArray();
        var mainCapitalFlow = overview?.MainCapitalFlow is { } mcf ? new MarketContextCapitalFlowDto(mcf.MainNetInflow, mcf.AmountUnit, mcf.SnapshotTime) : null;
        var northboundFlow = overview?.NorthboundFlow is { } nbf ? new MarketContextNorthboundDto(nbf.TotalNetInflow, nbf.AmountUnit, nbf.SnapshotTime) : null;
        var breadth = overview?.Breadth is { } b ? new MarketContextBreadthDto(b.Advancers, b.Decliners, b.LimitUpCount, b.LimitDownCount) : null;

        var evidence = BuildMarketContextEvidence(normalizedSymbol, marketContext, overview);
        var features = BuildMarketContextFeatures(marketContext, overview);

        // 追加近30天板块趋势摘要
        string? trendSummary = null;
        try
        {
            trendSummary = await _sectorRotationQueryService.GetMainlineTrendSummaryAsync(30, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MarketContextMcp: GetMainlineTrendSummaryAsync failed, degrading trend summary to null.");
            degradedFlags.Add("trend_summary_failed");
        }

        stopwatch.Stop();
        return BuildEnvelope(
            toolName: StockMcpToolNames.MarketContext,
            policyClass: "local_required",
            taskId: taskId,
            latencyMs: stopwatch.ElapsedMilliseconds,
            data: new StockCopilotMarketContextDataDto(
                normalizedSymbol,
                rawMarketContext is not null,
                marketContext?.StageConfidence,
                marketContext?.StockSectorName,
                marketContext?.MainlineSectorName,
                marketContext?.SectorCode,
                marketContext?.MainlineScore,
                indices,
                mainCapitalFlow,
                northboundFlow,
                breadth,
                trendSummary),
            evidence: ApplyWindow(evidence, windowOptions.EvidenceSkip, windowOptions.EvidenceTake),
            features: features,
            symbol: normalizedSymbol,
            interval: null,
            query: null,
            marketContext: marketContext,
            degradedFlags: degradedFlags,
            warnings: warnings);
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotSocialSentimentDataDto>> GetSocialSentimentAsync(string symbol, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var windowOptions = NormalizeWindowOptions(window);
        await EnsureSymbolFactsRefreshedAsync(normalizedSymbol, cancellationToken);
        var localFacts = await _queryLocalFactDatabaseTool.QueryAsync(normalizedSymbol, cancellationToken);
        var marketSummary = await _sectorRotationQueryService.GetLatestSummaryAsync(cancellationToken);
        var marketContext = await _marketContextService.GetLatestAsync(normalizedSymbol, cancellationToken);

        var stockNews = BuildSentimentCount(localFacts.StockNews);
        var sectorReports = BuildSentimentCount(localFacts.SectorReports);
        var marketReports = BuildSentimentCount(localFacts.MarketReports);
        var localEvidenceCount = stockNews.TotalCount + sectorReports.TotalCount + marketReports.TotalCount;
        var marketProxy = marketSummary is null ? null : BuildMarketProxy(marketSummary);
        var evidence = localFacts.StockNews
            .Concat(localFacts.SectorReports)
            .Concat(localFacts.MarketReports)
            .Select(ToEvidence)
            .ToList();

        if (marketSummary is not null)
        {
            evidence.Add(BuildMarketSummaryEvidence(marketSummary));
        }

        evidence = ApplyWindow(evidence, windowOptions.EvidenceSkip, windowOptions.EvidenceTake).ToList();

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

            warnings.Add("SocialSentimentMcp v1 是本地情绪相关证据聚合工具，仅汇总本地新闻与市场代理快照，不会自行给出社交情绪结论。");
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
                evidence.Count,
                latestEvidenceAt == default ? null : latestEvidenceAt,
                stockNews,
                sectorReports,
                marketReports,
                marketProxy is null
                    ? null
                    : new StockCopilotSocialSentimentMarketProxyDto(
                        marketProxy.StageLabel,
                        marketProxy.StageConfidence,
                        marketProxy.SnapshotTime)),
            evidence: evidence,
            features: BuildSocialSentimentFeatures(stockNews, sectorReports, marketReports, marketProxy, blocked, approximationMode),
            symbol: normalizedSymbol,
            interval: null,
            query: null,
            marketContext: ToCopilotMarketContext(marketContext),
            degradedFlags: degradedFlags,
            warnings: warnings);
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotKlineDataDto>> GetKlineAsync(string symbol, string interval, int count, string? source, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var safeInterval = string.IsNullOrWhiteSpace(interval) ? "day" : interval.Trim();
        var safeCount = Math.Clamp(count, 20, 240);
        var windowOptions = NormalizeWindowOptions(window);

        await EnsureSymbolFactsRefreshedAsync(normalizedSymbol, cancellationToken);

        var bundle = await FetchSymbolDataBundleAsync(normalizedSymbol, safeInterval, safeCount, source, cancellationToken);
        var newsPolicy = StockAgentNewsContextPolicy.Apply(bundle.Messages, DateTime.Now).Policy;
        var prepared = _featureEngineeringService.Prepare(normalizedSymbol, bundle.Quote, bundle.KLines, bundle.MinuteLines, bundle.Messages, newsPolicy, StockAgentLocalFactProjection.Create(bundle.LocalFacts), DateTime.Now);

        var latestClose = bundle.KLines.LastOrDefault()?.Close ?? bundle.Quote.Price;
        var support = bundle.KLines.TakeLast(20).DefaultIfEmpty().Min(item => item?.Low ?? latestClose);
        var resistance = bundle.KLines.TakeLast(20).DefaultIfEmpty().Max(item => item?.High ?? latestClose);
        var trend = prepared.Features.Trend;
        var data = new StockCopilotKlineDataDto(
            normalizedSymbol,
            safeInterval,
            bundle.KLines.Count,
            bundle.KLines,
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
            evidence: ApplyWindow(ToEvidence(prepared.LocalFacts.StockNews), windowOptions.EvidenceSkip, windowOptions.EvidenceTake),
            features: BuildFeatureList(prepared.Features),
            symbol: normalizedSymbol,
            interval: safeInterval,
            query: null,
            marketContext: await ResolveMcpMarketContextAsync(normalizedSymbol, bundle.Quote.SectorName, cancellationToken),
            degradedFlags: prepared.Features.DegradedFlags,
            warnings: Array.Empty<string>());
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotMinuteDataDto>> GetMinuteAsync(string symbol, string? source, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var windowOptions = NormalizeWindowOptions(window);

        await EnsureSymbolFactsRefreshedAsync(normalizedSymbol, cancellationToken);

        var bundle = await FetchSymbolDataBundleAsync(normalizedSymbol, "day", 60, source, cancellationToken);
        var prepared = _featureEngineeringService.Prepare(
            normalizedSymbol,
            bundle.Quote,
            bundle.KLines,
            bundle.MinuteLines,
            bundle.Messages,
            StockAgentNewsContextPolicy.Apply(bundle.Messages, DateTime.Now).Policy,
            StockAgentLocalFactProjection.Create(bundle.LocalFacts),
            DateTime.Now);

        var opening = bundle.MinuteLines.FirstOrDefault()?.Price;
        var middayAnchor = bundle.MinuteLines.LastOrDefault(item => item.Time <= new TimeSpan(11, 30, 0))?.Price;
        var close = bundle.MinuteLines.LastOrDefault()?.Price;
        var high = bundle.MinuteLines.Count == 0 ? (decimal?)null : bundle.MinuteLines.Max(item => item.Price);
        var low = bundle.MinuteLines.Count == 0 ? (decimal?)null : bundle.MinuteLines.Min(item => item.Price);
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
            bundle.MinuteLines.Count,
            bundle.MinuteLines,
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
            evidence: ApplyWindow(ToEvidence(prepared.LocalFacts.StockNews), windowOptions.EvidenceSkip, windowOptions.EvidenceTake),
            features: BuildFeatureList(prepared.Features),
            symbol: normalizedSymbol,
            interval: "minute",
            query: null,
            marketContext: await ResolveMcpMarketContextAsync(normalizedSymbol, bundle.Quote.SectorName, cancellationToken),
            degradedFlags: prepared.Features.DegradedFlags,
            warnings: Array.Empty<string>());
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotStrategyDataDto>> GetStrategyAsync(string symbol, string interval, int count, string? source, IReadOnlyList<string>? strategies, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var safeInterval = string.IsNullOrWhiteSpace(interval) ? "day" : interval.Trim();
        var safeCount = Math.Clamp(count, 30, 180);
        var windowOptions = NormalizeWindowOptions(window);

        await EnsureSymbolFactsRefreshedAsync(normalizedSymbol, cancellationToken);

        var bundle = await FetchSymbolDataBundleAsync(normalizedSymbol, safeInterval, safeCount, source, cancellationToken);
        var prepared = _featureEngineeringService.Prepare(
            normalizedSymbol,
            bundle.Quote,
            bundle.KLines,
            bundle.MinuteLines,
            bundle.Messages,
            StockAgentNewsContextPolicy.Apply(bundle.Messages, DateTime.Now).Policy,
            StockAgentLocalFactProjection.Create(bundle.LocalFacts),
            DateTime.Now);

        var requested = NormalizeStrategies(strategies);
        var signals = BuildStrategySignals(bundle.KLines, bundle.MinuteLines, requested, safeInterval);

        stopwatch.Stop();
        return BuildEnvelope(
            toolName: "StockStrategyMcp",
            policyClass: "local_required",
            taskId: taskId,
            latencyMs: stopwatch.ElapsedMilliseconds,
            data: new StockCopilotStrategyDataDto(normalizedSymbol, safeInterval, requested, signals),
            evidence: ApplyWindow(ToEvidence(prepared.LocalFacts.StockNews.Concat(prepared.LocalFacts.SectorReports)), windowOptions.EvidenceSkip, windowOptions.EvidenceTake),
            features: BuildFeatureList(prepared.Features),
            symbol: normalizedSymbol,
            interval: safeInterval,
            query: null,
            marketContext: await ResolveMcpMarketContextAsync(normalizedSymbol, bundle.Quote.SectorName, cancellationToken),
            degradedFlags: prepared.Features.DegradedFlags,
            warnings: Array.Empty<string>());
    }

    public async Task<StockCopilotMcpEnvelopeDto<StockCopilotNewsDataDto>> GetNewsAsync(string symbol, string level, string? taskId, StockCopilotMcpWindowOptions? window = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedLevel = string.IsNullOrWhiteSpace(level) ? "stock" : level.Trim().ToLowerInvariant();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var windowOptions = NormalizeWindowOptions(window);

        if (normalizedLevel == "market")
        {
            await EnsureMarketFactsRefreshedAsync(cancellationToken);
        }
        else
        {
            await EnsureSymbolFactsRefreshedAsync(normalizedSymbol, cancellationToken);
        }

        LocalNewsBucketDto bucket = normalizedLevel == "market"
            ? await _queryLocalFactDatabaseTool.QueryMarketAsync(cancellationToken)
            : await _queryLocalFactDatabaseTool.QueryLevelAsync(normalizedSymbol, normalizedLevel, cancellationToken);

        var evidence = ApplyWindow(bucket.Items.Select(ToEvidence).ToArray(), windowOptions.EvidenceSkip, windowOptions.EvidenceTake);
        var warnings = Array.Empty<string>();
        var degradedFlags = evidence.Count == 0 ? new[] { "no_local_news_evidence" } : Array.Empty<string>();
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
            marketContext: normalizedLevel == "market" ? null : await ResolveMcpMarketContextAsync(normalizedSymbol, cancellationToken),
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
        var apiKey = await ResolveSearchApiKeyAsync(cancellationToken);
        var searchEnabled = _searchOptions.Enabled || !string.IsNullOrWhiteSpace(apiKey);

        if (!searchEnabled || string.IsNullOrWhiteSpace(apiKey))
        {
            warnings.Add("外部搜索未启用，StockSearchMcp 当前只返回空结果。请在 LLM 设置中配置 Tavily API Key。");
            degradedFlags.Add("external_search_unavailable");
        }
        else
        {
            var httpClient = _httpClientFactory.CreateClient();
            using var response = await httpClient.PostAsJsonAsync(
                _searchOptions.BaseUrl.TrimEnd('/') + "/search",
                new
                {
                    api_key = apiKey,
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
                        var publishedAt = ParseExternalPublishedAt(ReadString(item, "published_date"));
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

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotFinancialReportDataDto>> GetFinancialReportAsync(
        string symbol, int periods, string? taskId, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);

        var summary = _financialDataReadService?.GetReportSummary(normalizedSymbol, periods);

        var periodDtos = summary?.Periods?.Select(p => new FinancialReportPeriodDto(
            p.ReportDate, p.ReportType, p.SourceChannel,
            p.KeyMetrics as IReadOnlyDictionary<string, object?> ??
                new Dictionary<string, object?>(p.KeyMetrics)
        )).ToList() ?? new List<FinancialReportPeriodDto>();

        var degradedFlags = summary == null || periodDtos.Count == 0
            ? new List<string> { "no_financial_report_data" }
            : new List<string>();

        sw.Stop();
        return Task.FromResult(BuildEnvelope(
            toolName: StockMcpToolNames.FinancialReport,
            policyClass: "local_required",
            taskId: taskId,
            latencyMs: sw.ElapsedMilliseconds,
            data: new StockCopilotFinancialReportDataDto(normalizedSymbol, periodDtos.Count, periodDtos),
            evidence: Array.Empty<StockCopilotMcpEvidenceDto>(),
            features: Array.Empty<StockCopilotMcpFeatureDto>(),
            symbol: normalizedSymbol,
            interval: null,
            query: null,
            marketContext: null,
            degradedFlags: degradedFlags,
            warnings: Array.Empty<string>()));
    }

    public Task<StockCopilotMcpEnvelopeDto<StockCopilotFinancialTrendDataDto>> GetFinancialTrendAsync(
        string symbol, int periods, string? taskId, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);

        var summary = _financialDataReadService?.GetTrendSummary(normalizedSymbol, periods);

        var revenue = summary?.Revenue?.Select(t => new FinancialTrendPointDto(t.Period, t.Value, t.YoY)).ToList()
            ?? new List<FinancialTrendPointDto>();
        var netProfit = summary?.NetProfit?.Select(t => new FinancialTrendPointDto(t.Period, t.Value, t.YoY)).ToList()
            ?? new List<FinancialTrendPointDto>();
        var totalAssets = summary?.TotalAssets?.Select(t => new FinancialTrendPointDto(t.Period, t.Value, t.YoY)).ToList()
            ?? new List<FinancialTrendPointDto>();
        var dividends = summary?.RecentDividends?.Select(d => new FinancialDividendDto(d.Plan, d.DividendPerShare)).ToList()
            ?? new List<FinancialDividendDto>();

        var periodCount = Math.Max(revenue.Count, Math.Max(netProfit.Count, totalAssets.Count));
        var degradedFlags = summary == null || periodCount == 0
            ? new List<string> { "no_financial_trend_data" }
            : new List<string>();

        sw.Stop();
        return Task.FromResult(BuildEnvelope(
            toolName: StockMcpToolNames.FinancialTrend,
            policyClass: "local_required",
            taskId: taskId,
            latencyMs: sw.ElapsedMilliseconds,
            data: new StockCopilotFinancialTrendDataDto(normalizedSymbol, periodCount, revenue, netProfit, totalAssets, dividends),
            evidence: Array.Empty<StockCopilotMcpEvidenceDto>(),
            features: Array.Empty<StockCopilotMcpFeatureDto>(),
            symbol: normalizedSymbol,
            interval: null,
            query: null,
            marketContext: null,
            degradedFlags: degradedFlags,
            warnings: Array.Empty<string>()));
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
        StockCopilotMcpMarketContextDto? marketContext,
        IReadOnlyList<string> degradedFlags,
        IReadOnlyList<string> warnings)
    {
        var generatedAt = DateTime.UtcNow;
        var cache = new StockCopilotMcpCacheDto(false, "live", generatedAt);
        var normalizedMarketContext = NormalizeMcpMarketContext(marketContext);
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
            new StockCopilotMcpMetaDto(Version, policyClass, toolName, symbol, interval, query, normalizedMarketContext),
            ResolveErrorCode(degradedFlags, warnings),
            ResolveFreshnessTag(evidence, generatedAt),
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

    private static string ResolveFreshnessTag(IReadOnlyList<StockCopilotMcpEvidenceDto> evidence, DateTime generatedAtUtc)
    {
        if (evidence.Count == 0)
        {
            return "no_data";
        }

        var latestTimestamp = evidence
            .Select(item => item.PublishedAt ?? item.CrawledAt ?? item.IngestedAt)
            .Where(item => item.HasValue)
            .Select(item => item!.Value.ToUniversalTime())
            .DefaultIfEmpty(generatedAtUtc)
            .Max();

        var age = generatedAtUtc - latestTimestamp;
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

    private static StockCopilotMcpMarketContextDto? NormalizeMcpMarketContext(StockCopilotMcpMarketContextDto? marketContext)
    {
        if (marketContext is null)
        {
            return null;
        }

        return new StockCopilotMcpMarketContextDto(
            marketContext.StageConfidence,
            NormalizeNullableText(marketContext.StockSectorName),
            NormalizeNullableText(marketContext.MainlineSectorName),
            NormalizeNullableText(marketContext.SectorCode),
            marketContext.MainlineScore);
    }

    private static string? NormalizeNullableText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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
            new StockCopilotMcpFeatureDto("latestPublishedAt", "Latest Publish Time", "text", null, ChinaTimeZone.ToChina(items.OrderByDescending(item => item.PublishTime).FirstOrDefault()?.PublishTime)?.ToString("yyyy-MM-dd HH:mm:ss"), null, "Latest published time across retained local evidence."),
            new StockCopilotMcpFeatureDto("sectorName", "Sector Name", "text", null, bucket.SectorName, null, "Sector inferred by local fact query."),
        };
    }

    private static IReadOnlyList<StockCopilotMcpEvidenceDto> BuildMarketContextEvidence(string symbol, StockCopilotMcpMarketContextDto? marketContext, MarketRealtimeOverviewDto? overview = null)
    {
        var evidence = new List<StockCopilotMcpEvidenceDto>();

        if (marketContext is not null)
        {
            if (!string.IsNullOrWhiteSpace(marketContext.StockSectorName) || !string.IsNullOrWhiteSpace(marketContext.SectorCode))
            {
                var summary = $"该字段来自本地市场上下文，不是东方财富公司概况字段。个股行业={marketContext.StockSectorName ?? "未知"}，行业代码={marketContext.SectorCode ?? "未知"}。";
                evidence.Add(new StockCopilotMcpEvidenceDto(
                    $"个股行业={marketContext.StockSectorName ?? "未知"}",
                    "本地个股行业上下文",
                    "本地市场上下文",
                    null,
                    null,
                    null,
                    summary,
                    summary,
                    "local_fact",
                    "summary_only",
                    null,
                    null,
                    $"market_context:{symbol}:stock_sector",
                    "market_context",
                    null,
                    symbol,
                    new[] { "local_market_context" }));
            }

            if (!string.IsNullOrWhiteSpace(marketContext.MainlineSectorName) || marketContext.MainlineScore.HasValue || marketContext.StageConfidence.HasValue)
            {
                var summary = $"该字段来自本地市场上下文/板块轮动快照，不是东方财富字段。当前主线={marketContext.MainlineSectorName ?? "无"}，主线强度={FormatOptionalNumber(marketContext.MainlineScore)}，阶段置信度={FormatOptionalNumber(marketContext.StageConfidence)}。";
                evidence.Add(new StockCopilotMcpEvidenceDto(
                    $"本地主线板块={marketContext.MainlineSectorName ?? "无"}",
                    "本地主线板块轮动",
                    "本地市场上下文/板块轮动",
                    null,
                    null,
                    null,
                    summary,
                    summary,
                    "local_fact",
                    "summary_only",
                    null,
                    null,
                    $"market_context:{symbol}:mainline",
                    "market_context",
                    null,
                    symbol,
                    new[] { "local_market_context", "sector_rotation" }));
            }
        }

        if (overview is not null)
        {
            if (overview.Indices is { Count: > 0 } indices)
            {
                var indexSummary = string.Join("；", indices.Select(q => $"{q.Name}({q.Symbol}) {q.Price:F2} {q.ChangePercent:+0.00;-0.00}%"));
                evidence.Add(new StockCopilotMcpEvidenceDto(
                    "三大指数实时行情",
                    "市场实时概览",
                    "实时行情接口",
                    overview.SnapshotTime,
                    overview.SnapshotTime,
                    null,
                    indexSummary,
                    indexSummary,
                    "realtime_quote",
                    "summary_only",
                    null,
                    null,
                    $"market_context:{symbol}:indices",
                    "market_context",
                    null,
                    symbol,
                    new[] { "realtime_overview", "indices" }));
            }

            if (overview.MainCapitalFlow is { } mcf)
            {
                var mcfSummary = $"主力资金净流入={mcf.MainNetInflow:F2}{mcf.AmountUnit}，快照时间={mcf.SnapshotTime:HH:mm}。";
                evidence.Add(new StockCopilotMcpEvidenceDto(
                    $"主力净流入={mcf.MainNetInflow:F2}{mcf.AmountUnit}",
                    "主力资金流向",
                    "实时资金流向接口",
                    mcf.SnapshotTime,
                    mcf.SnapshotTime,
                    null,
                    mcfSummary,
                    mcfSummary,
                    "realtime_quote",
                    "summary_only",
                    null,
                    null,
                    $"market_context:{symbol}:main_capital_flow",
                    "market_context",
                    null,
                    symbol,
                    new[] { "realtime_overview", "capital_flow" }));
            }

            if (overview.NorthboundFlow is { } nbf)
            {
                var nbfSummary = $"北向资金净流入={nbf.TotalNetInflow:F2}{nbf.AmountUnit}，快照时间={nbf.SnapshotTime:HH:mm}。";
                evidence.Add(new StockCopilotMcpEvidenceDto(
                    $"北向净流入={nbf.TotalNetInflow:F2}{nbf.AmountUnit}",
                    "北向资金流向",
                    "实时北向资金接口",
                    nbf.SnapshotTime,
                    nbf.SnapshotTime,
                    null,
                    nbfSummary,
                    nbfSummary,
                    "realtime_quote",
                    "summary_only",
                    null,
                    null,
                    $"market_context:{symbol}:northbound_flow",
                    "market_context",
                    null,
                    symbol,
                    new[] { "realtime_overview", "northbound" }));
            }

            if (overview.Breadth is { } breadth)
            {
                var breadthSummary = $"涨={breadth.Advancers}，跌={breadth.Decliners}，涨停={breadth.LimitUpCount}，跌停={breadth.LimitDownCount}。";
                evidence.Add(new StockCopilotMcpEvidenceDto(
                    $"涨跌分布: 涨{breadth.Advancers}/跌{breadth.Decliners}",
                    "涨跌分布",
                    "实时涨跌分布接口",
                    overview.SnapshotTime,
                    overview.SnapshotTime,
                    null,
                    breadthSummary,
                    breadthSummary,
                    "realtime_quote",
                    "summary_only",
                    null,
                    null,
                    $"market_context:{symbol}:breadth",
                    "market_context",
                    null,
                    symbol,
                    new[] { "realtime_overview", "breadth" }));
            }
        }

        return evidence;
    }

    private static IReadOnlyList<StockCopilotMcpFeatureDto> BuildMarketContextFeatures(StockCopilotMcpMarketContextDto? marketContext, MarketRealtimeOverviewDto? overview = null)
    {
        var features = new List<StockCopilotMcpFeatureDto>();

        if (marketContext is null)
        {
            features.Add(new StockCopilotMcpFeatureDto("available", "Context Available", "text", null, "false", null, "Whether a local market-context snapshot was available."));
        }
        else
        {
            features.Add(new StockCopilotMcpFeatureDto("available", "Context Available", "text", null, "true", null, "Whether a local market-context snapshot was available."));
            features.Add(new StockCopilotMcpFeatureDto("stageConfidence", "Stage Confidence", "number", marketContext.StageConfidence, null, null, "Confidence score retained from the local market-context snapshot."));
            features.Add(new StockCopilotMcpFeatureDto("mainlineScore", "Mainline Score", "number", marketContext.MainlineScore, null, null, "Strength score retained from local market-context / sector-rotation data."));
            features.Add(new StockCopilotMcpFeatureDto("stockSectorName", "Stock Sector Name", "text", null, marketContext.StockSectorName, null, "Stock sector carried from local market context."));
            features.Add(new StockCopilotMcpFeatureDto("mainlineSectorName", "Mainline Sector Name", "text", null, marketContext.MainlineSectorName, null, "Mainline sector carried from local market context / sector rotation."));
            features.Add(new StockCopilotMcpFeatureDto("sectorCode", "Sector Code", "text", null, marketContext.SectorCode, null, "Sector code carried from local market context."));
        }

        if (overview is not null)
        {
            if (overview.Indices is { Count: > 0 })
            {
                foreach (var idx in overview.Indices)
                {
                    features.Add(new StockCopilotMcpFeatureDto($"index_{idx.Symbol}_price", $"{idx.Name} Price", "number", idx.Price, null, null, $"Latest price of index {idx.Name}."));
                    features.Add(new StockCopilotMcpFeatureDto($"index_{idx.Symbol}_changePct", $"{idx.Name} Change%", "number", idx.ChangePercent, null, null, $"Change percent of index {idx.Name}."));
                }
            }

            if (overview.MainCapitalFlow is { } mcf)
            {
                features.Add(new StockCopilotMcpFeatureDto("mainCapitalNetInflow", "Main Capital Net Inflow", "number", mcf.MainNetInflow, null, mcf.AmountUnit, "Main capital net inflow from realtime overview."));
            }

            if (overview.NorthboundFlow is { } nbf)
            {
                features.Add(new StockCopilotMcpFeatureDto("northboundNetInflow", "Northbound Net Inflow", "number", nbf.TotalNetInflow, null, nbf.AmountUnit, "Total northbound net inflow from realtime overview."));
            }

            if (overview.Breadth is { } breadth)
            {
                features.Add(new StockCopilotMcpFeatureDto("advancers", "Advancers", "number", breadth.Advancers, null, null, "Number of advancing stocks."));
                features.Add(new StockCopilotMcpFeatureDto("decliners", "Decliners", "number", breadth.Decliners, null, null, "Number of declining stocks."));
                features.Add(new StockCopilotMcpFeatureDto("limitUpCount", "Limit Up Count", "number", breadth.LimitUpCount, null, null, "Number of limit-up stocks."));
                features.Add(new StockCopilotMcpFeatureDto("limitDownCount", "Limit Down Count", "number", breadth.LimitDownCount, null, null, "Number of limit-down stocks."));
            }
        }

        return features;
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
            new StockCopilotMcpFeatureDto("status", "Contract Status", "text", null, blocked ? "blocked" : "degraded", null, "SocialSentimentMcp v1 is a local sentiment-evidence aggregation tool, not an autonomous sentiment judge."),
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
            rows.Count(item => string.Equals(item.Sentiment, "利空", StringComparison.OrdinalIgnoreCase)),
            rows.Count(item => string.Equals(item.Sentiment, "中性", StringComparison.OrdinalIgnoreCase)),
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
            summary.SnapshotTime);
    }

    private static StockCopilotMcpEvidenceDto BuildMarketSummaryEvidence(MarketSentimentSummaryDto summary)
    {
        var stageLabel = string.IsNullOrWhiteSpace(summary.StageLabelV2)
            ? summary.StageLabel
            : summary.StageLabelV2;
        var summaryText = $"市场阶段={stageLabel}，置信度={(summary.StageConfidence > 0 ? summary.StageConfidence : summary.StageScore):0.##}，扩散={summary.DiffusionScore:0.##}，延续={summary.ContinuationScore:0.##}。";
        return new StockCopilotMcpEvidenceDto(
            $"本地市场代理阶段={stageLabel}",
            "本地市场代理快照",
            "本地市场上下文/情绪快照",
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
            null,
            "市场",
            new[] { "market_sentiment_proxy" });
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

    private static DateTime? ParseExternalPublishedAt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out var dto))
        {
            return dto.UtcDateTime;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return parsed.Kind switch
            {
                DateTimeKind.Utc => parsed,
                DateTimeKind.Local => parsed.ToUniversalTime(),
                _ => DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            };
        }

        return null;
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
        return ProductFactLabels.Contains(item.Label)
               || item.Label.StartsWith("主营构成", StringComparison.Ordinal);
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

            evidence.AddRange(localFacts.StockNews.Select(ToEvidence));
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
        string? registeredBusinessScope,
        string? industry,
        string? csrcIndustry,
        string? region,
        IReadOnlyList<string> marketRecognitionDirections)
    {
        if (productFacts.Count == 0)
        {
            return Array.Empty<StockCopilotMcpEvidenceDto>();
        }

        var summaryParts = new List<string>();
        if (marketRecognitionDirections.Count > 0)
        {
            summaryParts.Add($"市场认可方向={string.Join(" / ", marketRecognitionDirections)}");
        }

        var summaryBusiness = ResolveProductEvidenceBusinessSummary(mainBusiness, businessScope, registeredBusinessScope, industry, csrcIndustry);
        if (!string.IsNullOrWhiteSpace(summaryBusiness))
        {
            summaryParts.Add($"业务摘要={summaryBusiness}");
        }

        var primaryIndustry = FirstNonEmpty(industry, csrcIndustry);
        if (!string.IsNullOrWhiteSpace(primaryIndustry))
        {
            summaryParts.Add($"所属行业={primaryIndustry}");
        }

        if (!string.IsNullOrWhiteSpace(region))
        {
            summaryParts.Add($"所属地区={region}");
        }

        if (summaryParts.Count == 0 && !string.IsNullOrWhiteSpace(registeredBusinessScope))
        {
            summaryParts.Add("产品事实已收录，详见后续经营范围原始事实");
        }

        var evidence = new List<StockCopilotMcpEvidenceDto>();
        if (summaryParts.Count > 0)
        {
            var summary = string.Join("; ", summaryParts);
            var summarySource = BuildSourceSummary(productFacts);
            if (marketRecognitionDirections.Count > 0)
            {
                summarySource = string.Concat(summarySource, " + 市场归纳LLM");
            }

            evidence.Add(new StockCopilotMcpEvidenceDto(
                $"{symbol} 产品业务概览",
                "产品业务概览",
                summarySource,
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
            new StockCopilotMcpFeatureDto("fundamentalUpdatedAt", "Fundamental Updated At", "text", null, ChinaTimeZone.ToChina(updatedAt)?.ToString("yyyy-MM-dd HH:mm:ss"), null, "Timestamp of the latest structured company-profile update.")
        };
    }

    private static IReadOnlyList<StockCopilotMcpFeatureDto> AppendPortfolioFeatures(
        IReadOnlyList<StockCopilotMcpFeatureDto> baseFeatures,
        PortfolioContextDto portfolioContext,
        string symbol)
    {
        var list = new List<StockCopilotMcpFeatureDto>(baseFeatures);
        list.Add(new StockCopilotMcpFeatureDto("portfolioTotalCapital", "Portfolio Total Capital", "number", portfolioContext.TotalCapital, null, "CNY", "User total capital from portfolio settings."));
        list.Add(new StockCopilotMcpFeatureDto("portfolioTotalPositionRatio", "Portfolio Total Position Ratio", "number", portfolioContext.TotalPositionRatio, null, "%", "Total position ratio (total cost / total capital)."));
        list.Add(new StockCopilotMcpFeatureDto("portfolioAvailableCash", "Portfolio Available Cash", "number", portfolioContext.AvailableCash, null, "CNY", "Available cash for new positions."));

        var position = portfolioContext.Positions.FirstOrDefault(p => string.Equals(p.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        if (position is not null)
        {
            list.Add(new StockCopilotMcpFeatureDto("currentPositionQuantity", "Current Position Quantity", "number", position.Quantity, null, "shares", "User's current holding quantity for this symbol."));
            list.Add(new StockCopilotMcpFeatureDto("currentPositionRatio", "Current Position Ratio", "number", position.PositionRatio, null, "%", "User's position ratio for this symbol."));
            list.Add(new StockCopilotMcpFeatureDto("currentPositionPnL", "Current Position PnL", "number", position.UnrealizedPnL, null, "CNY", "Unrealized P&L for this symbol."));
        }

        return list;
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

    private static IReadOnlyList<StockCopilotMcpFeatureDto> BuildProductFeatures(int factCount, DateTime? updatedAt, string? mainBusiness, string? businessScope, string? registeredBusinessScope, string? industry, string? csrcIndustry, string? region)
    {
        return new[]
        {
            new StockCopilotMcpFeatureDto("factCount", "Product Fact Count", "number", factCount, null, null, "Number of product/business facts retained by this MCP."),
            new StockCopilotMcpFeatureDto("mainBusiness", "Main Business", "text", null, mainBusiness, null, "Main business description extracted from company profile facts."),
            new StockCopilotMcpFeatureDto("businessScope", "Business Scope", "text", null, businessScope, null, "Market-readable business summary, preferring 主营业务 and only falling back when needed."),
            new StockCopilotMcpFeatureDto("registeredBusinessScope", "Registered Business Scope", "text", null, registeredBusinessScope, null, "Original 工商口径经营范围 retained from company profile facts."),
            new StockCopilotMcpFeatureDto("industry", "Industry", "text", null, industry, null, "Industry field extracted from company profile facts."),
            new StockCopilotMcpFeatureDto("csrcIndustry", "CSRC Industry", "text", null, csrcIndustry, null, "CSRC industry classification extracted from company profile facts."),
            new StockCopilotMcpFeatureDto("region", "Region", "text", null, region, null, "Registered region extracted from company profile facts."),
            new StockCopilotMcpFeatureDto("updatedAt", "Updated At", "text", null, ChinaTimeZone.ToChina(updatedAt)?.ToString("yyyy-MM-dd HH:mm:ss"), null, "Timestamp of the latest product/business snapshot.")
        };
    }

    private async Task<IReadOnlyList<string>> ResolveProductMarketRecognitionDirectionsAsync(
        string symbol,
        LocalFactPackageDto localFacts,
        string? mainBusiness,
        string? businessScope,
        string? registeredBusinessScope,
        string? industry,
        string? csrcIndustry,
        string? region,
        CancellationToken cancellationToken)
    {
        if (_llmService is null)
        {
            return Array.Empty<string>();
        }

        var localEvidence = localFacts.StockNews
            .Concat(localFacts.SectorReports)
            .OrderByDescending(item => item.PublishTime)
            .ThenByDescending(item => item.IngestedAt ?? item.CrawledAt)
            .Take(6)
            .Select(item => new ProductMarketRecognitionLocalEvidencePromptItem(
                item.Title,
                item.TranslatedTitle,
                item.Source,
                item.PublishTime,
                NormalizeNullableText(item.AiTarget),
                item.AiTags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                TruncateForPrompt(LocalFactDisplayPolicy.SanitizeEvidenceSnippet(item.Summary, item.Excerpt, item.Title), 220)))
            .ToArray();

        var externalEvidence = await SearchProductMarketRecognitionEvidenceAsync(symbol, localFacts.Name, cancellationToken);
        if (localEvidence.Length == 0
            && externalEvidence.Count == 0
            && string.IsNullOrWhiteSpace(mainBusiness)
            && string.IsNullOrWhiteSpace(businessScope)
            && string.IsNullOrWhiteSpace(registeredBusinessScope)
            && string.IsNullOrWhiteSpace(industry)
            && string.IsNullOrWhiteSpace(csrcIndustry)
            && string.IsNullOrWhiteSpace(region))
        {
            return Array.Empty<string>();
        }

        try
        {
            var prompt = BuildProductMarketRecognitionPrompt(
                symbol,
                localFacts.Name,
                mainBusiness,
                businessScope,
                registeredBusinessScope,
                industry,
                csrcIndustry,
                region,
                localEvidence,
                externalEvidence);
            var result = await _llmService.ChatAsync(
                _stockSyncOptions.AiProvider,
                new LlmChatRequest(prompt, _stockSyncOptions.AiModel, 0.1, false),
                cancellationToken);
            return ParseProductMarketRecognitionDirections(result.Content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StockProductMcp 市场认可方向归纳失败，已回退基础业务摘要，symbol={Symbol}", symbol);
            return Array.Empty<string>();
        }
    }

    private async Task<IReadOnlyList<ProductMarketRecognitionExternalEvidencePromptItem>> SearchProductMarketRecognitionEvidenceAsync(
        string symbol,
        string? name,
        CancellationToken cancellationToken)
    {
        var apiKey = await ResolveSearchApiKeyAsync(cancellationToken);
        var searchEnabled = _searchOptions.Enabled || !string.IsNullOrWhiteSpace(apiKey);
        if (!searchEnabled || string.IsNullOrWhiteSpace(apiKey))
        {
            return Array.Empty<ProductMarketRecognitionExternalEvidencePromptItem>();
        }

        try
        {
            var queryName = FirstNonEmpty(name, symbol);
            var query = string.Concat(queryName, " ", symbol, " 题材 概念 板块").Trim();
            var httpClient = _httpClientFactory.CreateClient();
            using var response = await httpClient.PostAsJsonAsync(
                _searchOptions.BaseUrl.TrimEnd('/') + "/search",
                new
                {
                    api_key = apiKey,
                    query,
                    max_results = 3,
                    search_depth = "advanced",
                    include_answer = false,
                    include_raw_content = false
                },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("StockProductMcp 外部搜索失败，status={StatusCode}，symbol={Symbol}", (int)response.StatusCode, symbol);
                return Array.Empty<ProductMarketRecognitionExternalEvidencePromptItem>();
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!document.RootElement.TryGetProperty("results", out var node) || node.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<ProductMarketRecognitionExternalEvidencePromptItem>();
            }

            return node.EnumerateArray()
                .Select(item => new ProductMarketRecognitionExternalEvidencePromptItem(
                    ReadString(item, "title") ?? string.Empty,
                    ReadString(item, "source") ?? ExtractHost(ReadString(item, "url")) ?? "external",
                    ReadString(item, "url"),
                    ParseExternalPublishedAt(ReadString(item, "published_date")),
                    TruncateForPrompt(ReadString(item, "content"), 240)))
                .Where(item => !string.IsNullOrWhiteSpace(item.Title))
                .Take(3)
                .ToArray();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "StockProductMcp 外部搜索异常，已忽略搜索补充证据，symbol={Symbol}", symbol);
            return Array.Empty<ProductMarketRecognitionExternalEvidencePromptItem>();
        }
    }

    private static string BuildProductMarketRecognitionPrompt(
        string symbol,
        string? name,
        string? mainBusiness,
        string? businessScope,
        string? registeredBusinessScope,
        string? industry,
        string? csrcIndustry,
        string? region,
        IReadOnlyList<ProductMarketRecognitionLocalEvidencePromptItem> localEvidence,
        IReadOnlyList<ProductMarketRecognitionExternalEvidencePromptItem> externalEvidence)
    {
        var payload = new ProductMarketRecognitionPromptPayload(
            symbol,
            NormalizeNullableText(name),
            NormalizeNullableText(mainBusiness),
            NormalizeNullableText(businessScope),
            NormalizeNullableText(registeredBusinessScope),
            NormalizeNullableText(industry),
            NormalizeNullableText(csrcIndustry),
            NormalizeNullableText(region),
            localEvidence,
            externalEvidence);

        var builder = new StringBuilder();
        builder.AppendLine("你是 A 股市场题材归纳助手。目标不是复述工商字段，而是基于证据归纳这家公司当前更可能被市场认可的方向。");
        builder.AppendLine("只返回 JSON object，不要 Markdown，不要解释。");
        builder.AppendLine("输出 schema：");
        builder.AppendLine("{\"marketRecognitionDirections\":[\"方向1\",\"方向2\"],\"reason\":\"一句中文理由\"}");
        builder.AppendLine("规则：");
        builder.AppendLine("1. marketRecognitionDirections 最多 3 个，必须是中文短语，可包含常见缩写，但不要写成长句。");
        builder.AppendLine("2. 只有证据较充分时才输出；如果证据不足或存在明显歧义，请返回空数组。");
        builder.AppendLine("3. 不要把经营范围、所属行业、所属地区直接冒充为市场认可方向；只有当新闻/搜索证据明确指向市场归类时才可提炼。");
        builder.AppendLine("4. 优先参考最近本地 stockNews / sectorReports，其次参考 externalSearchEvidence。");
        builder.AppendLine("5. 不要编造概念、不要输出‘未知’、‘其他’、‘无明确靶点’之类占位词。");
        builder.AppendLine("输入 JSON：");
        builder.Append(JsonSerializer.Serialize(payload, ProductMarketRecognitionJsonOptions));
        return builder.ToString();
    }

    private static IReadOnlyList<string> ParseProductMarketRecognitionDirections(string content)
    {
        var json = ExtractJsonObject(content);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("marketRecognitionDirections", out var directionsNode)
            || directionsNode.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in directionsNode.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var normalized = NormalizeMarketRecognitionDirection(item.GetString());
            if (!string.IsNullOrWhiteSpace(normalized) && seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }

        return result.Take(3).ToArray();
    }

    private static string? NormalizeMarketRecognitionDirection(string? value)
    {
        var normalized = NormalizeNullableText(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        normalized = string.Join(' ', normalized
            .Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        normalized = normalized.Trim().Trim('"', '\'', '“', '”', '‘', '’', '：', ':', '；', ';', '，', ',', '。', '、', '|', ' ');
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > 24 ? null : normalized;
    }

    private static string? ExtractJsonObject(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

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
        return objectStart >= 0 && objectEnd >= objectStart
            ? cleaned.Substring(objectStart, objectEnd - objectStart + 1)
            : null;
    }

    private static string? TruncateForPrompt(string? value, int maxLength)
    {
        var normalized = NormalizeNullableText(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
    }

    private static string? ResolveProductEvidenceBusinessSummary(
        string? mainBusiness,
        string? businessScope,
        string? registeredBusinessScope,
        string? industry,
        string? csrcIndustry)
    {
        if (!string.IsNullOrWhiteSpace(mainBusiness))
        {
            return mainBusiness.Trim();
        }

        if (!string.IsNullOrWhiteSpace(businessScope)
            && !string.Equals(businessScope, registeredBusinessScope, StringComparison.OrdinalIgnoreCase))
        {
            return businessScope.Trim();
        }

        var industryBusinessSummary = BuildIndustryBusinessSummary(industry, csrcIndustry);
        if (!string.IsNullOrWhiteSpace(industryBusinessSummary))
        {
            return industryBusinessSummary;
        }

        return null;
    }

    private static IReadOnlyList<StockCopilotMcpFeatureDto> BuildFundamentalFeatures(int factCount, DateTime? updatedAt)
    {
        return new[]
        {
            new StockCopilotMcpFeatureDto("factCount", "Fact Count", "number", factCount, null, null, "Total number of structured fundamental facts available before applying factSkip/factTake."),
            new StockCopilotMcpFeatureDto("updatedAt", "Updated At", "text", null, ChinaTimeZone.ToChina(updatedAt)?.ToString("yyyy-MM-dd HH:mm:ss"), null, "Timestamp of the latest fundamental snapshot.")
        };
    }

    private static IReadOnlyList<StockCopilotMcpFeatureDto> BuildShareholderFeatures(int? shareholderCount, int factCount, DateTime? updatedAt)
    {
        return new[]
        {
            new StockCopilotMcpFeatureDto("shareholderCount", "Shareholder Count", "number", shareholderCount, null, null, "Latest shareholder count extracted from cache or upstream snapshot."),
            new StockCopilotMcpFeatureDto("factCount", "Shareholder Fact Count", "number", factCount, null, null, "Number of shareholder-specific facts retained in the response."),
            new StockCopilotMcpFeatureDto("updatedAt", "Updated At", "text", null, ChinaTimeZone.ToChina(updatedAt)?.ToString("yyyy-MM-dd HH:mm:ss"), null, "Timestamp of the latest shareholder-related snapshot.")
        };
    }

    /// <summary>
    /// Bundle of commonly fetched symbol data, used by KlineMcp/MinuteMcp/StrategyMcp.
    /// Extensible: add new data fields here when new data types are introduced.
    /// </summary>
    private sealed record SymbolDataBundle(
        StockQuoteDto Quote,
        IReadOnlyList<KLinePointDto> KLines,
        IReadOnlyList<MinuteLinePointDto> MinuteLines,
        IReadOnlyList<IntradayMessageDto> Messages,
        LocalFactPackageDto LocalFacts);

    /// <summary>
    /// Fetches all common symbol data in parallel. To add new data types,
    /// add a new task and field to SymbolDataBundle.
    /// </summary>
    private async Task<SymbolDataBundle> FetchSymbolDataBundleAsync(
        string symbol, string interval, int count, string? source, CancellationToken ct)
    {
        var quoteTask = _dataService.GetQuoteAsync(symbol, source, ct);
        var kLineTask = _dataService.GetKLineAsync(symbol, interval, count, source, ct);
        var minuteTask = _dataService.GetMinuteLineAsync(symbol, source, ct);
        var messagesTask = _dataService.GetIntradayMessagesAsync(symbol, source, ct);
        var localFactsTask = _queryLocalFactDatabaseTool.QueryAsync(symbol, ct);

        await Task.WhenAll(quoteTask, kLineTask, minuteTask, messagesTask, localFactsTask);

        return new SymbolDataBundle(
            await quoteTask,
            await kLineTask,
            await minuteTask,
            await messagesTask,
            await localFactsTask);
    }

    private async Task EnsureSymbolFactsRefreshedAsync(string symbol, CancellationToken cancellationToken)
    {
        if (_localFactIngestionService is null || string.IsNullOrWhiteSpace(symbol))
        {
            return;
        }

        // Skip if this symbol was refreshed recently (avoids redundant crawls
        // when multiple parallel analysts call MCP for the same symbol).
        if (RecentRefreshTimestamps.TryGetValue(symbol, out var lastRefresh)
            && DateTime.UtcNow - lastRefresh < RefreshSkipWindow)
        {
            return;
        }

        await _localFactIngestionService.EnsureFreshAsync(symbol, cancellationToken);
        RecentRefreshTimestamps[symbol] = DateTime.UtcNow;
    }

    private async Task EnsureMarketFactsRefreshedAsync(CancellationToken cancellationToken)
    {
        if (_localFactIngestionService is null)
        {
            return;
        }

        await _localFactIngestionService.EnsureMarketFreshAsync(cancellationToken);
    }

    private async Task<StockCopilotMcpMarketContextDto?> ResolveMcpMarketContextAsync(string symbol, CancellationToken cancellationToken)
    {
        string? sectorNameHint = null;
        try
        {
            var quote = await _dataService.GetQuoteAsync(symbol, null, cancellationToken);
            sectorNameHint = quote.SectorName;
        }
        catch
        {
            // Quote fetch may fail; proceed without hint
        }

        var marketContext = await _marketContextService.GetLatestAsync(symbol, sectorNameHint, cancellationToken);
        return ToCopilotMarketContext(marketContext);
    }

    /// <summary>
    /// Resolves market context using an already-known sector name hint,
    /// avoiding a redundant quote fetch when the caller already has the quote.
    /// </summary>
    private async Task<StockCopilotMcpMarketContextDto?> ResolveMcpMarketContextAsync(string symbol, string? sectorNameHint, CancellationToken cancellationToken)
    {
        var marketContext = await _marketContextService.GetLatestAsync(symbol, sectorNameHint, cancellationToken);
        return ToCopilotMarketContext(marketContext);
    }

    private static StockCopilotMcpMarketContextDto? RedactProgrammaticMarketContext(StockMarketContextDto? marketContext)
    {
        if (marketContext is null)
        {
            return null;
        }

        return new StockCopilotMcpMarketContextDto(
            marketContext.StageConfidence,
            marketContext.StockSectorName,
            marketContext.MainlineSectorName,
            marketContext.SectorCode,
            marketContext.MainlineScore);
    }

    private static StockCopilotMcpMarketContextDto? ToCopilotMarketContext(StockMarketContextDto? marketContext)
    {
        return RedactProgrammaticMarketContext(marketContext);
    }

    private static IReadOnlyList<StockFundamentalFactDto> OrderFundamentalFacts(IReadOnlyList<StockFundamentalFactDto> facts)
    {
        return facts
            .Select((item, index) => new { Item = item, Index = index })
            .OrderBy(entry => GetFundamentalSortBucket(entry.Item))
            .ThenBy(entry => GetFundamentalLabelOrder(entry.Item))
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Item)
            .ToArray();
    }

    private static int GetFundamentalSortBucket(StockFundamentalFactDto fact)
    {
        if (string.Equals(fact.Source, LatestFinanceFactSource, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return IsProductFact(fact) ? 2 : 1;
    }

    private static int GetFundamentalLabelOrder(StockFundamentalFactDto fact)
    {
        return FundamentalLabelOrder.TryGetValue(fact.Label, out var order) ? order : int.MaxValue;
    }

    private static string? ResolveProductBusinessScope(string? mainBusiness, string? registeredBusinessScope, string? industry, string? csrcIndustry)
    {
        if (!string.IsNullOrWhiteSpace(mainBusiness))
        {
            return mainBusiness.Trim();
        }

        var industryBusinessSummary = BuildIndustryBusinessSummary(industry, csrcIndustry);
        if (!string.IsNullOrWhiteSpace(industryBusinessSummary))
        {
            return industryBusinessSummary;
        }

        if (!string.IsNullOrWhiteSpace(registeredBusinessScope))
        {
            return registeredBusinessScope.Trim();
        }

        return null;
    }

    private static string? BuildIndustryBusinessSummary(string? industry, string? csrcIndustry)
    {
        var primaryIndustry = FirstNonEmpty(NormalizeNullableText(industry), NormalizeNullableText(csrcIndustry));
        return string.IsNullOrWhiteSpace(primaryIndustry) ? null : $"以{primaryIndustry}相关业务为主";
    }

    private static StockCopilotMcpWindowOptions NormalizeWindowOptions(StockCopilotMcpWindowOptions? window)
    {
        var safeEvidenceSkip = Math.Max(0, window?.EvidenceSkip ?? 0);
        var safeEvidenceTake = NormalizeTake(window?.EvidenceTake);
        var safeFactSkip = Math.Max(0, window?.FactSkip ?? 0);
        var safeFactTake = NormalizeTake(window?.FactTake);
        return new StockCopilotMcpWindowOptions(safeEvidenceSkip, safeEvidenceTake, safeFactSkip, safeFactTake);
    }

    private static int? NormalizeTake(int? requested)
    {
        if (!requested.HasValue)
        {
            return null;
        }

        return requested.Value < 0 ? 0 : requested.Value;
    }

    private static IReadOnlyList<T> ApplyWindow<T>(IReadOnlyList<T> items, int skip, int? take)
    {
        ArgumentNullException.ThrowIfNull(items);

        var safeSkip = Math.Max(0, skip);
        var safeTake = NormalizeTake(take);
        IEnumerable<T> query = items.Skip(safeSkip);
        if (safeTake.HasValue)
        {
            query = query.Take(safeTake.Value);
        }

        return query.ToArray();
    }

    private static string FormatOptionalNumber(decimal? value)
    {
        return value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : "未知";
    }

    private async Task<string?> ResolveSearchApiKeyAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_searchOptions.ApiKey))
        {
            return _searchOptions.ApiKey.Trim();
        }

        if (_llmSettingsStore is not null)
        {
            var activeProvider = await _llmSettingsStore.GetActiveProviderKeyAsync(cancellationToken);
            var settings = await _llmSettingsStore.GetProviderAsync(activeProvider, cancellationToken);
            if (!string.IsNullOrWhiteSpace(settings?.TavilyApiKey))
            {
                return settings.TavilyApiKey.Trim();
            }

            // Tavily API Key is global: fall back to any provider
            var globalKey = await _llmSettingsStore.GetGlobalTavilyKeyAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(globalKey))
            {
                return globalKey;
            }
        }

        var envKey = Environment.GetEnvironmentVariable("TAVILY_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            return envKey.Trim();
        }

        return null;
    }

    private sealed record ProductMarketRecognitionPromptPayload(
        string Symbol,
        string? Name,
        string? MainBusiness,
        string? BusinessScope,
        string? RegisteredBusinessScope,
        string? Industry,
        string? CsrcIndustry,
        string? Region,
        IReadOnlyList<ProductMarketRecognitionLocalEvidencePromptItem> LocalEvidence,
        IReadOnlyList<ProductMarketRecognitionExternalEvidencePromptItem> ExternalSearchEvidence);

    private sealed record ProductMarketRecognitionLocalEvidencePromptItem(
        string Title,
        string? TranslatedTitle,
        string Source,
        DateTime PublishedAt,
        string? AiTarget,
        IReadOnlyList<string> AiTags,
        string? Summary);

    private sealed record ProductMarketRecognitionExternalEvidencePromptItem(
        string Title,
        string Source,
        string? Url,
        DateTime? PublishedAt,
        string? Summary);
}