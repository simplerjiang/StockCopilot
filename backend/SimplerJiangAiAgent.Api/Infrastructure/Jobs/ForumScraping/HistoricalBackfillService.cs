using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs.ForumScraping;

public interface IHistoricalBackfillService
{
    Task BackfillAsync(string symbol, int days, CancellationToken ct);
    Task BackfillAllWatchlistAsync(int days, CancellationToken ct);
}

public sealed class HistoricalBackfillService : IHistoricalBackfillService
{
    private readonly IEnumerable<IForumPostCountScraper> _scrapers;
    private readonly AppDbContext _dbContext;
    private readonly IActiveWatchlistService _watchlistService;
    private readonly IBackfillStatusTracker _statusTracker;
    private readonly ILogger<HistoricalBackfillService> _logger;

    public HistoricalBackfillService(
        IEnumerable<IForumPostCountScraper> scrapers,
        AppDbContext dbContext,
        IActiveWatchlistService watchlistService,
        IBackfillStatusTracker statusTracker,
        ILogger<HistoricalBackfillService> logger)
    {
        _scrapers = scrapers;
        _dbContext = dbContext;
        _watchlistService = watchlistService;
        _statusTracker = statusTracker;
        _logger = logger;
    }

    public async Task BackfillAsync(string symbol, int days, CancellationToken ct)
    {
        var code = NormalizeToDigits(symbol);
        if (string.IsNullOrEmpty(code))
        {
            _logger.LogWarning("历史回填: symbol 无法规范化: {Symbol}", symbol);
            return;
        }

        _statusTracker.SetBackfilling(code, true);
        try
        {
            days = Math.Clamp(days, 1, 365);
            var maxPages = Math.Clamp(days / 2, 5, 100);

            // Use upsert (ON CONFLICT UPDATE) instead of delete+reinsert
            // to avoid a window where concurrent queries return 0 results.

            foreach (var scraper in _scrapers)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var dailyCounts = await scraper.GetDailyBreakdownAsync(symbol, maxPages, ct);
                    if (dailyCounts.Count > 0)
                    {
                        foreach (var (date, count) in dailyCounts)
                        {
                            await WritePostCountAsync(code, scraper.Platform,
                                date.ToString("yyyy-MM-dd"), count, ct);
                        }
                        _logger.LogInformation(
                            "Backfill {Platform} for {Symbol}: {Days} dates, {Total} total posts",
                            scraper.Platform, symbol, dailyCounts.Count, dailyCounts.Values.Sum());
                    }
                    else
                    {
                        _logger.LogWarning("Backfill {Platform} for {Symbol}: no data returned",
                            scraper.Platform, symbol);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Backfill failed for {Symbol} on {Platform}", symbol, scraper.Platform);
                }

                // Anti-crawl delay between platforms
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(3000, 5000)), ct);
            }
        }
        finally
        {
            _statusTracker.SetBackfilling(code, false);
        }
    }

    public async Task BackfillAllWatchlistAsync(int days, CancellationToken ct)
    {
        var stocks = await _watchlistService.GetEnabledAsync(200, ct);
        if (stocks.Count == 0)
        {
            _logger.LogWarning("历史回填: 无自选股");
            return;
        }

        _logger.LogInformation("历史回填全部自选: {Count} 只股票, {Days} 天", stocks.Count, days);

        foreach (var stock in stocks)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await BackfillAsync(stock.Symbol, days, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "历史回填失败: {Symbol}", stock.Symbol);
            }

            // 股票间更大间隔 5-10 秒
            await Task.Delay(Random.Shared.Next(5000, 10000), ct);
        }
    }

    private async Task ClearOldDataAsync(string normalizedSymbol, CancellationToken ct)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM ForumPostCounts WHERE Symbol = @symbol";
        var param = cmd.CreateParameter();
        param.ParameterName = "@symbol";
        param.Value = normalizedSymbol;
        cmd.Parameters.Add(param);

        var deleted = await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Cleared {Count} old records for {Symbol}", deleted, normalizedSymbol);
    }

    private async Task WritePostCountAsync(
        string symbol, string platform, string tradingDate, int postCount, CancellationToken ct)
    {
        await _dbContext.Database.ExecuteSqlRawAsync(
            "INSERT INTO ForumPostCounts (Symbol, Platform, TradingDate, SessionPhase, PostCount, CollectedAt) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5}) " +
            "ON CONFLICT(Symbol, Platform, TradingDate, SessionPhase) " +
            "DO UPDATE SET PostCount = excluded.PostCount, CollectedAt = excluded.CollectedAt;",
            symbol, platform, tradingDate, "post_market", postCount, DateTime.UtcNow.ToString("o"));
    }

    private static string NormalizeToDigits(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return string.Empty;

        var s = symbol.Trim();
        if (s.Length > 2 && (s.StartsWith("sh", StringComparison.OrdinalIgnoreCase)
                         || s.StartsWith("sz", StringComparison.OrdinalIgnoreCase)))
        {
            s = s[2..];
        }

        foreach (var c in s)
        {
            if (!char.IsAsciiDigit(c))
                return string.Empty;
        }

        return s;
    }
}
