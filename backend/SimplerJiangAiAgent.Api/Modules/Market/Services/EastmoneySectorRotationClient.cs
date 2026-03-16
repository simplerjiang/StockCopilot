using System.Globalization;
using System.Text.Json;
using SimplerJiangAiAgent.Api.Modules.Market.Models;

namespace SimplerJiangAiAgent.Api.Modules.Market.Services;

public sealed class EastmoneySectorRotationClient : IEastmoneySectorRotationClient
{
    private readonly HttpClient _httpClient;

    public EastmoneySectorRotationClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<EastmoneySectorBoardRow>> GetBoardRankingsAsync(string boardType, int take, CancellationToken cancellationToken = default)
    {
        var filter = NormalizeBoardFilter(boardType);
        var url = $"https://push2.eastmoney.com/api/qt/clist/get?pn=1&pz={Math.Clamp(take, 1, 200)}&po=1&np=1&fltt=2&invt=2&fid=f3&fs={Uri.EscapeDataString(filter)}&fields=f12,f14,f3,f62,f66,f69,f72,f75,f78,f81,f84,f87,f184,f6";
        using var document = await GetDocumentAsync(url, cancellationToken);
        return GetDiffRows(document)
            .Select((item, index) => new EastmoneySectorBoardRow(
                boardType,
                GetString(item, "f12"),
                GetString(item, "f14"),
                GetDecimal(item, "f3"),
                GetDecimal(item, "f62"),
                GetDecimal(item, "f66"),
                GetDecimal(item, "f72"),
                GetDecimal(item, "f78"),
                GetDecimal(item, "f84"),
                GetDecimal(item, "f6"),
                GetDecimal(item, "f184"),
                index + 1,
                item.GetRawText()))
            .Where(item => !string.IsNullOrWhiteSpace(item.SectorCode) && !string.IsNullOrWhiteSpace(item.SectorName))
            .ToArray();
    }

    public async Task<IReadOnlyList<EastmoneySectorLeaderRow>> GetSectorLeadersAsync(string sectorCode, int take, CancellationToken cancellationToken = default)
    {
        var url = $"https://push2.eastmoney.com/api/qt/clist/get?pn=1&pz={Math.Clamp(take, 1, 200)}&po=1&np=1&fltt=2&invt=2&fid=f3&fs=b:{Uri.EscapeDataString(sectorCode)}&fields=f12,f14,f3,f6,f7,f8,f15,f16,f17,f18";
        using var document = await GetDocumentAsync(url, cancellationToken);
        return GetDiffRows(document)
            .Select((item, index) =>
            {
                var changePercent = GetDecimal(item, "f3");
                return new EastmoneySectorLeaderRow(
                    index + 1,
                    GetString(item, "f12"),
                    GetString(item, "f14"),
                    changePercent,
                    GetDecimal(item, "f6"),
                    changePercent >= 9.7m,
                    false);
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Symbol) && !string.IsNullOrWhiteSpace(item.Name))
            .ToArray();
    }

    public async Task<EastmoneyMarketBreadthSnapshot> GetMarketBreadthAsync(int take, CancellationToken cancellationToken = default)
    {
        var targetCount = Math.Clamp(take, 200, 6000);
        var rows = new Dictionary<string, (decimal ChangePercent, decimal TurnoverAmount)>(StringComparer.OrdinalIgnoreCase);

        using var firstDescendingPage = await GetDocumentAsync(BuildMarketBreadthUrl(1, descending: true), cancellationToken);
        var total = GetTotalCount(firstDescendingPage);
        AppendMarketBreadthRows(firstDescendingPage, rows);

        if (total > 0 && total <= targetCount)
        {
            var pageCount = (int)Math.Ceiling(total / 100m);
            for (var page = 2; page <= pageCount; page += 1)
            {
                using var document = await GetDocumentAsync(BuildMarketBreadthUrl(page, descending: true), cancellationToken);
                AppendMarketBreadthRows(document, rows);
            }
        }
        else
        {
            var perSideCount = (int)Math.Ceiling(targetCount / 2m);
            var pageCount = (int)Math.Ceiling(perSideCount / 100m);

            for (var page = 2; page <= pageCount; page += 1)
            {
                using var document = await GetDocumentAsync(BuildMarketBreadthUrl(page, descending: true), cancellationToken);
                AppendMarketBreadthRows(document, rows);
            }

            for (var page = 1; page <= pageCount; page += 1)
            {
                using var document = await GetDocumentAsync(BuildMarketBreadthUrl(page, descending: false), cancellationToken);
                AppendMarketBreadthRows(document, rows);
            }
        }

        var advancers = 0;
        var decliners = 0;
        var flatCount = 0;
        var totalTurnover = 0m;
        foreach (var row in rows.Values)
        {
            var changePercent = row.ChangePercent;
            if (changePercent > 0)
            {
                advancers += 1;
            }
            else if (changePercent < 0)
            {
                decliners += 1;
            }
            else
            {
                flatCount += 1;
            }

            totalTurnover += Math.Max(0, row.TurnoverAmount);
        }

        return new EastmoneyMarketBreadthSnapshot(advancers, decliners, flatCount, totalTurnover);
    }

    public async Task<int> GetLimitUpCountAsync(DateOnly tradingDate, CancellationToken cancellationToken = default)
    {
        using var document = await GetDocumentAsync(BuildTopicPoolUrl("getTopicZTPool", tradingDate, null), cancellationToken);
        return GetTopicCount(document);
    }

    public async Task<int> GetLimitDownCountAsync(DateOnly tradingDate, CancellationToken cancellationToken = default)
    {
        using var document = await GetDocumentAsync(BuildTopicPoolUrl("getTopicDTPool", tradingDate, "zdp:asc"), cancellationToken);
        return GetTopicCount(document);
    }

    public async Task<int> GetBrokenBoardCountAsync(DateOnly tradingDate, CancellationToken cancellationToken = default)
    {
        using var document = await GetDocumentAsync(BuildTopicPoolUrl("getTopicZBPool", tradingDate, null), cancellationToken);
        return GetTopicCount(document);
    }

    public async Task<int> GetMaxLimitUpStreakAsync(DateOnly tradingDate, CancellationToken cancellationToken = default)
    {
        using var document = await GetDocumentAsync(BuildTopicPoolUrl("getTopicZTPool", tradingDate, null), cancellationToken);
        var rows = GetPoolRows(document);
        var max = 0;
        foreach (var item in rows)
        {
            max = Math.Max(max, GetInt(item, "lbc"));
        }

        return max;
    }

    private async Task<JsonDocument> GetDocumentAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        request.Headers.TryAddWithoutValidation("Referer", "https://quote.eastmoney.com/");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(payload);
    }

    internal static string NormalizeBoardFilter(string boardType)
    {
        return boardType switch
        {
            SectorBoardTypes.Industry => "m:90+t:2",
            SectorBoardTypes.Concept => "m:90+t:3",
            SectorBoardTypes.Style => "m:90+t:3",
            _ => throw new ArgumentOutOfRangeException(nameof(boardType), boardType, "Unsupported board type")
        };
    }

    private static string BuildTopicPoolUrl(string endpoint, DateOnly tradingDate, string? sort)
    {
        var sortSegment = string.IsNullOrWhiteSpace(sort) ? string.Empty : $"&sort={sort}";
        return $"https://push2ex.eastmoney.com/{endpoint}?ut=7eea3edcaed734bea9cbfc24409ed989&dpt=wz.ztzt&Pageindex=0&pagesize=1000{sortSegment}&date={tradingDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}";
    }

    private static string BuildMarketBreadthUrl(int pageNumber, bool descending)
    {
        return $"https://push2.eastmoney.com/api/qt/clist/get?pn={pageNumber}&pz=100&po={(descending ? 1 : 0)}&np=1&fltt=2&invt=2&fid=f3&fs={Uri.EscapeDataString("m:0+t:6,m:0+t:80,m:1+t:2,m:1+t:23")}&fields=f12,f3,f6";
    }

    private static IEnumerable<JsonElement> GetDiffRows(JsonDocument document)
    {
        if (!TryGetDataObject(document, out var data)
            || !data.TryGetProperty("diff", out var diff)
            || diff.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<JsonElement>();
        }

        return diff.EnumerateArray().ToArray();
    }

    private static IEnumerable<JsonElement> GetPoolRows(JsonDocument document)
    {
        if (!TryGetDataObject(document, out var data))
        {
            return Array.Empty<JsonElement>();
        }

        if (data.TryGetProperty("pool", out var pool) && pool.ValueKind == JsonValueKind.Array)
        {
            return pool.EnumerateArray().ToArray();
        }

        return Array.Empty<JsonElement>();
    }

    private static int GetTotalCount(JsonDocument document)
    {
        if (!TryGetDataObject(document, out var data))
        {
            return 0;
        }

        return data.TryGetProperty("total", out var totalElement) && totalElement.TryGetInt32(out var total)
            ? total
            : 0;
    }

    private static void AppendMarketBreadthRows(JsonDocument document, IDictionary<string, (decimal ChangePercent, decimal TurnoverAmount)> rows)
    {
        foreach (var item in GetDiffRows(document))
        {
            var symbol = GetString(item, "f12");
            if (string.IsNullOrWhiteSpace(symbol) || rows.ContainsKey(symbol))
            {
                continue;
            }

            rows[symbol] = (GetDecimal(item, "f3"), GetDecimal(item, "f6"));
        }
    }

    private static int GetTopicCount(JsonDocument document)
    {
        if (!TryGetDataObject(document, out var data))
        {
            return 0;
        }

        if (data.TryGetProperty("tc", out var totalCount) && totalCount.TryGetInt32(out var count))
        {
            return count;
        }

        return GetPoolRows(document).Count();
    }

    private static bool TryGetDataObject(JsonDocument document, out JsonElement data)
    {
        if (!document.RootElement.TryGetProperty("data", out data) || data.ValueKind != JsonValueKind.Object)
        {
            data = default;
            return false;
        }

        return true;
    }

    private static string GetString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            _ => string.Empty
        };
    }

    private static decimal GetDecimal(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return 0m;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetDecimal(out var decimalValue) ? decimalValue : 0m;
        }

        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0m;
    }

    private static int GetInt(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetInt32(out var intValue) ? intValue : 0;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0;
    }
}
