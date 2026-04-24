using System.Text.RegularExpressions;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs.ForumScraping;

public sealed partial class TaogubaScraper : IForumPostCountScraper
{
    public string Platform => "taoguba";

    private static readonly TimeZoneInfo ChinaTimeZone = ResolveChinaTimeZone();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TaogubaScraper> _logger;

    public TaogubaScraper(IHttpClientFactory httpClientFactory, ILogger<TaogubaScraper> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<int?> GetPostCountAsync(string symbol, CancellationToken ct)
    {
        var prefixed = SinaGubaScraper.NormalizeToPrefixed(symbol);
        if (string.IsNullOrEmpty(prefixed))
        {
            _logger.LogWarning("淘股吧: symbol 无法规范化: {Symbol}", symbol);
            return null;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient("TaogubaGuba");

            // Fetch first page
            var url = $"https://www.taoguba.com.cn/quotes/{prefixed}";
            var html = await client.GetStringAsync(url, ct);

            // Strategy 1: Try to find total post count from pagination info
            var totalFromPagination = TryExtractTotalFromPagination(html);
            if (totalFromPagination.HasValue && totalFromPagination.Value > 0)
            {
                _logger.LogDebug("淘股吧: {Symbol} 分页估算总帖数={Count}", symbol, totalFromPagination.Value);
                return totalFromPagination.Value;
            }

            // Strategy 2: Count forumRow elements across visible pages
            var page1Count = ForumRowRegex().Matches(html).Count;
            if (page1Count == 0)
            {
                // Try date-based counting as last resort
                var dateCount = PostDateRegex().Matches(html).Count;
                if (dateCount > 0)
                {
                    _logger.LogDebug("淘股吧: {Symbol} 首页日期计数={Count}", symbol, dateCount);
                    return dateCount;
                }

                _logger.LogWarning("淘股吧: 首页未找到帖子: {Symbol}", symbol);
                return null;
            }

            // Try page 2 and 3 to accumulate more posts
            var totalCount = page1Count;
            for (var page = 2; page <= 3; page++)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(1000, 2000)), ct);

                try
                {
                    var pageUrl = $"https://www.taoguba.com.cn/quotes/{prefixed}?pageNo={page}";
                    var pageHtml = await client.GetStringAsync(pageUrl, ct);
                    var pageCount = ForumRowRegex().Matches(pageHtml).Count;
                    if (pageCount == 0) break; // No more pages
                    totalCount += pageCount;

                    // Also check pagination info on later pages
                    var total = TryExtractTotalFromPagination(pageHtml);
                    if (total.HasValue && total.Value > 0)
                    {
                        _logger.LogDebug("淘股吧: {Symbol} 第{Page}页找到分页信息，总帖数={Count}", symbol, page, total.Value);
                        return total.Value;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch
                {
                    break; // Ignore page fetch errors
                }
            }

            _logger.LogDebug("淘股吧: {Symbol} 前几页帖子总数={Count}", symbol, totalCount);
            return totalCount;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "淘股吧采集失败: {Symbol}", symbol);
            return null;
        }
    }

    private static int? TryExtractTotalFromPagination(string html)
    {
        // Try "共 N 条" pattern (total post count)
        var totalMatch = TotalCountRegex().Match(html);
        if (totalMatch.Success && int.TryParse(totalMatch.Groups[1].Value, out var total))
            return total;

        // Try "共 N 页" pattern with items per page estimation
        var pageMatch = TotalPagesRegex().Match(html);
        if (pageMatch.Success && int.TryParse(pageMatch.Groups[1].Value, out var pages))
            return pages * 20; // Estimate 20 posts per page

        return null;
    }

    [GeneratedRegex(@"<div\s+id\s*=\s*""forumRow_", RegexOptions.IgnoreCase)]
    private static partial Regex ForumRowRegex();

    /// <summary>
    /// Matches date patterns in related-sources divs: 2026-04-13 08:54
    /// </summary>
    [GeneratedRegex(@"<div\s+class\s*=\s*""related-sources""\s*>(\d{4}-\d{2}-\d{2})\s", RegexOptions.IgnoreCase)]
    private static partial Regex PostDateRegex();

    [GeneratedRegex(@"共\s*(\d+)\s*条", RegexOptions.IgnoreCase)]
    private static partial Regex TotalCountRegex();

    [GeneratedRegex(@"共\s*(\d+)\s*页", RegexOptions.IgnoreCase)]
    private static partial Regex TotalPagesRegex();

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
