using System.Globalization;
using System.Text.Json;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

internal static class EastmoneySectorSearchParser
{
    public static IReadOnlyList<LocalSectorReportSeed> Parse(
        string symbol,
        string? sectorName,
        string raw,
        DateTime crawledAt)
    {
        if (string.IsNullOrWhiteSpace(sectorName) || string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<LocalSectorReportSeed>();
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var listNode = FindListNode(document.RootElement);
            if (listNode is null || listNode.Value.Value.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<LocalSectorReportSeed>();
            }

            var result = new List<LocalSectorReportSeed>();
            foreach (var item in listNode.Value.Value.EnumerateArray())
            {
                var title = ReadString(item, "Title", "title", "Name", "name");
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                result.Add(new LocalSectorReportSeed(
                    symbol,
                    sectorName,
                    "sector",
                    title,
                    ReadString(item, "SourceName", "sourceName", "Source", "source") ?? "东方财富板块搜索",
                    "eastmoney-sector-search",
                    ReadString(item, "InfoCode", "infoCode", "Code", "code", "ID", "id"),
                    ParseTime(ReadString(item, "PublishTime", "publishTime", "DisplayTime", "displayTime", "showTime", "ShowTime")),
                    crawledAt,
                    ReadString(item, "Url", "url", "ArticleUrl", "articleUrl", "ShareUrl", "shareUrl")));
            }

            return result
                .OrderByDescending(item => item.PublishTime)
                .DistinctBy(item => item.Url ?? item.ExternalId ?? item.Title)
                .Take(20)
                .ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<LocalSectorReportSeed>();
        }
    }

    private static KeyValuePair<string, JsonElement>? FindListNode(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in new[] { "Data", "data" })
        {
            if (root.TryGetProperty(propertyName, out var dataNode))
            {
                if (dataNode.ValueKind == JsonValueKind.Array)
                {
                    return new KeyValuePair<string, JsonElement>(propertyName, dataNode);
                }

                if (dataNode.ValueKind == JsonValueKind.Object)
                {
                    foreach (var listName in new[] { "Data", "data", "List", "list" })
                    {
                        if (dataNode.TryGetProperty(listName, out var listNode) && listNode.ValueKind == JsonValueKind.Array)
                        {
                            return new KeyValuePair<string, JsonElement>(listName, listNode);
                        }
                    }
                }
            }
        }

        foreach (var listName in new[] { "List", "list" })
        {
            if (root.TryGetProperty(listName, out var listNode) && listNode.ValueKind == JsonValueKind.Array)
            {
                return new KeyValuePair<string, JsonElement>(listName, listNode);
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var node) && node.ValueKind == JsonValueKind.String)
            {
                var value = node.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static DateTime ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTime.UtcNow;
        }

        if (DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
        {
            return parsed;
        }

        return DateTime.UtcNow;
    }
}