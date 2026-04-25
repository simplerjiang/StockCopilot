using System.Globalization;
using System.Text.Json;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

internal static class EastmoneyAnnouncementParser
{
    private static readonly TimeZoneInfo ChinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
    public static IReadOnlyList<LocalStockNewsSeed> Parse(
        string symbol,
        string name,
        string? sectorName,
        string json,
        DateTime crawledAt)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var dataNode)
            || dataNode.ValueKind != JsonValueKind.Object
            || !dataNode.TryGetProperty("list", out var listNode)
            || listNode.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<LocalStockNewsSeed>();
        }

        var result = new List<LocalStockNewsSeed>();
        foreach (var item in listNode.EnumerateArray())
        {
            var title = item.TryGetProperty("title", out var titleNode) ? titleNode.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var artCode = item.TryGetProperty("art_code", out var artCodeNode) ? artCodeNode.GetString() : null;
            var publishText = item.TryGetProperty("display_time", out var displayNode) ? displayNode.GetString() : null;
            var publishTime = ParseEastmoneyTime(publishText);
            var url = string.IsNullOrWhiteSpace(artCode)
                ? null
                : $"https://data.eastmoney.com/notices/detail/{symbol[2..]}/{artCode}.html";

            result.Add(new LocalStockNewsSeed(
                symbol,
                name,
                sectorName,
                title,
                "announcement",
                "东方财富公告",
                "eastmoney-announcement",
                artCode,
                publishTime,
                crawledAt,
                url));
        }

        return result;
    }

    private static DateTime ParseEastmoneyTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTime.UtcNow;
        }

        var normalized = value.Trim();
        var lastColon = normalized.LastIndexOf(':');
        if (lastColon > "yyyy-MM-dd HH:mm:ss".Length - 1)
        {
            normalized = normalized[..lastColon];
        }

        if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), ChinaTimeZone);
        }

        return DateTime.UtcNow;
    }
}