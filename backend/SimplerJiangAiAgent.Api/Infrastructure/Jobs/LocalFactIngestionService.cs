using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

public interface ILocalFactIngestionService
{
    Task SyncAsync(CancellationToken cancellationToken = default);
    Task EnsureMarketFreshAsync(CancellationToken cancellationToken = default);
    Task EnsureFreshAsync(string symbol, CancellationToken cancellationToken = default);
}

public sealed class LocalFactIngestionService : ILocalFactIngestionService
{
    private const string SinaRollUrl = "https://feed.mix.sina.com.cn/api/roll/get?pageid=153&lid=2509&num=60&versionNumber=1.2.8.1";
    private const int MarketNewsMaxAcceptedAgeDays = 3;
    private static readonly SemaphoreSlim MarketRefreshGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SymbolRefreshGates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DateTime> SymbolCrawlTimestamps = new(StringComparer.OrdinalIgnoreCase);
    private static long _lastMarketCrawlTicks = DateTime.MinValue.Ticks;
    private static readonly TimeSpan CrawlSkipWindow = TimeSpan.FromMinutes(2);
    private static readonly (string Url, string Source, string SourceTag)[] MarketRssFeeds =
    {
        // ── Tier 1: Google News RSS – universal aggregator (requires international network) ──
        ("https://news.google.com/rss/search?q=A%E8%82%A1+%E5%A4%A7%E7%9B%98+%E8%82%A1%E5%B8%82&hl=zh-CN&gl=CN&ceid=CN:zh-Hans", "Google News CN Stocks", "gnews-cn-stocks"),
        ("https://news.google.com/rss/search?q=%E8%AF%81%E5%88%B8+%E5%9F%BA%E9%87%91+%E6%8A%95%E8%B5%84&hl=zh-CN&gl=CN&ceid=CN:zh-Hans", "Google News CN Finance", "gnews-cn-finance"),
        ("https://news.google.com/rss/search?q=%E5%A4%AE%E8%A1%8C+%E8%B4%A7%E5%B8%81%E6%94%BF%E7%AD%96+%E5%88%A9%E7%8E%87&hl=zh-CN&gl=CN&ceid=CN:zh-Hans", "Google News CN Macro", "gnews-cn-macro"),
        ("https://news.google.com/rss/search?q=stock+market+Wall+Street&hl=en-US&gl=US&ceid=US:en", "Google News US Stocks", "gnews-us-stocks"),
        ("https://news.google.com/rss/search?q=Federal+Reserve+interest+rate+economy&hl=en-US&gl=US&ceid=US:en", "Google News US Macro", "gnews-us-macro"),
        ("https://news.google.com/rss/search?q=global+economy+trade+tariff&hl=en-US&gl=US&ceid=US:en", "Google News Global Macro", "gnews-global-macro"),

        // ── Tier 2: Google News site-specific proxies (requires international network) ──
        ("https://news.google.com/rss/search?q=site:reuters.com+markets+economy&hl=en-US&gl=US&ceid=US:en", "Google News Reuters", "gnews-reuters"),
        ("https://news.google.com/rss/search?q=site:bloomberg.com+markets+economy&hl=en-US&gl=US&ceid=US:en", "Google News Bloomberg", "gnews-bloomberg"),
        ("https://news.google.com/rss/search?q=site:ft.com+markets+economy&hl=en-US&gl=US&ceid=US:en", "Google News FT", "gnews-ft"),
        ("https://news.google.com/rss/search?q=site:wsj.com+markets+stocks&hl=en-US&gl=US&ceid=US:en", "Google News WSJ", "gnews-wsj"),

        // ── Tier 3: Direct financial media RSS ──
        ("https://www.cnbc.com/id/10000664/device/rss/rss.html", "CNBC Finance", "cnbc-finance-rss"),
        ("https://www.cnbc.com/id/100003114/device/rss/rss.html", "CNBC US Markets", "cnbc-us-markets-rss"),
        ("https://www.cnbc.com/id/19836768/device/rss/rss.html", "CNBC Economy", "cnbc-economy-rss"),
        ("https://www.cnbc.com/id/15839135/device/rss/rss.html", "CNBC World", "cnbc-world-rss"),
        ("https://feeds.marketwatch.com/marketwatch/topstories/", "MarketWatch", "marketwatch-top-rss"),
        ("https://feeds.marketwatch.com/marketwatch/marketpulse/", "MarketWatch Pulse", "marketwatch-pulse-rss"),
        ("https://feeds.bbci.co.uk/news/business/rss.xml", "BBC Business", "bbc-business-rss"),
        ("https://rss.nytimes.com/services/xml/rss/nyt/Business.xml", "NYT Business", "nyt-business-rss"),
        ("https://seekingalpha.com/feed.xml", "Seeking Alpha", "seeking-alpha-rss"),
        ("https://www.investing.com/rss/news.rss", "Investing.com", "investing-com-rss"),
        ("https://feeds.skynews.com/feeds/rss/business.xml", "Sky News Business", "sky-business-rss"),

        // ── Tier 4: Crypto ──
        ("https://cointelegraph.com/rss", "CoinTelegraph", "cointelegraph-rss")
    };
    private static readonly string[] BlockedSourceKeywords = { "自媒体" };
    private static readonly string[] BlockedTitlePatterns =
    {
        "Earnings Call Transcript",
        "Earnings Call Slides",
        "Conference Call Transcript"
    };
    private static readonly string[] MarketKeywords =
    {
        "A股", "大盘", "沪指", "深成指", "创业板", "科创板", "两市", "收盘", "午评", "早评", "北向资金", "指数"
    };
    private static readonly string[] FinanceRelevanceKeywords =
    {
        // English finance keywords
        "stock", "market", "economy", "economic", "fed ", "federal reserve", "interest rate",
        "inflation", "gdp", "trade", "tariff", "oil", "gold", "bond", "yield", "earnings",
        "profit", "revenue", "ipo", "merger", "bank", "fiscal", "deficit", "surplus",
        "recession", "rally", "crash", "bull", "bear", "dow", "s&p", "nasdaq", "ftse",
        "investor", "hedge", "commodity", "currency", "forex", "crypto", "bitcoin",
        "wall street", "treasury", "sanction", "chipmaker", "semiconductor",
        // Chinese finance keywords
        "股", "市场", "经济", "利率", "通胀", "贸易", "关税", "央行", "基金", "证券",
        "投资", "盈利", "营收", "上市", "收购", "银行", "债券", "油价", "金价",
        "半导体", "芯片", "制裁", "港股", "美股", "欧股", "汇率", "人民币",
        "券商", "净利润", "北交所", "沪深", "原油", "业绩", "期货", "盘面", "大盘",
        "标普", "恒指", "日元", "美元", "英镑", "欧元", "纳指", "道指"
    };
    private const string EastmoneyMarketNewsUrlTemplate = "https://np-listapi.eastmoney.com/comm/web/getNewsByColumns?client=web&biz=web_news_col&column={0}&order=1&needInteractData=0&page_index=1&page_size=20&req_trace=";
    private static readonly (int Column, string SourceTag)[] EastmoneyMarketColumns =
    {
        (1350, "eastmoney-market-news"),   // 全球市场
        (347,  "eastmoney-ashare-news"),   // A股要闻/研究
    };
    private const string ClsTelegraphUrl = "https://www.cls.cn/nodeapi/updateTelegraphList?app=CailianpressWeb&os=web&sv=8.4.6&rn=30";

    private readonly AppDbContext _dbContext;
    private readonly HttpClient _httpClient;
    private readonly StockSyncOptions _options;
    private readonly ILocalFactAiEnrichmentService _aiEnrichmentService;
    private readonly ILogger<LocalFactIngestionService> _logger;

    public LocalFactIngestionService(
        AppDbContext dbContext,
        HttpClient httpClient,
        IOptions<StockSyncOptions> options,
        ILocalFactAiEnrichmentService aiEnrichmentService,
        ILogger<LocalFactIngestionService> logger)
    {
        _dbContext = dbContext;
        _httpClient = httpClient;
        _options = options.Value;
        _aiEnrichmentService = aiEnrichmentService;
        _logger = logger;
    }

    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        var symbols = await GetTrackedSymbolsAsync(cancellationToken);
        if (symbols.Count == 0)
        {
            return;
        }

        var crawledAt = DateTime.UtcNow;
        var marketReports = await FetchMarketReportsAsync(crawledAt, cancellationToken);

        await MarketRefreshGate.WaitAsync(cancellationToken);
        try
        {
            await UpsertMarketReportsAsync(marketReports, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _aiEnrichmentService.ProcessMarketPendingAsync(cancellationToken);
        }
        finally
        {
            MarketRefreshGate.Release();
        }

        foreach (var symbol in symbols)
        {
            var symbolGate = GetSymbolGate(symbol);
            await symbolGate.WaitAsync(cancellationToken);
            try
            {
                await SyncSymbolAsync(symbol, crawledAt, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                await _aiEnrichmentService.ProcessSymbolPendingAsync(symbol, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "同步本地事实失败: {Symbol}", symbol);
            }
            finally
            {
                symbolGate.Release();
            }
        }
    }

    public async Task EnsureFreshAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = StockSymbolNormalizer.Normalize(symbol);
        await EnsureMarketFreshAsync(cancellationToken);

        var symbolGate = GetSymbolGate(normalized);
        await symbolGate.WaitAsync(cancellationToken);
        try
        {
            // Skip the network crawl if we synced this symbol recently,
            // but always process pending AI enrichment rows below.
            var skipCrawl = SymbolCrawlTimestamps.TryGetValue(normalized, out var lastCrawl)
                && DateTime.UtcNow - lastCrawl < CrawlSkipWindow;

            if (!skipCrawl)
            {
                try
                {
                    var crawledAt = DateTime.UtcNow;
                    await SyncSymbolAsync(normalized, crawledAt, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "按需刷新本地事实失败，回退到现有缓存数据: {Symbol}", normalized);
                }
                SymbolCrawlTimestamps[normalized] = DateTime.UtcNow;
            }

            await _aiEnrichmentService.ProcessSymbolPendingAsync(normalized, cancellationToken);
        }
        finally
        {
            symbolGate.Release();
        }
    }

    public async Task EnsureMarketFreshAsync(CancellationToken cancellationToken = default)
    {
        await MarketRefreshGate.WaitAsync(cancellationToken);
        try
        {
            var lastCrawl = new DateTime(Interlocked.Read(ref _lastMarketCrawlTicks), DateTimeKind.Utc);
            if (DateTime.UtcNow - lastCrawl < CrawlSkipWindow)
            {
                // Skip crawl but still process pending AI enrichment
                await _aiEnrichmentService.ProcessMarketPendingAsync(cancellationToken);
                return;
            }

            var crawledAt = DateTime.UtcNow;
            var marketReports = await FetchMarketReportsAsync(crawledAt, cancellationToken);
            await UpsertMarketReportsAsync(marketReports, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _aiEnrichmentService.ProcessMarketPendingAsync(cancellationToken);
            Interlocked.Exchange(ref _lastMarketCrawlTicks, DateTime.UtcNow.Ticks);
        }
        finally
        {
            MarketRefreshGate.Release();
        }
    }

    private static SemaphoreSlim GetSymbolGate(string symbol)
    {
        return SymbolRefreshGates.GetOrAdd(symbol, _ => new SemaphoreSlim(1, 1));
    }

    private async Task<IReadOnlyList<string>> GetTrackedSymbolsAsync(CancellationToken cancellationToken)
    {
        var configured = _options.Symbols
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => StockSymbolNormalizer.Normalize(item.Trim()));

        var recent = await _dbContext.StockQueryHistories
            .OrderByDescending(item => item.UpdatedAt)
            .Select(item => item.Symbol)
            .Take(20)
            .ToListAsync(cancellationToken);

        return configured
            .Concat(recent.Select(StockSymbolNormalizer.Normalize))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task SyncSymbolAsync(
        string symbol,
        DateTime crawledAt,
        CancellationToken cancellationToken)
    {
        var profile = await FetchCompanyProfileAsync(symbol, cancellationToken);
        var announcementJson = await _httpClient.GetStringAsync(BuildAnnouncementUrl(symbol), cancellationToken);
        var announcementItems = EastmoneyAnnouncementParser.Parse(symbol, profile.Name, profile.SectorName, announcementJson, crawledAt);

        var companyHtml = await _httpClient.GetStringAsync($"https://finance.sina.com.cn/realstock/company/{symbol}/nc.shtml", cancellationToken);
        var companyNews = SinaCompanyNewsParser.ParseCompanyNews(companyHtml)
            .Select(item => new LocalStockNewsSeed(
                symbol,
                profile.Name,
                profile.SectorName,
                item.Title,
                "company_news",
                item.Source,
                "sina-company-news",
                item.Url,
                item.PublishedAt,
                crawledAt,
                item.Url))
            .ToArray();

        await UpsertStockNewsAsync(symbol, announcementItems.Concat(companyNews).ToArray(), cancellationToken);
        var sectorReports = await FetchSectorReportsAsync(symbol, profile.SectorName, crawledAt, cancellationToken);
        await UpsertSectorReportsAsync(symbol, sectorReports, cancellationToken);
    }

    private async Task<EastmoneyCompanyProfileDto> FetchCompanyProfileAsync(string symbol, CancellationToken cancellationToken)
    {
        var marketPrefix = symbol.StartsWith("sh", StringComparison.OrdinalIgnoreCase) ? "SH" : "SZ";
        var code = symbol[2..];
        var surveyUrl = $"https://emweb.securities.eastmoney.com/PC_HSF10/CompanySurvey/CompanySurveyAjax?code={marketPrefix}{code}";
        var shareholderUrl = $"https://emweb.securities.eastmoney.com/PC_HSF10/ShareholderResearch/PageAjax?code={marketPrefix}{code}";
        var surveyTask = _httpClient.GetStringAsync(surveyUrl, cancellationToken);
        var shareholderTask = TryGetStringAsync(shareholderUrl, cancellationToken);
        await Task.WhenAll(new Task[] { surveyTask, shareholderTask });
        return EastmoneyCompanyProfileParser.Parse(symbol, await surveyTask, await shareholderTask);
    }

    private async Task<string?> TryGetStringAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.GetStringAsync(url, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<IntradayMessageDto>> FetchRollMessagesAsync(CancellationToken cancellationToken)
    {
        var json = await _httpClient.GetStringAsync(SinaRollUrl, cancellationToken);
        return SinaRollParser.ParseRollMessages(json, string.Empty)
            .OrderByDescending(item => item.PublishedAt)
            .ToArray();
    }

    private async Task<IReadOnlyList<LocalSectorReportSeed>> FetchSectorReportsAsync(
        string symbol,
        string? sectorName,
        DateTime crawledAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sectorName))
        {
            return Array.Empty<LocalSectorReportSeed>();
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildSectorSearchUrl(sectorName));
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Referer", "https://finance.sina.com.cn/");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0 Safari/537.36");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            var reports = SinaSectorNewsSearchParser.Parse(symbol, sectorName, content, crawledAt);
            if (reports.Count == 0)
            {
                _logger.LogWarning("新浪板块搜索未返回可解析结果: {SectorName}", sectorName);
            }

            return reports;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Array.Empty<LocalSectorReportSeed>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "抓取板块资讯失败: {SectorName}", sectorName);
            return Array.Empty<LocalSectorReportSeed>();
        }
    }

    private async Task<IReadOnlyList<LocalSectorReportSeed>> FetchMarketReportsAsync(
        DateTime crawledAt,
        CancellationToken cancellationToken)
    {
        var domesticTask = FetchRollMessagesSafeAsync(cancellationToken);
        var eastmoneyTask = FetchEastmoneyMarketNewsAsync(crawledAt, cancellationToken);
        var clsTask = FetchClsTelegraphAsync(crawledAt, cancellationToken);
        var rssTasks = MarketRssFeeds
            .Select(feed => FetchMarketFeedAsync(feed.Url, feed.Source, feed.SourceTag, crawledAt, cancellationToken))
            .ToArray();

        var rssResults = await Task.WhenAll(rssTasks);
        var domesticReports = BuildDomesticMarketReports(await domesticTask, crawledAt);
        var eastmoneyReports = await eastmoneyTask;
        var clsReports = await clsTask;
        var allItems = rssResults
            .SelectMany(items => items)
            .Concat(domesticReports)
            .Concat(eastmoneyReports)
            .Concat(clsReports)
            .Where(item => !IsBlockedSource(item.Source))
            .DistinctBy(item => item.Url ?? item.ExternalId ?? item.Title)
            .ToArray();

        // Phase 1: guarantee at least 2 items per source for broad coverage
        var diversityPicks = allItems
            .GroupBy(item => item.SourceTag)
            .SelectMany(group => group.OrderByDescending(item => item.PublishTime).Take(2))
            .ToList();

        var pickedKeys = new HashSet<string>(
            diversityPicks.Select(item => item.Url ?? item.ExternalId ?? item.Title ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);

        // Phase 2: fill remaining slots with freshest from any source
        var fillPicks = allItems
            .Where(item => !pickedKeys.Contains(item.Url ?? item.ExternalId ?? item.Title ?? string.Empty))
            .OrderByDescending(item => item.PublishTime);

        var reports = diversityPicks
            .Concat(fillPicks)
            .OrderByDescending(item => item.PublishTime)
            .Take(150)
            .ToArray();

        if (reports.Length > 0)
        {
            return reports;
        }

        return domesticReports;
    }

    private async Task<IReadOnlyList<IntradayMessageDto>> FetchRollMessagesSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await FetchRollMessagesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Array.Empty<IntradayMessageDto>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "抓取新浪滚动大盘资讯失败");
            return Array.Empty<IntradayMessageDto>();
        }
    }

    private async Task<IReadOnlyList<LocalSectorReportSeed>> FetchMarketFeedAsync(
        string url,
        string source,
        string sourceTag,
        DateTime crawledAt,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildCacheBustedUrl(url, crawledAt));
            request.Headers.TryAddWithoutValidation("Accept", "application/rss+xml,application/xml,text/xml,*/*");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0 Safari/537.36");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            return RssMarketNewsParser.Parse(content, source, sourceTag, crawledAt)
                .Where(item => !IsBlockedSource(item.Source))
                .Where(item => !IsBlockedTitle(item.Title))
                .Where(item => IsFinanceRelevant(item.Title))
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Array.Empty<LocalSectorReportSeed>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "抓取宏观 RSS 失败: {Source}", source);
            return Array.Empty<LocalSectorReportSeed>();
        }
    }

    private async Task<IReadOnlyList<LocalSectorReportSeed>> FetchEastmoneyMarketNewsAsync(
        DateTime crawledAt,
        CancellationToken cancellationToken)
    {
        var allItems = new List<LocalSectorReportSeed>();
        foreach (var (column, sourceTag) in EastmoneyMarketColumns)
        {
            try
            {
                var traceId = Guid.NewGuid().ToString("N");
                var url = string.Format(EastmoneyMarketNewsUrlTemplate, column) + traceId;
                var json = await _httpClient.GetStringAsync(url, cancellationToken);
                var items = EastmoneyMarketNewsParser.Parse(json, sourceTag, crawledAt)
                    .Where(item => IsFinanceRelevant(item.Title))
                    .ToArray();
                allItems.AddRange(items);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return allItems;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "抓取东方财富大盘资讯失败: column={Column}", column);
            }
        }

        return allItems;
    }

    private async Task<IReadOnlyList<LocalSectorReportSeed>> FetchClsTelegraphAsync(
        DateTime crawledAt,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ClsTelegraphUrl);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0 Safari/537.36");
            request.Headers.TryAddWithoutValidation("Referer", "https://www.cls.cn/");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ClsTelegraphParser.Parse(json, crawledAt)
                .Where(item => IsFinanceRelevant(item.Title))
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Array.Empty<LocalSectorReportSeed>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "抓取财联社电报失败");
            return Array.Empty<LocalSectorReportSeed>();
        }
    }

    private static string BuildAnnouncementUrl(string symbol)
    {
        var code = symbol[2..];
        return $"https://np-anotice-stock.eastmoney.com/api/security/ann?page_size=30&page_index=1&ann_type=A&client_source=web&stock_list={code}";
    }

    private static string BuildSectorSearchUrl(string sectorName)
    {
        return $"https://search.sina.com.cn/?q={Uri.EscapeDataString(sectorName)}&c=news";
    }

    private static string BuildCacheBustedUrl(string url, DateTime crawledAt)
    {
        var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{url}{separator}t={EnsureUtc(crawledAt):yyyyMMddHHmmss}";
    }

    private async Task UpsertStockNewsAsync(string symbol, IReadOnlyList<LocalStockNewsSeed> items, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.LocalStockNews
            .Where(item => item.Symbol == symbol)
            .ToListAsync(cancellationToken);

        MergeStockNewsEntities(existing, items
            .OrderByDescending(item => item.PublishTime)
            .Take(40)
            .ToArray(), _dbContext.LocalStockNews);
    }

    private async Task UpsertSectorReportsAsync(
        string symbol,
        IReadOnlyList<LocalSectorReportSeed> sectorReports,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.LocalSectorReports
            .Where(item => item.Symbol == symbol && item.Level == "sector")
            .ToListAsync(cancellationToken);

        MergeSectorReportEntities(existing, sectorReports, _dbContext.LocalSectorReports);
    }

    private async Task UpsertMarketReportsAsync(
        IReadOnlyList<LocalSectorReportSeed> reports,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.LocalSectorReports
            .Where(item => item.Level == "market")
            .ToListAsync(cancellationToken);

        // Additive merge: add new items and update existing, but never delete
        // items not in the incoming set. This prevents data from intermittent
        // sources (e.g. CLS, Sina) from being removed between refresh cycles.
        var existingLookup = existing.ToDictionary(BuildSectorReportKey, StringComparer.OrdinalIgnoreCase);

        foreach (var item in reports)
        {
            var key = BuildSectorReportKey(item);

            if (existingLookup.TryGetValue(key, out var previous))
            {
                previous.Symbol = item.Symbol;
                previous.SectorName = item.SectorName;
                previous.Level = item.Level;
                previous.Title = item.Title;
                previous.Source = item.Source;
                previous.SourceTag = item.SourceTag;
                previous.ExternalId = item.ExternalId;
                previous.PublishTime = item.PublishTime;
                previous.CrawledAt = item.CrawledAt;
                previous.Url = item.Url;
                continue;
            }

            _dbContext.LocalSectorReports.Add(new LocalSectorReport
            {
                Symbol = item.Symbol,
                SectorName = item.SectorName,
                Level = item.Level,
                Title = item.Title,
                Source = item.Source,
                SourceTag = item.SourceTag,
                ExternalId = item.ExternalId,
                PublishTime = item.PublishTime,
                CrawledAt = item.CrawledAt,
                Url = item.Url,
                IsAiProcessed = false,
                AiSentiment = "中性"
            });
        }

        // Time-based cleanup: remove stale market items older than 7 days
        var staleThreshold = DateTime.UtcNow.AddDays(-7);
        var staleItems = existing.Where(item => item.PublishTime < staleThreshold).ToArray();
        if (staleItems.Length > 0)
        {
            _dbContext.LocalSectorReports.RemoveRange(staleItems);
        }
    }

    internal static void MergeStockNewsEntities(
        IReadOnlyList<LocalStockNews> existing,
        IReadOnlyList<LocalStockNewsSeed> incoming,
        DbSet<LocalStockNews> dbSet)
    {
        var existingLookup = existing.ToDictionary(BuildStockNewsKey, StringComparer.OrdinalIgnoreCase);
        var retainedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in incoming)
        {
            var key = BuildStockNewsKey(item);
            retainedKeys.Add(key);

            if (existingLookup.TryGetValue(key, out var previous))
            {
                previous.Symbol = item.Symbol;
                previous.Name = item.Name;
                previous.SectorName = item.SectorName;
                previous.Title = item.Title;
                previous.Category = item.Category;
                previous.Source = item.Source;
                previous.SourceTag = item.SourceTag;
                previous.ExternalId = item.ExternalId;
                previous.PublishTime = item.PublishTime;
                previous.CrawledAt = item.CrawledAt;
                previous.Url = item.Url;
                continue;
            }

            dbSet.Add(new LocalStockNews
            {
                Symbol = item.Symbol,
                Name = item.Name,
                SectorName = item.SectorName,
                Title = item.Title,
                Category = item.Category,
                Source = item.Source,
                SourceTag = item.SourceTag,
                ExternalId = item.ExternalId,
                PublishTime = item.PublishTime,
                CrawledAt = item.CrawledAt,
                Url = item.Url,
                IsAiProcessed = false,
                AiSentiment = "中性"
            });
        }

        var toRemove = existing
            .Where(item => !retainedKeys.Contains(BuildStockNewsKey(item)))
            .ToArray();

        if (toRemove.Length > 0)
        {
            dbSet.RemoveRange(toRemove);
        }
    }

    internal static void MergeSectorReportEntities(
        IReadOnlyList<LocalSectorReport> existing,
        IReadOnlyList<LocalSectorReportSeed> incoming,
        DbSet<LocalSectorReport> dbSet)
    {
        var existingLookup = existing.ToDictionary(BuildSectorReportKey, StringComparer.OrdinalIgnoreCase);
        var retainedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in incoming)
        {
            var key = BuildSectorReportKey(item);
            retainedKeys.Add(key);

            if (existingLookup.TryGetValue(key, out var previous))
            {
                previous.Symbol = item.Symbol;
                previous.SectorName = item.SectorName;
                previous.Level = item.Level;
                previous.Title = item.Title;
                previous.Source = item.Source;
                previous.SourceTag = item.SourceTag;
                previous.ExternalId = item.ExternalId;
                previous.PublishTime = item.PublishTime;
                previous.CrawledAt = item.CrawledAt;
                previous.Url = item.Url;
                continue;
            }

            dbSet.Add(new LocalSectorReport
            {
                Symbol = item.Symbol,
                SectorName = item.SectorName,
                Level = item.Level,
                Title = item.Title,
                Source = item.Source,
                SourceTag = item.SourceTag,
                ExternalId = item.ExternalId,
                PublishTime = item.PublishTime,
                CrawledAt = item.CrawledAt,
                Url = item.Url,
                IsAiProcessed = false,
                AiSentiment = "中性"
            });
        }

        var toRemove = existing
            .Where(item => !retainedKeys.Contains(BuildSectorReportKey(item)))
            .ToArray();

        if (toRemove.Length > 0)
        {
            dbSet.RemoveRange(toRemove);
        }
    }

    private static string BuildStockNewsKey(LocalStockNewsSeed item)
    {
        return string.IsNullOrWhiteSpace(item.ExternalId)
            ? string.IsNullOrWhiteSpace(item.Url)
                ? $"{item.Symbol}|{item.Title}|{item.PublishTime:O}"
                : item.Url
            : item.ExternalId;
    }

    private static string BuildStockNewsKey(LocalStockNews item)
    {
        return string.IsNullOrWhiteSpace(item.ExternalId)
            ? string.IsNullOrWhiteSpace(item.Url)
                ? $"{item.Symbol}|{item.Title}|{item.PublishTime:O}"
                : item.Url
            : item.ExternalId;
    }

    private static string BuildSectorReportKey(LocalSectorReportSeed item)
    {
        return string.IsNullOrWhiteSpace(item.ExternalId)
            ? string.IsNullOrWhiteSpace(item.Url)
                ? $"{item.Level}|{item.Symbol}|{item.Title}|{item.PublishTime:O}"
                : item.Url
            : item.ExternalId;
    }

    private static string BuildSectorReportKey(LocalSectorReport item)
    {
        return string.IsNullOrWhiteSpace(item.ExternalId)
            ? string.IsNullOrWhiteSpace(item.Url)
                ? $"{item.Level}|{item.Symbol}|{item.Title}|{item.PublishTime:O}"
                : item.Url
            : item.ExternalId;
    }

    internal static IReadOnlyList<LocalSectorReportSeed> BuildDomesticMarketReports(
        IReadOnlyList<IntradayMessageDto> rollMessages,
        DateTime crawledAt)
    {
        var minPublishTime = EnsureUtc(crawledAt).AddDays(-MarketNewsMaxAcceptedAgeDays);
        var matched = rollMessages
            .Where(item => EnsureUtc(item.PublishedAt) >= minPublishTime)
            .Where(item => !IsBlockedSource(item.Source))
            .Where(item => MarketKeywords.Any(keyword => item.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.PublishedAt)
            .Take(12)
            .ToArray();

        var source = matched.Length > 0
            ? matched
            : rollMessages
                .Where(item => EnsureUtc(item.PublishedAt) >= minPublishTime)
                .Where(item => !IsBlockedSource(item.Source))
                .OrderByDescending(item => item.PublishedAt)
                .Take(12)
                .ToArray();

        return source
            .Select(item => new LocalSectorReportSeed(
                null,
                "大盘环境",
                "market",
                item.Title,
                item.Source,
                "sina-roll-market",
                item.Url,
                item.PublishedAt,
                crawledAt,
                item.Url))
            .ToArray();
    }

    private static bool IsBlockedSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        return BlockedSourceKeywords.Any(keyword => source.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBlockedTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        return BlockedTitlePatterns.Any(pattern => title.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsFinanceRelevant(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        return FinanceRelevanceKeywords.Any(keyword => title.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private Task<bool> HasPendingMarketAiAsync(CancellationToken cancellationToken)
    {
        return _dbContext.LocalSectorReports.AnyAsync(item => item.Level == "market" && !item.IsAiProcessed, cancellationToken);
    }

    private Task<bool> HasPendingSymbolAiAsync(string symbol, CancellationToken cancellationToken)
    {
        return _dbContext.LocalStockNews.AnyAsync(item => item.Symbol == symbol && !item.IsAiProcessed, cancellationToken)
            .ContinueWith(async stockPendingTask =>
            {
                if (stockPendingTask.Result)
                {
                    return true;
                }

                return await _dbContext.LocalSectorReports.AnyAsync(
                    item => item.Symbol == symbol && item.Level == "sector" && !item.IsAiProcessed,
                    cancellationToken);
            }, cancellationToken).Unwrap();
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}