using System.Globalization;
using System.Text.Json;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

internal static class EastmoneyMarketNewsParser
{
    public static IReadOnlyList<LocalSectorReportSeed> Parse(string json, string sourceTag, DateTime crawledAt)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var dataNode)
            || dataNode.ValueKind != JsonValueKind.Object
            || !dataNode.TryGetProperty("list", out var listNode)
            || listNode.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<LocalSectorReportSeed>();
        }

        var result = new List<LocalSectorReportSeed>();
        foreach (var item in listNode.EnumerateArray())
        {
            var title = item.TryGetProperty("title", out var titleNode) ? titleNode.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var code = item.TryGetProperty("code", out var codeNode) ? codeNode.GetString() : null;
            var showTime = item.TryGetProperty("showTime", out var timeNode) ? timeNode.GetString() : null;
            var mediaName = item.TryGetProperty("mediaName", out var mediaNode) ? mediaNode.GetString() : null;
            var url = item.TryGetProperty("url", out var urlNode) ? urlNode.GetString() : null;

            var publishTime = ParseTime(showTime);
            var source = string.IsNullOrWhiteSpace(mediaName) ? "东方财富资讯" : mediaName;

            result.Add(new LocalSectorReportSeed(
                null,
                "大盘环境",
                "market",
                title,
                source,
                sourceTag,
                code,
                publishTime,
                crawledAt,
                url));
        }

        return result;
    }

    private static DateTime ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTime.UtcNow;
        }

        // Eastmoney showTime is always in China Standard Time (UTC+8)
        if (DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed.AddHours(-8); // Convert CST to UTC
        }

        return DateTime.UtcNow;
    }
}
