using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using SimplerJiangAiAgent.Api.Data.Entities;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface ILocalFactArticleReadService
{
    Task PrepareAsync(IReadOnlyList<LocalStockNews> items, CancellationToken cancellationToken = default);
    Task PrepareAsync(IReadOnlyList<LocalSectorReport> items, CancellationToken cancellationToken = default);
}

internal static class LocalFactArticleTextSanitizer
{
    private static readonly string[] LeadingNavKeywords =
    {
        "财经", "焦点", "股票", "新股", "期指", "期权", "基金", "理财", "外汇", "保险", "行情", "数据", "资讯", "直播"
    };

    public static string StripLeadingNavBar(string text)
    {
        return StripLeadingNavBarCore(text, preserveNavOnly: true);
    }

    public static string StripLeadingNavBarForStoredSnippet(string text)
    {
        return StripLeadingNavBarCore(text, preserveNavOnly: false);
    }

    private static string StripLeadingNavBarCore(string text, bool preserveNavOnly)
    {
        var head = text.Length > 100 ? text[..100] : text;
        var matchCount = LeadingNavKeywords.Count(kw => head.Contains(kw));
        if (matchCount < 5)
        {
            return text;
        }

        var lastEnd = 0;
        foreach (var kw in LeadingNavKeywords)
        {
            var idx = head.LastIndexOf(kw, StringComparison.Ordinal);
            if (idx >= 0)
            {
                var end = idx + kw.Length;
                if (end > lastEnd)
                {
                    lastEnd = end;
                }
            }
        }

        while (lastEnd < text.Length && char.IsWhiteSpace(text[lastEnd]))
        {
            lastEnd++;
        }

        return lastEnd < text.Length
            ? text[lastEnd..]
            : preserveNavOnly ? text : string.Empty;
    }
}

public sealed class LocalFactArticleReadService : ILocalFactArticleReadService
{
    private static readonly string[] FullTextSourceTags =
    {
        "announcement",
        "company-news",
        "company_news",
        "eastmoney-announcement",
        "sina-company-news",
        "regulatory"
    };

    private static readonly string[] FullTextTitleKeywords =
    {
        "公告", "财报", "季报", "年报", "中报", "业绩", "预告", "快报", "合同", "订单", "回购", "问询", "监管", "停牌", "复牌"
    };

    private static readonly Regex MultiWhitespaceRegex = new("\\s+", RegexOptions.Compiled);
    private static readonly Regex BoilerplateRegex = new("(责任编辑|免责声明|原标题|来源：|点击进入专题)", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly ILogger<LocalFactArticleReadService> _logger;

    public LocalFactArticleReadService(HttpClient httpClient, ILogger<LocalFactArticleReadService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task PrepareAsync(IReadOnlyList<LocalStockNews> items, CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            await PrepareAsync(
                item.Title,
                item.TranslatedTitle,
                item.Category,
                item.SourceTag,
                item.Url,
                item.ArticleExcerpt,
                item.ArticleSummary,
                item.ReadMode,
                item.ReadStatus,
                item.IngestedAt,
                (excerpt, summary, readMode, readStatus, ingestedAt) =>
                {
                    item.ArticleExcerpt = excerpt;
                    item.ArticleSummary = summary;
                    item.ReadMode = readMode;
                    item.ReadStatus = readStatus;
                    item.IngestedAt = ingestedAt;
                },
                cancellationToken);
        }
    }

    public async Task PrepareAsync(IReadOnlyList<LocalSectorReport> items, CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            await PrepareAsync(
                item.Title,
                item.TranslatedTitle,
                item.Level,
                item.SourceTag,
                item.Url,
                item.ArticleExcerpt,
                item.ArticleSummary,
                item.ReadMode,
                item.ReadStatus,
                item.IngestedAt,
                (excerpt, summary, readMode, readStatus, ingestedAt) =>
                {
                    item.ArticleExcerpt = excerpt;
                    item.ArticleSummary = summary;
                    item.ReadMode = readMode;
                    item.ReadStatus = readStatus;
                    item.IngestedAt = ingestedAt;
                },
                cancellationToken);
        }
    }

    private async Task PrepareAsync(
        string title,
        string? translatedTitle,
        string? category,
        string sourceTag,
        string? url,
        string? existingExcerpt,
        string? existingSummary,
        string? existingReadMode,
        string? existingReadStatus,
        DateTime? existingIngestedAt,
        Action<string?, string?, string, string, DateTime?> assign,
        CancellationToken cancellationToken)
    {
        if (ShouldReuseCache(existingReadStatus, existingIngestedAt))
        {
            return;
        }

        var titleText = FirstNonEmpty(translatedTitle, title);
        if (string.IsNullOrWhiteSpace(url))
        {
            assign(
                existingExcerpt ?? BuildExcerpt(titleText),
                existingSummary ?? BuildSummary(titleText),
                "url_unavailable",
                string.IsNullOrWhiteSpace(titleText) ? "metadata_only" : "title_only",
                existingIngestedAt ?? DateTime.UtcNow);
            return;
        }

        if (!ShouldFetchFullText(category, sourceTag, titleText))
        {
            assign(
                existingExcerpt ?? BuildExcerpt(titleText),
                existingSummary ?? BuildSummary(titleText),
                "local_fact",
                string.IsNullOrWhiteSpace(titleText) ? "metadata_only" : "title_only",
                existingIngestedAt ?? DateTime.UtcNow);
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0 Safari/537.36");
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var text = ExtractReadableText(html);

            if (string.IsNullOrWhiteSpace(text))
            {
                assign(
                    existingExcerpt ?? BuildExcerpt(titleText),
                    existingSummary ?? BuildSummary(titleText),
                    "url_fetched",
                    "fetch_failed",
                    DateTime.UtcNow);
                return;
            }

            var excerpt = BuildExcerpt(text);
            var summary = BuildSummary(text);
            var readStatus = text.Length >= 320 ? "full_text_read" : "summary_only";
            assign(excerpt, summary, "url_fetched", readStatus, DateTime.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "抓取正文失败: {Url}", url);
            assign(
                existingExcerpt ?? BuildExcerpt(titleText),
                existingSummary ?? BuildSummary(titleText),
                "url_fetched",
                "fetch_failed",
                DateTime.UtcNow);
        }
    }

    private static bool ShouldReuseCache(string? readStatus, DateTime? ingestedAt)
    {
        if (string.IsNullOrWhiteSpace(readStatus) || !ingestedAt.HasValue)
        {
            return false;
        }

        if (string.Equals(readStatus, "fetch_failed", StringComparison.OrdinalIgnoreCase))
        {
            return ingestedAt.Value >= DateTime.UtcNow.AddHours(-6);
        }

        return true;
    }

    private static bool ShouldFetchFullText(string? category, string sourceTag, string? title)
    {
        if (!string.IsNullOrWhiteSpace(category) && FullTextTitleKeywords.Any(category.Contains))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(sourceTag)
            && FullTextSourceTags.Any(tag => sourceTag.Contains(tag, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(title)
            && FullTextTitleKeywords.Any(keyword => title.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static string ExtractReadableText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var document = new HtmlDocument();
        document.LoadHtml(html);
        RemoveNodes(document, "//script|//style|//noscript|//nav|//footer|//header|//iframe");

        var container = document.DocumentNode.SelectSingleNode("//article")
            ?? document.DocumentNode.SelectSingleNode("//main")
            ?? document.DocumentNode.SelectSingleNode("//body")
            ?? document.DocumentNode;

        var text = HtmlEntity.DeEntitize(container.InnerText ?? string.Empty);
        text = MultiWhitespaceRegex.Replace(text, " ").Trim();

        text = LocalFactArticleTextSanitizer.StripLeadingNavBar(text);

        var boilerplateIndex = BoilerplateRegex.Match(text).Index;
        if (boilerplateIndex > 120)
        {
            text = text[..boilerplateIndex].Trim();
        }

        return text.Length > 4000 ? text[..4000].Trim() : text;
    }

    private static void RemoveNodes(HtmlDocument document, string xPath)
    {
        var nodes = document.DocumentNode.SelectNodes(xPath);
        if (nodes is null)
        {
            return;
        }

        foreach (var node in nodes)
        {
            node.Remove();
        }
    }

    private static string? BuildExcerpt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = text.Trim();
        return normalized.Length <= 220 ? normalized : normalized[..220].Trim() + "...";
    }

    private static string? BuildSummary(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = text.Trim();
        var sentenceBreak = normalized.IndexOfAny(new[] { '。', '！', '？', '.', '!' });
        if (sentenceBreak >= 40)
        {
            return normalized[..(sentenceBreak + 1)].Trim();
        }

        return normalized.Length <= 140 ? normalized : normalized[..140].Trim() + "...";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}