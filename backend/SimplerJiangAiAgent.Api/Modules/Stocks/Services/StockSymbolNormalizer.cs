using System.Text.RegularExpressions;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public static class StockSymbolNormalizer
{
    // A股: 可选 sh/sz/bj 前缀 + 6位数字
    private static readonly Regex AShareWithPrefix = new(@"^(sh|sz|bj)\d{6}$", RegexOptions.Compiled);
    // 港股: hk 前缀 + 1~5位数字（当前产品不支持，仅用于友好识别外盘）
    private static readonly Regex HkStock = new(@"^hk\d{1,5}$", RegexOptions.Compiled);
    // 裸6位数字（A股无前缀）
    private static readonly Regex BareAShare = new(@"^\d{6}$", RegexOptions.Compiled);

    /// <summary>
    /// 判断股票代码是否为合法格式（当前仅支持 A股/北交所）。
    /// </summary>
    public static bool IsValid(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        var trimmed = symbol.Trim().ToLowerInvariant();
        return AShareWithPrefix.IsMatch(trimmed) || BareAShare.IsMatch(trimmed);
    }

    /// <summary>
    /// 检测是否为外盘符号（us./gb./jp. 等前缀），用于返回更友好的错误信息。
    /// </summary>
    public static bool IsForeignMarket(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return false;
        var trimmed = symbol.Trim().ToLowerInvariant();
        return trimmed.StartsWith("us.")
            || trimmed.StartsWith("gb.")
            || trimmed.StartsWith("jp.")
            || trimmed.StartsWith("hk.")
            || HkStock.IsMatch(trimmed);
    }

    /// <summary>
    /// 判断是否为指数代码（000xxx / 399xxx），如上证指数 sh000001、深证成指 sz399001。
    /// </summary>
    public static bool IsIndex(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return false;
        var trimmed = symbol.Trim().ToLowerInvariant();
        if (trimmed.StartsWith("sh"))
        {
            var code = trimmed[2..];
            return code.Length == 6 && code.StartsWith("000");
        }

        if (trimmed.StartsWith("sz"))
        {
            var code = trimmed[2..];
            return code.Length == 6 && code.StartsWith("399");
        }

        if (trimmed.StartsWith("bj"))
        {
            return false;
        }

        return trimmed.Length == 6 && trimmed.All(char.IsDigit) && trimmed.StartsWith("399");
    }

    public static string Normalize(string symbol)
    {
        var trimmed = symbol.Trim().ToLowerInvariant();
        if (trimmed.StartsWith("sh") || trimmed.StartsWith("sz") || trimmed.StartsWith("bj"))
        {
            return trimmed;
        }

        if (trimmed.StartsWith("hk"))
        {
            return trimmed;
        }

        if (trimmed.Length == 6 && trimmed.All(char.IsDigit))
        {
            return trimmed[0] switch
            {
                '6' => $"sh{trimmed}",       // 上交所（含科创板 688xxx）
                '4' or '8' => $"bj{trimmed}", // 北交所
                _ => $"sz{trimmed}",          // 深交所（0/2/3 开头）
            };
        }

        return trimmed;
    }
}
