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
            var url = $"https://www.taoguba.com.cn/quotes/{prefixed}";
            var html = await client.GetStringAsync(url, ct);

            // 提取帖子日期，统计今日帖子数
            var todayStr = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ChinaTimeZone)
                .ToString("yyyy-MM-dd");

            var dateMatches = PostDateRegex().Matches(html);
            if (dateMatches.Count == 0)
            {
                // 回退：仅计数 forumRow 元素
                var rowMatches = ForumRowRegex().Matches(html);
                if (rowMatches.Count == 0)
                {
                    _logger.LogWarning("淘股吧: 首页未找到帖子行: {Symbol}", symbol);
                    return null;
                }
                _logger.LogDebug("淘股吧: 无法解析帖子日期，回退为行计数: {Symbol}, count={Count}", symbol, rowMatches.Count);
                return rowMatches.Count;
            }

            var todayCount = 0;
            foreach (Match m in dateMatches)
            {
                if (m.Groups[1].Value == todayStr)
                    todayCount++;
            }

            _logger.LogDebug("淘股吧: {Symbol} 首页帖子={Total}, 今日帖子={Today}", symbol, dateMatches.Count, todayCount);

            // 返回今日帖子数（即使为 0 也是有效数据）
            return todayCount;
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

    [GeneratedRegex(@"<div\s+id\s*=\s*""forumRow_", RegexOptions.IgnoreCase)]
    private static partial Regex ForumRowRegex();

    /// <summary>
    /// Matches date patterns in related-sources divs: 2026-04-13 08:54
    /// </summary>
    [GeneratedRegex(@"<div\s+class\s*=\s*""related-sources""\s*>(\d{4}-\d{2}-\d{2})\s", RegexOptions.IgnoreCase)]
    private static partial Regex PostDateRegex();

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
