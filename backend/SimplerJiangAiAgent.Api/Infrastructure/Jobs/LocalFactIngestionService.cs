using System.Collections.Concurrent;
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
    Task EnsureFreshAsync(string symbol, CancellationToken cancellationToken = default);
}

public sealed class LocalFactIngestionService : ILocalFactIngestionService
{
    private const string SinaRollUrl = "https://feed.mix.sina.com.cn/api/roll/get?pageid=155&lid=1686&num=60&versionNumber=1.2.8.1";
    private const string EastmoneySectorSearchBaseUrl = "https://search-api-dev.eastmoney.com/api/info/get";
    private static readonly SemaphoreSlim MarketRefreshGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SymbolRefreshGates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly (string Url, string Source, string SourceTag)[] MarketRssFeeds =
    {
        ("https://search.cnbc.com/rs/search/combinedcms/view.xml?partnerId=wrss01&id=10000664", "CNBC Finance", "cnbc-finance-rss"),
        ("https://feeds.a.dj.com/rss/WSJcomUSBusiness.xml", "WSJ US Business", "wsj-us-business-rss"),
        ("https://rss.nytimes.com/services/xml/rss/nyt/Business.xml", "NYT Business", "nyt-business-rss")
    };
    private static readonly string[] MarketKeywords =
    {
        "A股", "大盘", "沪指", "深成指", "创业板", "科创板", "两市", "收盘", "午评", "早评", "北向资金", "指数"
    };

    private readonly AppDbContext _dbContext;
    private readonly HttpClient _httpClient;
    private readonly StockSyncOptions _options;
    private readonly ILogger<LocalFactIngestionService> _logger;

    public LocalFactIngestionService(
        AppDbContext dbContext,
        HttpClient httpClient,
        IOptions<StockSyncOptions> options,
        ILogger<LocalFactIngestionService> logger)
    {
        _dbContext = dbContext;
        _httpClient = httpClient;
        _options = options.Value;
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
        var freshCutoff = DateTime.UtcNow.AddMinutes(-30);

        await MarketRefreshGate.WaitAsync(cancellationToken);
        try
        {
            var hasFreshMarket = await _dbContext.LocalSectorReports
                .AnyAsync(item => item.Level == "market" && item.CrawledAt >= freshCutoff, cancellationToken);

            if (!hasFreshMarket)
            {
                var crawledAt = DateTime.UtcNow;
                var marketReports = await FetchMarketReportsAsync(crawledAt, cancellationToken);
                await UpsertMarketReportsAsync(marketReports, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        finally
        {
            MarketRefreshGate.Release();
        }

        var symbolGate = GetSymbolGate(normalized);
        await symbolGate.WaitAsync(cancellationToken);
        try
        {
            var hasFreshStockNews = await _dbContext.LocalStockNews
                .AnyAsync(item => item.Symbol == normalized && item.CrawledAt >= freshCutoff, cancellationToken);

            var hasFreshSector = await _dbContext.LocalSectorReports
                .AnyAsync(item => item.Symbol == normalized && item.Level == "sector" && item.CrawledAt >= freshCutoff, cancellationToken);

            if (hasFreshStockNews && hasFreshSector)
            {
                return;
            }

            var crawledAt = DateTime.UtcNow;
            await SyncSymbolAsync(normalized, crawledAt, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            symbolGate.Release();
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
        var url = $"https://emweb.securities.eastmoney.com/PC_HSF10/CompanySurvey/CompanySurveyAjax?code={marketPrefix}{code}";
        var json = await _httpClient.GetStringAsync(url, cancellationToken);
        return EastmoneyCompanyProfileParser.Parse(symbol, json);
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
            request.Headers.TryAddWithoutValidation("Accept", "application/json,text/plain,*/*");
            request.Headers.TryAddWithoutValidation("Referer", "https://so.eastmoney.com/");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0 Safari/537.36");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            var reports = EastmoneySectorSearchParser.Parse(symbol, sectorName, content, crawledAt);
            if (reports.Count == 0)
            {
                _logger.LogWarning("东财板块搜索未返回可解析结果: {SectorName}", sectorName);
            }

            return reports;
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
        var rssTasks = MarketRssFeeds
            .Select(feed => FetchMarketFeedAsync(feed.Url, feed.Source, feed.SourceTag, crawledAt, cancellationToken))
            .ToArray();

        var rssResults = await Task.WhenAll(rssTasks);
        var reports = rssResults
            .SelectMany(items => items)
            .OrderByDescending(item => item.PublishTime)
            .DistinctBy(item => item.Url ?? item.ExternalId ?? item.Title)
            .Take(18)
            .ToArray();

        if (reports.Length > 0)
        {
            return reports;
        }

        var fallbackMessages = await FetchRollMessagesAsync(cancellationToken);
        return BuildFallbackMarketReports(fallbackMessages, crawledAt);
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
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept", "application/rss+xml,application/xml,text/xml,*/*");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0 Safari/537.36");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            return RssMarketNewsParser.Parse(content, source, sourceTag, crawledAt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "抓取宏观 RSS 失败: {Source}", source);
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
        return $"{EastmoneySectorSearchBaseUrl}?words={Uri.EscapeDataString(sectorName)}&type=21";
    }

    private async Task UpsertStockNewsAsync(string symbol, IReadOnlyList<LocalStockNewsSeed> items, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.LocalStockNews
            .Where(item => item.Symbol == symbol)
            .ToListAsync(cancellationToken);

        _dbContext.LocalStockNews.RemoveRange(existing);
        _dbContext.LocalStockNews.AddRange(items
            .OrderByDescending(item => item.PublishTime)
            .Take(40)
            .Select(item => new LocalStockNews
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
                Url = item.Url
            }));
    }

    private async Task UpsertSectorReportsAsync(
        string symbol,
        IReadOnlyList<LocalSectorReportSeed> sectorReports,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.LocalSectorReports
            .Where(item => item.Symbol == symbol && item.Level == "sector")
            .ToListAsync(cancellationToken);

        _dbContext.LocalSectorReports.RemoveRange(existing);
        _dbContext.LocalSectorReports.AddRange(sectorReports.Select(item => new LocalSectorReport
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
            Url = item.Url
        }));
    }

    private async Task UpsertMarketReportsAsync(
        IReadOnlyList<LocalSectorReportSeed> reports,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.LocalSectorReports
            .Where(item => item.Level == "market")
            .ToListAsync(cancellationToken);

        _dbContext.LocalSectorReports.RemoveRange(existing);
        _dbContext.LocalSectorReports.AddRange(reports.Select(item => new LocalSectorReport
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
            Url = item.Url
        }));
    }

    internal static IReadOnlyList<LocalSectorReportSeed> BuildFallbackMarketReports(
        IReadOnlyList<IntradayMessageDto> rollMessages,
        DateTime crawledAt)
    {
        var matched = rollMessages
            .Where(item => MarketKeywords.Any(keyword => item.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.PublishedAt)
            .Take(12)
            .ToArray();

        var source = matched.Length > 0
            ? matched
            : rollMessages.OrderByDescending(item => item.PublishedAt).Take(12).ToArray();

        return source
            .Select(item => new LocalSectorReportSeed(
                null,
                "大盘环境",
                "market",
                item.Title,
                item.Source,
                "sina-roll-market-fallback",
                item.Url,
                item.PublishedAt,
                crawledAt,
                item.Url))
            .ToArray();
    }
}