using System.Text.Json;
using System.Text.RegularExpressions;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs.ForumScraping;

public sealed partial class EastmoneyGubaScraper : IForumPostCountScraper
{
    public string Platform => "eastmoney";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EastmoneyGubaScraper> _logger;

    public EastmoneyGubaScraper(IHttpClientFactory httpClientFactory, ILogger<EastmoneyGubaScraper> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<int?> GetPostCountAsync(string symbol, CancellationToken ct)
    {
        var code = NormalizeToDigits(symbol);
        if (string.IsNullOrEmpty(code))
        {
            _logger.LogWarning("东方财富股吧: symbol 无法规范化: {Symbol}", symbol);
            return null;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient("EastmoneyGuba");
            var url = $"https://guba.eastmoney.com/list,{code}.html";
            var html = await client.GetStringAsync(url, ct);

            var match = ArticleListRegex().Match(html);
            if (!match.Success)
            {
                _logger.LogWarning("东方财富股吧: 未找到 article_list 变量: {Symbol}", symbol);
                return null;
            }

            var json = match.Groups[1].Value;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("count", out var countElement))
            {
                return countElement.GetInt32();
            }

            _logger.LogWarning("东方财富股吧: JSON 中未找到 count 字段: {Symbol}", symbol);
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "东方财富股吧采集失败: {Symbol}", symbol);
            return null;
        }
    }

    internal static string NormalizeToDigits(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return string.Empty;

        var s = symbol.Trim();
        // Remove sh/sz/SH/SZ prefix
        if (s.Length > 2 && (s.StartsWith("sh", StringComparison.OrdinalIgnoreCase)
                         || s.StartsWith("sz", StringComparison.OrdinalIgnoreCase)))
        {
            s = s[2..];
        }

        // Verify remaining is all digits
        foreach (var c in s)
        {
            if (!char.IsAsciiDigit(c))
                return string.Empty;
        }

        return s;
    }

    [GeneratedRegex(@"var\s+article_list\s*=\s*(\{.*?\});", RegexOptions.Singleline)]
    private static partial Regex ArticleListRegex();

    public async Task<Dictionary<DateOnly, int>> GetDailyBreakdownAsync(string symbol, int maxPages, CancellationToken ct)
    {
        var code = NormalizeToDigits(symbol);
        if (string.IsNullOrEmpty(code))
            return new Dictionary<DateOnly, int>();

        var dailyCounts = new Dictionary<DateOnly, int>();
        using var client = _httpClientFactory.CreateClient("EastmoneyGuba");

        for (var page = 1; page <= maxPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var url = page == 1
                    ? $"https://guba.eastmoney.com/list,{code}.html"
                    : $"https://guba.eastmoney.com/list,{code}_{page}.html";

                var html = await client.GetStringAsync(url, ct);
                var match = ArticleListRegex().Match(html);
                if (!match.Success) break;

                var json = match.Groups[1].Value;
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("re", out var reElement)) break;

                var foundDates = false;
                foreach (var post in reElement.EnumerateArray())
                {
                    if (post.TryGetProperty("post_publish_time", out var timeEl))
                    {
                        var dateStr = timeEl.GetString();
                        if (dateStr?.Length >= 10 && DateOnly.TryParse(dateStr[..10], out var date))
                        {
                            dailyCounts.TryGetValue(date, out var existing);
                            dailyCounts[date] = existing + 1;
                            foundDates = true;
                        }
                    }
                }

                if (!foundDates) break;

                // Anti-crawl delay
                if (page < maxPages)
                    await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(1000, 2000)), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "东方财富股吧 GetDailyBreakdown page {Page} failed: {Symbol}", page, symbol);
                break;
            }
        }

        return dailyCounts;
    }
}
