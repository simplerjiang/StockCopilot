using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs.ForumScraping;

public interface IHistoricalBackfillService
{
    Task BackfillAsync(string symbol, int days, CancellationToken ct);
    Task BackfillAllWatchlistAsync(int days, CancellationToken ct);
}

public sealed partial class HistoricalBackfillService : IHistoricalBackfillService
{
    private static readonly TimeZoneInfo ChinaTimeZone = ResolveChinaTimeZone();
    private const int PostsPerPage = 80;
    private const int MaxPages = 300;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppDbContext _dbContext;
    private readonly IActiveWatchlistService _watchlistService;
    private readonly IBackfillStatusTracker _statusTracker;
    private readonly ILogger<HistoricalBackfillService> _logger;

    public HistoricalBackfillService(
        IHttpClientFactory httpClientFactory,
        AppDbContext dbContext,
        IActiveWatchlistService watchlistService,
        IBackfillStatusTracker statusTracker,
        ILogger<HistoricalBackfillService> logger)
    {
        _httpClientFactory = httpClientFactory;
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

            var chinaToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ChinaTimeZone));
            var cutoffDate = chinaToday.AddDays(-days);

            // 东方财富回填
            await BackfillPlatformAsync(code, "eastmoney", days, chinaToday, cutoffDate, ct);

            // 新浪回填
            try
            {
                await BackfillSinaPlatformAsync(code, days, chinaToday, cutoffDate, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Backfill {Platform} for {Symbol} failed: {Error}", "sina", symbol, ex.Message);
            }

            // 淘股吧回填
            try
            {
                await BackfillTaogubaPlatformAsync(code, days, chinaToday, cutoffDate, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Backfill {Platform} for {Symbol} failed: {Error}", "taoguba", symbol, ex.Message);
            }
        }
        finally
        {
            _statusTracker.SetBackfilling(code, false);
        }
    }

    /// <summary>东方财富回填（原有逻辑）</summary>
    private async Task BackfillPlatformAsync(
        string code, string platform, int days, DateOnly chinaToday, DateOnly cutoffDate, CancellationToken ct)
    {
        var existingCount = await CountExistingRecordsAsync(code, platform, cutoffDate, chinaToday, ct);
        if (existingCount >= days * 0.9 && existingCount >= 20)
        {
            _logger.LogInformation("历史回填跳过: {Symbol}/{Platform} 已有 {Count} 条记录 (目标 {Days} 天)",
                code, platform, existingCount, days);
            return;
        }

        _logger.LogInformation("历史回填开始: {Symbol}/{Platform}, {Days} 天 (已有 {Existing} 条)", code, platform, days, existingCount);

        var (baseLine, firstPageDates) = await FetchFirstPageAsync(code, ct);
        if (baseLine <= 0)
        {
            _logger.LogWarning("历史回填: 无法获取总帖子数: {Symbol}/{Platform}", code, platform);
            return;
        }

        var dailyCounts = new Dictionary<DateOnly, int>();
        AddDailyCountsFromDates(dailyCounts, firstPageDates);

        var oldestDateSeen = firstPageDates.Count > 0 ? firstPageDates.Min() : chinaToday;
        var page = 2;
        var stalePageCount = 0;

        while (oldestDateSeen > cutoffDate && page <= MaxPages)
        {
            ct.ThrowIfCancellationRequested();

            var pageDates = await FetchPageDatesAsync(code, page, ct);
            if (pageDates.Count == 0)
            {
                stalePageCount++;
                if (stalePageCount >= 3) break;
                page++;
                continue;
            }

            AddDailyCountsFromDates(dailyCounts, pageDates);

            var pageMin = pageDates.Min();
            if (pageMin >= oldestDateSeen)
            {
                stalePageCount++;
                if (stalePageCount >= 3 && page > 5) break;
            }
            else
            {
                stalePageCount = 0;
                oldestDateSeen = pageMin;
            }

            _logger.LogDebug("Backfill eastmoney {Symbol}: page {Page}, {Count} dates, oldest={Oldest}",
                code, page, pageDates.Count, oldestDateSeen);

            page++;

            await Task.Delay(Random.Shared.Next(1000, 3000), ct);
        }

        if (dailyCounts.Count == 0)
        {
            _logger.LogWarning("历史回填: 未解析到任何帖子日期: {Symbol}/{Platform}", code, platform);
            return;
        }

        await WriteSyntheticPostCountsAsync(code, platform, baseLine, dailyCounts, ct);

        _logger.LogInformation(
            "历史回填完成: {Symbol}/{Platform}, 写入 {DayCount} 天数据, 爬取 {PageCount} 页, 最早日期 {OldestDate}",
            code, platform, dailyCounts.Count, page - 1, oldestDateSeen);
    }

    /// <summary>新浪股吧回填</summary>
    private async Task BackfillSinaPlatformAsync(
        string code, int days, DateOnly chinaToday, DateOnly cutoffDate, CancellationToken ct)
    {
        const string platform = "sina";

        var existingCount = await CountExistingRecordsAsync(code, platform, cutoffDate, chinaToday, ct);
        if (existingCount >= days * 0.9 && existingCount >= 20)
        {
            _logger.LogInformation("历史回填跳过: {Symbol}/{Platform} 已有 {Count} 条记录 (目标 {Days} 天)",
                code, platform, existingCount, days);
            return;
        }

        _logger.LogInformation("Backfill {Platform} for {Symbol}: starting (existing={Existing})", platform, code, existingCount);

        var dailyCounts = await ScrapeHistoricalFromSinaAsync(code, days, cutoffDate, chinaToday, ct);
        if (dailyCounts.Count == 0)
        {
            _logger.LogWarning("历史回填: 未解析到任何帖子日期: {Symbol}/{Platform}", code, platform);
            return;
        }

        // 新浪没有总帖子数的精确baseline，使用每日帖数写入增量模式
        await WriteBackfillDailyCountsAsync(code, platform, dailyCounts, ct);

        _logger.LogInformation("Backfill {Platform} for {Symbol}: found {Count} daily entries, writing to DB",
            platform, code, dailyCounts.Count);
    }

    /// <summary>淘股吧回填</summary>
    private async Task BackfillTaogubaPlatformAsync(
        string code, int days, DateOnly chinaToday, DateOnly cutoffDate, CancellationToken ct)
    {
        const string platform = "taoguba";

        var existingCount = await CountExistingRecordsAsync(code, platform, cutoffDate, chinaToday, ct);
        if (existingCount >= days * 0.9 && existingCount >= 20)
        {
            _logger.LogInformation("历史回填跳过: {Symbol}/{Platform} 已有 {Count} 条记录 (目标 {Days} 天)",
                code, platform, existingCount, days);
            return;
        }

        _logger.LogInformation("Backfill {Platform} for {Symbol}: starting (existing={Existing})", platform, code, existingCount);

        var dailyCounts = await ScrapeHistoricalFromTaogubaAsync(code, cutoffDate, ct);
        if (dailyCounts.Count == 0)
        {
            _logger.LogWarning("历史回填: 未解析到任何帖子日期: {Symbol}/{Platform}", code, platform);
            return;
        }

        await WriteBackfillDailyCountsAsync(code, platform, dailyCounts, ct);

        _logger.LogInformation("Backfill {Platform} for {Symbol}: found {Count} daily entries, writing to DB",
            platform, code, dailyCounts.Count);
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

    /// <summary>获取第一页：总帖子数 + 帖子日期列表 (东方财富)</summary>
    private async Task<(long BaseLine, List<DateOnly> Dates)> FetchFirstPageAsync(string code, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient("EastmoneyGuba");
        var url = $"https://guba.eastmoney.com/list,{code}.html";

        string html;
        try
        {
            html = await client.GetStringAsync(url, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "历史回填: 获取第一页失败: {Code}", code);
            return (0, new List<DateOnly>());
        }

        // 提取 article_list JSON
        var match = ArticleListRegex().Match(html);
        if (!match.Success)
            return (0, new List<DateOnly>());

        var json = match.Groups[1].Value;
        long baseLine = 0;
        var dates = new List<DateOnly>();

        try
        {
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("count", out var countEl))
                baseLine = countEl.GetInt64();

            if (doc.RootElement.TryGetProperty("re", out var reEl) && reEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var post in reEl.EnumerateArray())
                {
                    if (post.TryGetProperty("post_publish_time", out var timeEl))
                    {
                        var dateStr = timeEl.GetString();
                        if (TryParsePostDate(dateStr, out var date))
                            dates.Add(date);
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "历史回填: JSON 解析失败: {Code}", code);
        }

        return (baseLine, dates);
    }

    /// <summary>获取指定页的帖子日期列表</summary>
    private async Task<List<DateOnly>> FetchPageDatesAsync(string code, int page, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient("EastmoneyGuba");
        var url = $"https://guba.eastmoney.com/list,{code},f_{page}.html";

        string html;
        try
        {
            html = await client.GetStringAsync(url, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "历史回填: 获取第 {Page} 页失败: {Code}", page, code);
            return new List<DateOnly>();
        }

        var match = ArticleListRegex().Match(html);
        if (!match.Success)
            return new List<DateOnly>();

        var dates = new List<DateOnly>();
        try
        {
            using var doc = JsonDocument.Parse(match.Groups[1].Value);
            if (doc.RootElement.TryGetProperty("re", out var reEl) && reEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var post in reEl.EnumerateArray())
                {
                    if (post.TryGetProperty("post_publish_time", out var timeEl))
                    {
                        var dateStr = timeEl.GetString();
                        if (TryParsePostDate(dateStr, out var date))
                            dates.Add(date);
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "历史回填: 第 {Page} 页 JSON 解析失败: {Code}", page, code);
        }

        return dates;
    }

    /// <summary>新浪股吧: 逐页爬取，解析 MM月DD日 格式日期，按天聚合</summary>
    private async Task<Dictionary<DateOnly, int>> ScrapeHistoricalFromSinaAsync(
        string code, int days, DateOnly cutoffDate, DateOnly chinaToday, CancellationToken ct)
    {
        var prefixed = SinaGubaScraper.NormalizeToPrefixed(code);
        if (string.IsNullOrEmpty(prefixed))
            return new Dictionary<DateOnly, int>();

        var dailyCounts = new Dictionary<DateOnly, int>();
        var currentYear = chinaToday.Year;
        const int maxPages = 200; // 新浪每页约 60 条，200 页覆盖低活跃股票
        var stalePageCount = 0;
        DateOnly? oldestDateSeen = null;

        for (var page = 1; page <= maxPages; page++)
        {
            ct.ThrowIfCancellationRequested();

            _logger.LogDebug("Backfill {Platform} for {Symbol}: scraping page {Page}...", "sina", code, page);

            List<DateOnly> pageDates;
            try
            {
                pageDates = await FetchSinaPageDatesAsync(prefixed, page, currentYear, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Backfill {Platform} for {Symbol}: page {Page} failed", "sina", code, page);
                break;
            }

            if (pageDates.Count == 0)
            {
                stalePageCount++;
                if (stalePageCount >= 3) break;
                continue;
            }

            AddDailyCountsFromDates(dailyCounts, pageDates);

            var pageMin = pageDates.Min();
            if (pageMin < cutoffDate)
                break;

            if (oldestDateSeen.HasValue && pageMin >= oldestDateSeen.Value)
            {
                stalePageCount++;
                if (stalePageCount >= 3 && page > 5) break;
            }
            else
            {
                stalePageCount = 0;
                oldestDateSeen = pageMin;
            }

            if (page < maxPages)
                await Task.Delay(Random.Shared.Next(1000, 3000), ct);
        }

        return dailyCounts;
    }

    /// <summary>获取新浪单页帖子日期</summary>
    private async Task<List<DateOnly>> FetchSinaPageDatesAsync(
        string prefixed, int page, int currentYear, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient("SinaGuba");
        var url = $"https://guba.sina.com.cn/?s=bar&name={prefixed}&type=0&page={page}";

        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var html = Encoding.GetEncoding("gbk").GetString(bytes);

        var dates = new List<DateOnly>();
        // 新浪日期格式: <td>04月13日 </td> (MM月DD日)
        foreach (Match m in SinaDateRegex().Matches(html))
        {
            if (int.TryParse(m.Groups[1].Value, out var month) &&
                int.TryParse(m.Groups[2].Value, out var day))
            {
                // 推断年份：如果 month 大于当前月份，可能是去年的
                var year = currentYear;
                var chinaToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ChinaTimeZone));
                if (month > chinaToday.Month || (month == chinaToday.Month && day > chinaToday.Day))
                    year--;

                try
                {
                    dates.Add(new DateOnly(year, month, day));
                }
                catch (ArgumentOutOfRangeException)
                {
                    // 无效日期，跳过
                }
            }
        }

        return dates;
    }

    /// <summary>淘股吧: 仅首页可获取（无服务端分页），解析 yyyy-MM-dd 日期</summary>
    private async Task<Dictionary<DateOnly, int>> ScrapeHistoricalFromTaogubaAsync(
        string code, DateOnly cutoffDate, CancellationToken ct)
    {
        var prefixed = SinaGubaScraper.NormalizeToPrefixed(code);
        if (string.IsNullOrEmpty(prefixed))
            return new Dictionary<DateOnly, int>();

        _logger.LogInformation("Backfill {Platform} for {Symbol}: scraping page {Page}...", "taoguba", code, 1);

        var dailyCounts = new Dictionary<DateOnly, int>();

        try
        {
            using var client = _httpClientFactory.CreateClient("TaogubaGuba");
            var url = $"https://www.taoguba.com.cn/quotes/{prefixed}";
            var html = await client.GetStringAsync(url, ct);

            foreach (Match m in TaogubaDateRegex().Matches(html))
            {
                if (DateOnly.TryParse(m.Groups[1].Value, out var date) && date >= cutoffDate)
                {
                    dailyCounts.TryGetValue(date, out var existing);
                    dailyCounts[date] = existing + 1;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Backfill {Platform} for {Symbol}: scraping failed", "taoguba", code);
        }

        return dailyCounts;
    }

    /// <summary>
    /// 写入每日帖子增量数据（用于新浪/淘股吧等无 baseline 的平台）。
    /// 每天的 PostCount 直接写入当日采集的帖子数量。
    /// </summary>
    private async Task WriteBackfillDailyCountsAsync(
        string code, string platform, Dictionary<DateOnly, int> dailyCounts, CancellationToken ct)
    {
        var provider = _dbContext.Database.ProviderName ?? string.Empty;
        var collectedAt = DateTime.UtcNow;

        foreach (var (date, count) in dailyCounts)
        {
            ct.ThrowIfCancellationRequested();
            var tradingDate = date.ToString("yyyy-MM-dd");

            if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                await _dbContext.Database.ExecuteSqlRawAsync(
                    "MERGE INTO dbo.ForumPostCounts AS target " +
                    "USING (SELECT {0} AS Symbol, {1} AS Platform, {2} AS TradingDate, {3} AS SessionPhase) AS source " +
                    "ON target.Symbol = source.Symbol AND target.Platform = source.Platform " +
                    "   AND target.TradingDate = source.TradingDate AND target.SessionPhase = source.SessionPhase " +
                    "WHEN NOT MATCHED THEN INSERT (Symbol, Platform, TradingDate, SessionPhase, PostCount, CollectedAt) " +
                    "VALUES ({0}, {1}, {2}, {3}, {4}, {5});",
                    code, platform, tradingDate, "post_market", count, collectedAt);
            }
            else
            {
                await _dbContext.Database.ExecuteSqlRawAsync(
                    "INSERT OR IGNORE INTO ForumPostCounts (Symbol, Platform, TradingDate, SessionPhase, PostCount, CollectedAt) " +
                    "VALUES ({0}, {1}, {2}, {3}, {4}, {5});",
                    code, platform, tradingDate, "post_market", count, collectedAt.ToString("o"));
            }
        }
    }

    private static bool TryParsePostDate(string? dateStr, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(dateStr))
            return false;

        // 格式: "2026-04-13 15:43:15"
        if (dateStr.Length >= 10 && DateOnly.TryParse(dateStr.AsSpan(0, 10), out date))
            return true;

        return false;
    }

    private static void AddDailyCountsFromDates(Dictionary<DateOnly, int> dailyCounts, List<DateOnly> dates)
    {
        foreach (var date in dates)
        {
            dailyCounts.TryGetValue(date, out var existing);
            dailyCounts[date] = existing + 1;
        }
    }

    /// <summary>
    /// 构造合成累积总量并写入 ForumPostCounts。
    /// baseLine 是今天的总帖子数,向前推算每天的累积值。
    /// </summary>
    private async Task WriteSyntheticPostCountsAsync(
        string code, string platform, long baseLine, Dictionary<DateOnly, int> dailyCounts, CancellationToken ct)
    {
        // 按日期降序排列
        var sortedDates = dailyCounts.Keys.OrderByDescending(d => d).ToList();
        var entries = new List<(string TradingDate, long PostCount)>();
        var cumulative = baseLine;

        foreach (var date in sortedDates)
        {
            entries.Add((date.ToString("yyyy-MM-dd"), cumulative));
            cumulative -= dailyCounts[date];
        }

        var provider = _dbContext.Database.ProviderName ?? string.Empty;
        var collectedAt = DateTime.UtcNow;

        foreach (var (tradingDate, postCount) in entries)
        {
            ct.ThrowIfCancellationRequested();

            if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                await _dbContext.Database.ExecuteSqlRawAsync(
                    "MERGE INTO dbo.ForumPostCounts AS target " +
                    "USING (SELECT {0} AS Symbol, {1} AS Platform, {2} AS TradingDate, {3} AS SessionPhase) AS source " +
                    "ON target.Symbol = source.Symbol AND target.Platform = source.Platform " +
                    "   AND target.TradingDate = source.TradingDate AND target.SessionPhase = source.SessionPhase " +
                    "WHEN NOT MATCHED THEN INSERT (Symbol, Platform, TradingDate, SessionPhase, PostCount, CollectedAt) " +
                    "VALUES ({0}, {1}, {2}, {3}, {4}, {5});",
                    code, platform, tradingDate, "post_market", (int)Math.Max(postCount, 0), collectedAt);
            }
            else
            {
                // SQLite: INSERT OR IGNORE — 不覆盖已有的真实采集数据
                await _dbContext.Database.ExecuteSqlRawAsync(
                    "INSERT OR IGNORE INTO ForumPostCounts (Symbol, Platform, TradingDate, SessionPhase, PostCount, CollectedAt) " +
                    "VALUES ({0}, {1}, {2}, {3}, {4}, {5});",
                    code, platform, tradingDate, "post_market", (int)Math.Max(postCount, 0), collectedAt.ToString("o"));
            }
        }
    }

    private async Task<int> CountExistingRecordsAsync(string code, string platform, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var fromStr = from.ToString("yyyy-MM-dd");
        var toStr = to.ToString("yyyy-MM-dd");

        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(DISTINCT TradingDate) FROM ForumPostCounts " +
            "WHERE Symbol = @symbol AND Platform = @platform " +
            "AND TradingDate >= @from AND TradingDate <= @to";

        var p1 = command.CreateParameter();
        p1.ParameterName = "@symbol";
        p1.Value = code;
        command.Parameters.Add(p1);

        var pPlatform = command.CreateParameter();
        pPlatform.ParameterName = "@platform";
        pPlatform.Value = platform;
        command.Parameters.Add(pPlatform);

        var p2 = command.CreateParameter();
        p2.ParameterName = "@from";
        p2.Value = fromStr;
        command.Parameters.Add(p2);

        var p3 = command.CreateParameter();
        p3.ParameterName = "@to";
        p3.Value = toStr;
        command.Parameters.Add(p3);

        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
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

    private static TimeZoneInfo ResolveChinaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("China Standard Time",
                TimeSpan.FromHours(8), "China Standard Time", "China Standard Time");
        }
    }

    [GeneratedRegex(@"var\s+article_list\s*=\s*(\{.*?\});", RegexOptions.Singleline)]
    private static partial Regex ArticleListRegex();

    /// <summary>新浪日期格式: &lt;td&gt;04月13日 &lt;/td&gt;</summary>
    [GeneratedRegex(@"<td>\s*(\d{2})月(\d{2})日\s*</td>")]
    private static partial Regex SinaDateRegex();

    /// <summary>淘股吧日期格式: &lt;div class="related-sources"&gt;2026-04-13 08:54</summary>
    [GeneratedRegex(@"<div\s+class\s*=\s*""related-sources""\s*>(\d{4}-\d{2}-\d{2})\s", RegexOptions.IgnoreCase)]
    private static partial Regex TaogubaDateRegex();
}
