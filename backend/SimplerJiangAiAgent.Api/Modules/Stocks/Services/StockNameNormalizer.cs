using System.Text.RegularExpressions;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public static class StockNameNormalizer
{
    private static readonly Regex StPrefixWhitespace = new(@"^(?<prefix>S\*ST|\*ST|ST)\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string NormalizeDisplayName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var trimmed = name.Trim();
        return StPrefixWhitespace.Replace(trimmed, match => match.Groups["prefix"].Value, count: 1);
    }

    public static string? NormalizeDisplayNameOrNull(string? name)
    {
        var normalized = NormalizeDisplayName(name);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}