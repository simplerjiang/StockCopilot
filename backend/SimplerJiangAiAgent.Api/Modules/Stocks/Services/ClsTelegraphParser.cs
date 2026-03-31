using System.Text.Json;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

internal static class ClsTelegraphParser
{
    public static IReadOnlyList<LocalSectorReportSeed> Parse(string json, DateTime crawledAt)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var dataNode)
            || dataNode.ValueKind != JsonValueKind.Object
            || !dataNode.TryGetProperty("roll_data", out var rollNode)
            || rollNode.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<LocalSectorReportSeed>();
        }

        var result = new List<LocalSectorReportSeed>();
        foreach (var item in rollNode.EnumerateArray())
        {
            var content = item.TryGetProperty("content", out var contentNode) ? contentNode.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            // Extract title from 【title】content pattern, or use first 80 chars
            var title = ExtractTitle(content);
            var ctime = item.TryGetProperty("ctime", out var ctimeNode) ? ctimeNode.GetInt64() : 0;
            var id = item.TryGetProperty("id", out var idNode) ? idNode.GetInt64().ToString() : null;

            var publishTime = ctime > 0
                ? DateTimeOffset.FromUnixTimeSeconds(ctime).UtcDateTime
                : DateTime.UtcNow;

            result.Add(new LocalSectorReportSeed(
                null,
                "大盘环境",
                "market",
                title,
                "财联社",
                "cls-telegraph",
                id,
                publishTime,
                crawledAt,
                id is not null ? $"https://www.cls.cn/detail/{id}" : null));
        }

        return result;
    }

    private static string ExtractTitle(string content)
    {
        // CLS content format: 【headline】body text
        var start = content.IndexOf('【');
        var end = content.IndexOf('】');
        if (start >= 0 && end > start)
        {
            return content[(start + 1)..end];
        }

        // Fallback: use first 80 chars as title
        return content.Length <= 80 ? content : content[..80];
    }
}
