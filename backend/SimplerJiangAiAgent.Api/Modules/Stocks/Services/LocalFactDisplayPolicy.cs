using System.Text.RegularExpressions;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

internal static class LocalFactDisplayPolicy
{
    private static readonly HashSet<string> NavigationNoiseKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "财经", "焦点", "股票", "新股", "期指", "期权", "行情", "数据", "全球", "美股", "港股",
        "基金", "外汇", "黄金", "债券", "理财", "期货", "直播", "专题", "博客", "股吧", "研报",
        "公告", "个股", "板块", "市场", "滚动", "新闻", "首页", "下载", "app", "客户端", "更多"
    };

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex SentenceSplitRegex = new(@"(?<=[。！？；;.!?])\s+|\r?\n+", RegexOptions.Compiled);

    public static string? SanitizeTranslatedTitle(string originalTitle, string? translatedTitle)
    {
        var normalized = NormalizeText(translatedTitle);
        if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, originalTitle, StringComparison.Ordinal))
        {
            return null;
        }

        return LooksLikeClearChineseTitle(originalTitle) ? null : normalized;
    }

    public static bool IsStrongStockMatch(
        string symbol,
        string? stockName,
        string title,
        string? translatedTitle,
        string? aiTarget)
    {
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            return false;
        }

        var bareSymbol = normalizedSymbol.Length > 2 ? normalizedSymbol[2..] : normalizedSymbol;
        var mergedText = string.Join(' ', new[] { title, translatedTitle, aiTarget }.Where(item => !string.IsNullOrWhiteSpace(item)));

        if (!string.IsNullOrWhiteSpace(aiTarget))
        {
            if (aiTarget.Contains("个股:", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(stockName) && aiTarget.Contains(stockName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (aiTarget.Contains(normalizedSymbol, StringComparison.OrdinalIgnoreCase)
                    || aiTarget.Contains(bareSymbol, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (aiTarget.Contains("无明确靶点", StringComparison.OrdinalIgnoreCase)
                || aiTarget.Contains("大盘", StringComparison.OrdinalIgnoreCase)
                || aiTarget.Contains("板块:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(stockName) && mergedText.Contains(stockName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return mergedText.Contains(normalizedSymbol, StringComparison.OrdinalIgnoreCase)
            || mergedText.Contains(bareSymbol, StringComparison.OrdinalIgnoreCase);
    }

    public static string? SanitizeEvidenceSnippet(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var sanitized = SanitizeEvidenceText(candidate);
            if (!string.IsNullOrWhiteSpace(sanitized))
            {
                return sanitized;
            }
        }

        return null;
    }

    private static bool LooksLikeClearChineseTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var chineseCount = value.Count(IsChineseCharacter);
        var letterCount = value.Count(char.IsLetter);
        return chineseCount >= 6 && chineseCount * 2 >= Math.Max(letterCount, 1);
    }

    private static bool IsChineseCharacter(char value)
    {
        return value >= 0x4E00 && value <= 0x9FFF;
    }

    private static string? SanitizeEvidenceText(string? value)
    {
        var normalized = NormalizeText(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        normalized = normalized
            .Replace('\u00A0', ' ')
            .Replace('\u3000', ' ');
        normalized = WhitespaceRegex.Replace(normalized, " ").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        normalized = StripLeadingNavigationTerms(normalized);
        if (string.IsNullOrWhiteSpace(normalized) || IsNavigationNoise(normalized))
        {
            return null;
        }

        var segments = SentenceSplitRegex.Split(normalized)
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Where(item => !IsNavigationNoise(item))
            .Take(2)
            .ToArray();

        var readable = segments.Length > 0 ? string.Join(' ', segments) : normalized;
        return TrimSnippet(readable);
    }

    private static string StripLeadingNavigationTerms(string value)
    {
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 6)
        {
            return value;
        }

        var index = 0;
        while (index < tokens.Length && NavigationNoiseKeywords.Contains(tokens[index]))
        {
            index += 1;
        }

        return index >= 6 && index < tokens.Length
            ? string.Join(' ', tokens[index..])
            : value;
    }

    private static bool IsNavigationNoise(string value)
    {
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 6)
        {
            return false;
        }

        var navigationCount = tokens.Count(token => NavigationNoiseKeywords.Contains(token));
        var shortTokenCount = tokens.Count(token => token.Length <= 4);
        var punctuationCount = value.Count(ch => ch is '。' or '！' or '？' or '；' or ';' or '.' or '!' or '?');

        return navigationCount >= 5
            && shortTokenCount * 2 >= tokens.Length
            && punctuationCount == 0;
    }

    private static string? TrimSnippet(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        const int maxLength = 120;
        if (value.Length <= maxLength)
        {
            return value;
        }

        var truncated = value[..maxLength].TrimEnd();
        var boundary = truncated.LastIndexOfAny(new[] { '。', '；', ';', '，', ',', ' ' });
        if (boundary >= 40)
        {
            truncated = truncated[..boundary].TrimEnd();
        }

        return $"{truncated}...";
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}