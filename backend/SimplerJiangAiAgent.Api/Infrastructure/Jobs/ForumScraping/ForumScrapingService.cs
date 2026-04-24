using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs.ForumScraping;

public interface IForumScrapingService
{
    Task ScrapeAsync(CancellationToken ct);
    Task InitialCollectAsync(CancellationToken ct);
    Task<IReadOnlyList<SingleStockCollectResult>> CollectSingleStockNowAsync(string symbol, CancellationToken ct);
}

public sealed class ForumScrapingService : IForumScrapingService
{
    private static readonly TimeZoneInfo ChinaTimeZone = ResolveChinaTimeZone();

    private readonly IEnumerable<IForumPostCountScraper> _scrapers;
    private readonly IActiveWatchlistService _watchlistService;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<ForumScrapingService> _logger;

    public ForumScrapingService(
        IEnumerable<IForumPostCountScraper> scrapers,
        IActiveWatchlistService watchlistService,
        AppDbContext dbContext,
        ILogger<ForumScrapingService> logger)
    {
        _scrapers = scrapers;
        _watchlistService = watchlistService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task ScrapeAsync(CancellationToken ct)
    {
        var symbols = await _watchlistService.GetEnabledAsync(200, ct);
        if (symbols.Count == 0)
        {
            _logger.LogDebug("论坛帖子采集: 无自选股，跳过");
            return;
        }

        var scraperList = _scrapers.ToList();
        var successCount = 0;
        var failCount = 0;

        foreach (var stock in symbols)
        {
            foreach (var scraper in scraperList)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var dailyCounts = await ScrapeWithRetryBreakdownAsync(scraper, stock.Symbol, 3, 3, ct);
                    if (dailyCounts.Count > 0)
                    {
                        foreach (var (date, count) in dailyCounts)
                        {
                            await UpsertPostCountAsync(stock.Symbol, scraper.Platform,
                                date.ToString("yyyy-MM-dd"), "post_market", count, ct);
                        }
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                        _logger.LogWarning("论坛帖子采集最终失败: {Symbol} {Platform} (返回空)", stock.Symbol, scraper.Platform);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failCount++;
                    _logger.LogWarning(ex, "论坛帖子采集异常: {Symbol} {Platform}", stock.Symbol, scraper.Platform);
                }

                // Random delay 3-5 seconds between requests
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(3000, 5000)), ct);
            }
        }

        _logger.LogInformation(
            "论坛帖子采集完成: 股票数={StockCount}, 成功={Success}, 失败={Fail}",
            symbols.Count, successCount, failCount);
    }

    private async Task UpsertPostCountAsync(
        string symbol, string platform, string tradingDate, string sessionPhase, int postCount,
        CancellationToken ct)
    {
        var normalizedSymbol = NormalizeSymbol(symbol);
        var provider = _dbContext.Database.ProviderName ?? string.Empty;
        var collectedAt = DateTime.UtcNow;

        if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                "MERGE INTO dbo.ForumPostCounts AS target " +
                "USING (SELECT {0} AS Symbol, {1} AS Platform, {2} AS TradingDate, {3} AS SessionPhase) AS source " +
                "ON target.Symbol = source.Symbol AND target.Platform = source.Platform " +
                "   AND target.TradingDate = source.TradingDate AND target.SessionPhase = source.SessionPhase " +
                "WHEN MATCHED THEN UPDATE SET PostCount = {4}, CollectedAt = {5} " +
                "WHEN NOT MATCHED THEN INSERT (Symbol, Platform, TradingDate, SessionPhase, PostCount, CollectedAt) " +
                "VALUES ({0}, {1}, {2}, {3}, {4}, {5});",
                normalizedSymbol, platform, tradingDate, sessionPhase, postCount, collectedAt);
        }
        else
        {
            // SQLite: INSERT OR REPLACE using the UNIQUE constraint
            await _dbContext.Database.ExecuteSqlRawAsync(
                "INSERT INTO ForumPostCounts (Symbol, Platform, TradingDate, SessionPhase, PostCount, CollectedAt) " +
                "VALUES ({0}, {1}, {2}, {3}, {4}, {5}) " +
                "ON CONFLICT(Symbol, Platform, TradingDate, SessionPhase) " +
                "DO UPDATE SET PostCount = excluded.PostCount, CollectedAt = excluded.CollectedAt;",
                normalizedSymbol, platform, tradingDate, sessionPhase, postCount, collectedAt);
        }
    }

    public async Task InitialCollectAsync(CancellationToken ct)
    {
        var stocks = await _watchlistService.GetEnabledAsync(200, ct);
        if (stocks.Count == 0)
        {
            _logger.LogWarning("InitialCollect: No enabled stocks in watchlist");
            return;
        }

        _logger.LogInformation("首次采集建立基准线，后续每日采集将自动计算增量: {StockCount} stocks", stocks.Count);

        var scraperList = _scrapers.ToList();
        int successCount = 0, failCount = 0;

        foreach (var stock in stocks)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var scraper in scraperList)
            {
                try
                {
                    var dailyCounts = await ScrapeWithRetryBreakdownAsync(scraper, stock.Symbol, 3, 3, ct);
                    if (dailyCounts.Count > 0)
                    {
                        foreach (var (date, count) in dailyCounts)
                        {
                            await UpsertPostCountAsync(stock.Symbol, scraper.Platform,
                                date.ToString("yyyy-MM-dd"), "post_market", count, ct);
                        }
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                        _logger.LogWarning("InitialCollect最终失败: {Symbol} {Platform} (返回空)", stock.Symbol, scraper.Platform);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "InitialCollect failed for {Symbol} on {Platform}", stock.Symbol, scraper.Platform);
                    failCount++;
                }

                // 反爬间隔
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(3000, 5000)), ct);
            }
        }

        _logger.LogInformation("InitialCollect complete: {Success} succeeded, {Failed} failed", successCount, failCount);
    }

    public async Task<IReadOnlyList<SingleStockCollectResult>> CollectSingleStockNowAsync(string symbol, CancellationToken ct)
    {
        var scraperList = _scrapers.ToList();
        var results = new List<SingleStockCollectResult>();

        foreach (var scraper in scraperList)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var dailyCounts = await ScrapeWithRetryBreakdownAsync(scraper, symbol, 3, 3, ct);
                if (dailyCounts.Count > 0)
                {
                    foreach (var (date, count) in dailyCounts)
                    {
                        await UpsertPostCountAsync(symbol, scraper.Platform,
                            date.ToString("yyyy-MM-dd"), "post_market", count, ct);
                    }
                    var totalPosts = dailyCounts.Values.Sum();
                    results.Add(new SingleStockCollectResult(scraper.Platform, true, totalPosts, null));
                }
                else
                {
                    results.Add(new SingleStockCollectResult(scraper.Platform, false, null, "scraper returned empty"));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CollectSingleStockNow failed: {Symbol} {Platform}", symbol, scraper.Platform);
                results.Add(new SingleStockCollectResult(scraper.Platform, false, null, ex.Message));
            }

            // 反爬间隔
            await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(2000, 4000)), ct);
        }

        _logger.LogInformation("CollectSingleStockNow: {Symbol}, results={Count}", symbol, results.Count);
        return results;
    }

    private static string ResolveSessionPhase(TimeSpan timeOfDay)
    {
        if (timeOfDay < new TimeSpan(11, 30, 0))
            return "pre_market";
        if (timeOfDay < new TimeSpan(13, 30, 0))
            return "noon";
        return "post_market";
    }

    private static string NormalizeSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return symbol;

        var s = symbol.Trim();
        if (s.Length > 2 &&
            (s.StartsWith("sh", StringComparison.OrdinalIgnoreCase) ||
             s.StartsWith("sz", StringComparison.OrdinalIgnoreCase)))
        {
            var rest = s[2..];
            if (rest.All(char.IsDigit))
                return rest;
        }

        return s;
    }

    private async Task<int?> ScrapeWithRetryAsync(
        IForumPostCountScraper scraper, string symbol, int maxRetries, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var count = await scraper.GetPostCountAsync(symbol, ct);
                if (count.HasValue)
                    return count;

                // Scraper returned null — retry if attempts remain
                if (attempt < maxRetries)
                {
                    _logger.LogDebug(
                        "论坛帖子采集重试: {Symbol} {Platform} 第{Attempt}/{Max}次 (返回null)",
                        symbol, scraper.Platform, attempt, maxRetries);
                    await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5000, 10000)), ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt < maxRetries)
                {
                    _logger.LogDebug(ex,
                        "论坛帖子采集重试: {Symbol} {Platform} 第{Attempt}/{Max}次",
                        symbol, scraper.Platform, attempt, maxRetries);
                    await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5000, 10000)), ct);
                }
                else
                {
                    throw; // Last attempt, rethrow for caller to handle
                }
            }
        }

        return null; // All retries exhausted
    }

    private async Task<Dictionary<DateOnly, int>> ScrapeWithRetryBreakdownAsync(
        IForumPostCountScraper scraper, string symbol, int maxPages, int maxRetries, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await scraper.GetDailyBreakdownAsync(symbol, maxPages, ct);
                if (result.Count > 0)
                    return result;

                if (attempt < maxRetries)
                {
                    _logger.LogDebug("论坛帖子采集(按日)重试: {Symbol} {Platform} 第{Attempt}/{Max}次 (返回空)",
                        symbol, scraper.Platform, attempt, maxRetries);
                    await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5000, 10000)), ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                if (attempt < maxRetries)
                {
                    _logger.LogDebug(ex, "论坛帖子采集(按日)重试: {Symbol} {Platform} 第{Attempt}/{Max}次",
                        symbol, scraper.Platform, attempt, maxRetries);
                    await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5000, 10000)), ct);
                }
                else throw;
            }
        }
        return new Dictionary<DateOnly, int>();
    }

    private static TimeZoneInfo ResolveChinaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("China Standard Time", TimeSpan.FromHours(8), "China Standard Time", "China Standard Time");
        }
    }
}
