namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public static class StockSymbolNormalizer
{
    public static string Normalize(string symbol)
    {
        var trimmed = symbol.Trim().ToLowerInvariant();
        if (trimmed.StartsWith("sh") || trimmed.StartsWith("sz"))
        {
            return trimmed;
        }

        if (trimmed.Length == 6 && trimmed.All(char.IsDigit))
        {
            return trimmed.StartsWith("6") ? $"sh{trimmed}" : $"sz{trimmed}";
        }

        return trimmed;
    }
}
