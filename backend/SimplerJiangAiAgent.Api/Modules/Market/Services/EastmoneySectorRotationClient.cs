using System.Globalization;
using System.Text.Json;
using System.Diagnostics;
using SimplerJiangAiAgent.Api.Modules.Market;
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
        var sourceKey = $"bkzj_board_rankings_{boardType}";
        var mergedSourceKey = "bkzj_board_rankings";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var boardCode = NormalizeBoardFilter(boardType);
            var pageSize = Math.Clamp(take, 1, 200);
            using var f3Document = await GetDocumentAsync(BuildBkzjUrl("f3", boardCode, pageSize), cancellationToken);
            using var f62Document = await GetDocumentAsync(BuildBkzjUrl("f62", boardCode, pageSize), cancellationToken);

            var rankedByChange = GetBkzjRows(f3Document)
                .Select((item, index) => new BoardMergeRow(ToBoardRow(boardType, item, index + 1), index + 1, null))
                .ToList();
            var rankedByMainFlow = GetBkzjRows(f62Document)
                .Select((item, index) => new BoardMergeRow(ToBoardRow(boardType, item, index + 1), null, index + 1))
                .ToList();

            var merged = MergeBoardRows(rankedByChange, rankedByMainFlow, take);
            DataSourceTracker.RecordSourceSuccess(sourceKey, stopwatch.Elapsed.TotalMilliseconds);
            DataSourceTracker.RecordSourceSuccess(mergedSourceKey, stopwatch.Elapsed.TotalMilliseconds);
            return merged;
        }
        catch (Exception ex)
        {
            DataSourceTracker.RecordSourceFailure(sourceKey, ex, stopwatch.Elapsed.TotalMilliseconds);
            DataSourceTracker.RecordSourceFailure(mergedSourceKey, ex, stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
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
        const string breadthSourceKey = "eastmoney_market_breadth_clist";
        var breadthSourceStopwatch = Stopwatch.StartNew();
        var targetCount = Math.Clamp(take, 200, 6000);
        var rows = new Dictionary<string, (decimal ChangePercent, decimal TurnoverAmount)>(StringComparer.OrdinalIgnoreCase);
        try
        {
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

            DataSourceTracker.RecordSourceSuccess(breadthSourceKey, breadthSourceStopwatch.Elapsed.TotalMilliseconds);
            return new EastmoneyMarketBreadthSnapshot(advancers, decliners, flatCount, totalTurnover);
        }
        catch (Exception ex)
        {
            DataSourceTracker.RecordSourceFailure(breadthSourceKey, ex, breadthSourceStopwatch.Elapsed.TotalMilliseconds);

            throw;
        }
    }

    public async Task<decimal> GetTotalMarketTurnoverAsync(CancellationToken cancellationToken = default)
    {
        const string sourceKey = "eastmoney_market_fs_sh_sz";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var document = await GetDocumentAsync(BuildMarketTurnoverUlistUrl(), cancellationToken);
            var total = SumTurnoverFromDocument(document);
            DataSourceTracker.RecordSourceSuccess(sourceKey, stopwatch.Elapsed.TotalMilliseconds);
            return total;
        }
        catch (Exception ex)
        {
            DataSourceTracker.RecordSourceFailure(sourceKey, ex, stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
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
        var sourceKey = "ths_continuous_limit_up";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var thsDocument = await GetDocumentAsync(BuildThsContinuousLimitUpUrl(tradingDate), cancellationToken);
            var maxFromThs = GetMaxHeightFromThs(thsDocument);
            DataSourceTracker.RecordSourceSuccess(sourceKey, stopwatch.Elapsed.TotalMilliseconds);
            return maxFromThs;
        }
        catch (Exception ex)
        {
            DataSourceTracker.RecordSourceFailure(sourceKey, ex, stopwatch.Elapsed.TotalMilliseconds);
        }

        using var fallbackDocument = await GetDocumentAsync(BuildTopicPoolUrl("getTopicZTPool", tradingDate, null), cancellationToken);
        var rows = GetPoolRows(fallbackDocument);
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
            SectorBoardTypes.Industry => "m:90+s:4",
            SectorBoardTypes.Concept => "m:90+t:3",
            SectorBoardTypes.Style => "m:90+t:1",
            _ => throw new ArgumentOutOfRangeException(nameof(boardType), boardType, "Unsupported board type")
        };
    }

    private static string BuildBkzjUrl(string key, string boardCode, int pageSize)
    {
        return $"https://data.eastmoney.com/dataapi/bkzj/getbkzj?sortField={Uri.EscapeDataString(key)}&sortDirec=1&pageNum=1&pageSize={Math.Clamp(pageSize, 1, 200)}&code={Uri.EscapeDataString(boardCode)}&key={Uri.EscapeDataString(key)}";
    }

    private static string BuildThsContinuousLimitUpUrl(DateOnly tradingDate)
    {
        return $"https://data.10jqka.com.cn/dataapi/limit_up/continuous_limit_up?date={tradingDate:yyyyMMdd}&page=1&limit=100";
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

    private static string BuildMarketTurnoverUlistUrl()
    {
        return "https://push2.eastmoney.com/api/qt/ulist.np/get?fltt=2&fields=f12,f13,f14,f2,f3,f4,f5,f6,f8,f9,f10,f15,f16,f124,f152&secids=1.000001,0.399001";
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

    private static IEnumerable<JsonElement> GetBkzjRows(JsonDocument document)
    {
        if (TryGetArray(document.RootElement, out var rootArray))
        {
            return rootArray.EnumerateArray().ToArray();
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            if (document.RootElement.TryGetProperty("data", out var data) && TryGetArray(data, out var dataArray))
            {
                return dataArray.EnumerateArray().ToArray();
            }

            if (document.RootElement.TryGetProperty("result", out var result) && TryGetArray(result, out var resultArray))
            {
                return resultArray.EnumerateArray().ToArray();
            }

            if (document.RootElement.TryGetProperty("result", out result) && result.ValueKind == JsonValueKind.Object)
            {
                if (result.TryGetProperty("data", out var resultData) && TryGetArray(resultData, out var nestedArray))
                {
                    return nestedArray.EnumerateArray().ToArray();
                }
            }
        }

        return Array.Empty<JsonElement>();
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

    private static decimal SumTurnoverFromDocument(JsonDocument document)
    {
        return GetDiffRows(document).Sum(item => Math.Max(0, GetDecimal(item, "f6")));
    }

    private EastmoneySectorBoardRow ToBoardRow(string boardType, JsonElement item, int rankNo)
    {
        var mainFlow = GetDecimal(item, "f62");
        return new EastmoneySectorBoardRow(
            boardType,
            GetString(item, "f12"),
            GetString(item, "f14"),
            GetDecimal(item, "f3"),
            mainFlow,
            0m,
            0m,
            0m,
            0m,
            mainFlow,
            0m,
            rankNo,
            item.GetRawText());
    }

    private static IReadOnlyList<EastmoneySectorBoardRow> MergeBoardRows(
        IReadOnlyList<BoardMergeRow> rankedByChange,
        IReadOnlyList<BoardMergeRow> rankedByMainFlow,
        int take)
    {
        var merged = new Dictionary<string, BoardMergeRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in rankedByChange)
        {
            if (string.IsNullOrWhiteSpace(item.Row.SectorCode))
            {
                continue;
            }

            merged[item.Row.SectorCode] = item;
        }

        foreach (var item in rankedByMainFlow)
        {
            if (string.IsNullOrWhiteSpace(item.Row.SectorCode))
            {
                continue;
            }

            if (merged.TryGetValue(item.Row.SectorCode, out var existing))
            {
                var preferredName = !string.IsNullOrWhiteSpace(existing.Row.SectorName) ? existing.Row.SectorName : item.Row.SectorName;
                var changePercent = existing.Row.ChangePercent != 0 ? existing.Row.ChangePercent : item.Row.ChangePercent;
                var mainFlow = item.Row.MainNetInflow != 0 ? item.Row.MainNetInflow : existing.Row.MainNetInflow;
                var mergedRow = existing.Row with
                {
                    SectorName = preferredName,
                    ChangePercent = changePercent,
                    MainNetInflow = mainFlow,
                    TurnoverAmount = mainFlow,
                    RawJson = existing.Row.RawJson.Length >= item.Row.RawJson.Length ? existing.Row.RawJson : item.Row.RawJson
                };
                merged[item.Row.SectorCode] = existing with { Row = mergedRow, RankByMainFlow = item.RankByMainFlow };
                continue;
            }

            merged[item.Row.SectorCode] = item;
        }

        return merged.Values
            .Where(x => !string.IsNullOrWhiteSpace(x.Row.SectorName))
            .OrderBy(x => Math.Min(x.RankByChange ?? int.MaxValue, x.RankByMainFlow ?? int.MaxValue))
            .ThenBy(x => (x.RankByChange ?? int.MaxValue) + (x.RankByMainFlow ?? int.MaxValue))
            .ThenBy(x => x.Row.SectorCode, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(take, 1, 200))
            .Select((x, index) => x.Row with { RankNo = index + 1 })
            .ToArray();
    }

    private static int GetMaxHeightFromThs(JsonDocument document)
    {
        return GetMaxHeightFromElement(document.RootElement);
    }

    private static int GetMaxHeightFromElement(JsonElement element)
    {
        var max = 0;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("height"))
                    {
                        max = Math.Max(max, ParseInt(property.Value));
                    }

                    max = Math.Max(max, GetMaxHeightFromElement(property.Value));
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    max = Math.Max(max, GetMaxHeightFromElement(item));
                }

                break;
        }

        return max;
    }

    private static bool TryGetArray(JsonElement element, out JsonElement array)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            array = element;
            return true;
        }

        array = default;
        return false;
    }

    private static int ParseInt(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private sealed record BoardMergeRow(EastmoneySectorBoardRow Row, int? RankByChange, int? RankByMainFlow);

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
