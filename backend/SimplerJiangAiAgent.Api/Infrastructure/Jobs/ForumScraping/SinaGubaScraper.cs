using System.Text;
using System.Text.RegularExpressions;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs.ForumScraping;

public sealed partial class SinaGubaScraper : IForumPostCountScraper
{
    private const int PostsPerPage = 15;

    public string Platform => "sina";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SinaGubaScraper> _logger;

    public SinaGubaScraper(IHttpClientFactory httpClientFactory, ILogger<SinaGubaScraper> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<int?> GetPostCountAsync(string symbol, CancellationToken ct)
    {
        var prefixed = NormalizeToPrefixed(symbol);
        if (string.IsNullOrEmpty(prefixed))
        {
            _logger.LogWarning("新浪股吧: symbol 无法规范化: {Symbol}", symbol);
            return null;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient("SinaGuba");
            var url = $"https://guba.sina.com.cn/?s=bar&name={prefixed}";

            // Sina returns GBK-encoded HTML
            var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            var html = Encoding.GetEncoding("gbk").GetString(bytes);

            // Primary: extract max page number from pagination links (page=N)
            var pageMatches = PageParamRegex().Matches(html);
            if (pageMatches.Count > 0)
            {
                var maxPage = pageMatches
                    .Select(m => int.TryParse(m.Groups[1].Value, out var p) ? p : 0)
                    .Max();
                if (maxPage > 0)
                    return maxPage * PostsPerPage;
            }

            // Fallback 1: try to find totalcount parameter
            var countMatch = TotalCountRegex().Match(html);
            if (countMatch.Success && int.TryParse(countMatch.Groups[1].Value, out var totalCount))
            {
                return totalCount;
            }

            // Fallback 2: try to find "共 N 页" pattern
            var match = TotalPagesRegex().Match(html);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var totalPages))
            {
                return totalPages * PostsPerPage;
            }

            _logger.LogWarning("新浪股吧: 未找到分页链接或总数: {Symbol}", symbol);
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "新浪股吧采集失败: {Symbol}", symbol);
            return null;
        }
    }

    internal static string NormalizeToPrefixed(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return string.Empty;

        var s = symbol.Trim();

        // Already prefixed
        if (s.Length > 2 && (s.StartsWith("sh", StringComparison.OrdinalIgnoreCase)
                         || s.StartsWith("sz", StringComparison.OrdinalIgnoreCase)))
        {
            return s.ToLowerInvariant();
        }

        // Pure digits: infer prefix by first digit
        // 6xx → sh (上证主板), 9xx → sh (B股)
        // 0xx → sz (深圳主板), 3xx → sz (创业板), 2xx → sz (B股)
        // 688xxx → sh (科创板)
        if (s.Length >= 1 && char.IsAsciiDigit(s[0]))
        {
            var prefix = s[0] switch
            {
                '6' or '9' => "sh",
                '0' or '2' or '3' => "sz",
                _ => string.Empty
            };

            if (string.IsNullOrEmpty(prefix))
                return string.Empty;

            return prefix + s;
        }

        return string.Empty;
    }

    [GeneratedRegex(@"page=(\d+)")]
    private static partial Regex PageParamRegex();

    [GeneratedRegex(@"共\s*(\d+)\s*页")]
    private static partial Regex TotalPagesRegex();

    [GeneratedRegex(@"totalcount[""']?\s*[:=]\s*[""']?(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex TotalCountRegex();

    [GeneratedRegex(@"<td>\s*(\d{2})月(\d{2})日\s*</td>")]
    private static partial Regex SinaDateRegex();

    public async Task<Dictionary<DateOnly, int>> GetDailyBreakdownAsync(string symbol, int maxPages, CancellationToken ct)
    {
        var prefixed = NormalizeToPrefixed(symbol);
        if (string.IsNullOrEmpty(prefixed))
            return new Dictionary<DateOnly, int>();

        var dailyCounts = new Dictionary<DateOnly, int>();
        using var client = _httpClientFactory.CreateClient("SinaGuba");
        var chinaToday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));

        for (var page = 1; page <= maxPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var url = $"https://guba.sina.com.cn/?s=bar&name={prefixed}&page={page}";
                var response = await client.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                var html = Encoding.GetEncoding("gbk").GetString(bytes);

                // Parse dates: <td>04月13日</td>
                var dateMatches = SinaDateRegex().Matches(html);
                if (dateMatches.Count == 0) break;

                foreach (Match m in dateMatches)
                {
                    if (int.TryParse(m.Groups[1].Value, out var month) && int.TryParse(m.Groups[2].Value, out var day))
                    {
                        // Year inference
                        var year = chinaToday.Year;
                        if (month > chinaToday.Month || (month == chinaToday.Month && day > chinaToday.Day))
                            year--;

                        try
                        {
                            var date = new DateOnly(year, month, day);
                            dailyCounts.TryGetValue(date, out var existing);
                            dailyCounts[date] = existing + 1;
                        }
                        catch { /* invalid date */ }
                    }
                }

                if (page < maxPages)
                    await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(1000, 2000)), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "新浪股吧 GetDailyBreakdown page {Page} failed: {Symbol}", page, symbol);
                break;
            }
        }

        return dailyCounts;
    }
}
