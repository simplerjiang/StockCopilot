namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

internal static class LocalFactDisplayPolicy
{
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

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}