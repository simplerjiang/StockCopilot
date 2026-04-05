using System.Text.Json;
using System.Text.RegularExpressions;

namespace SimplerJiangAiAgent.FinancialWorker.Services;

/// <summary>
/// 多个 Eastmoney 通道共享的静态工具方法
/// </summary>
public static class FinanceClientHelper
{
    /// <summary>6开头 = SH, 0/3开头 = SZ</summary>
    public static string GetMarketPrefix(string symbol) =>
        symbol.StartsWith('6') ? "SH" : "SZ";

    /// <summary>小写前缀，用于 HTML 页面 URL</summary>
    public static string GetMarketPrefixLower(string symbol) =>
        symbol.StartsWith('6') ? "sh" : "sz";

    /// <summary>根据报告期月份判断报告类型</summary>
    public static string ParseReportType(string reportDate)
    {
        if (reportDate.Length >= 7 && int.TryParse(reportDate.Substring(5, 2), out var month))
        {
            return month switch
            {
                12 => "Annual",
                3 => "Q1",
                6 => "Q2",
                9 => "Q3",
                _ => "Other",
            };
        }
        return "Other";
    }

    /// <summary>将 JsonElement 对象的所有属性展平为字典</summary>
    public static Dictionary<string, object?> ParseJsonToDict(JsonElement element)
    {
        var dict = new Dictionary<string, object?>();
        if (element.ValueKind != JsonValueKind.Object)
            return dict;

        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.Number => prop.Value.GetDouble(),
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => prop.Value.ToString(),
            };
        }
        return dict;
    }

    private static readonly Regex ChineseNumberRegex = new(
        @"^([+-]?\d+(?:\.\d+)?)\s*(亿|万|%)?$",
        RegexOptions.Compiled);

    /// <summary>
    /// 解析中文数字格式的值。
    /// 数字 → double；"XXX亿" → *10000（万元基准下）；"XXX万" → *1；"--"/空/null → null；百分号 → 去掉%保留数字
    /// </summary>
    public static object? ParseChineseNumber(object? value)
    {
        if (value is null) return null;
        if (value is double d) return d;
        if (value is int i) return (double)i;
        if (value is long l) return (double)l;

        var s = value.ToString()?.Trim();
        if (string.IsNullOrEmpty(s) || s == "--") return null;

        var match = ChineseNumberRegex.Match(s);
        if (match.Success && double.TryParse(match.Groups[1].Value, out var num))
        {
            var unit = match.Groups[2].Value;
            return unit switch
            {
                "亿" => num * 10000,  // 亿 = 10000万
                "万" => num,
                "%" => num,           // 去掉百分号，保留数字
                _ => num,
            };
        }

        // 尝试纯数字（无单位）
        if (double.TryParse(s, out var plain))
            return plain;

        return null;
    }
}
