using System.Globalization;
using System.Text.Json;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Modules.Market.Services;

public sealed class EastmoneyRealtimeMarketClient : IEastmoneyRealtimeMarketClient
{
    private const decimal YuanPerHundredMillion = 100000000m;
    private const decimal TenThousandPerHundredMillion = 10000m;
    private static readonly IReadOnlyDictionary<string, string> GlobalIndexSecIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["hsi"] = "100.HSI",
        ["hstech"] = "124.HSTECH",
        ["n225"] = "100.N225",
        ["ndx"] = "100.NDX",
        ["spx"] = "100.SPX",
        ["ftse"] = "100.FTSE",
        ["ks11"] = "100.KS11"
    };
    private readonly HttpClient _httpClient;

    public EastmoneyRealtimeMarketClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<BatchStockQuoteDto>> GetBatchQuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken = default)
    {
        var normalizedSymbols = symbols
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(StockSymbolNormalizer.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedSymbols.Length == 0)
        {
            return Array.Empty<BatchStockQuoteDto>();
        }

        var secIds = string.Join(',', normalizedSymbols.Select(ToEastmoneySecId));
        var url = $"https://push2.eastmoney.com/api/qt/ulist.np/get?fltt=2&fields=f12,f13,f14,f2,f3,f4,f5,f6,f8,f9,f10,f15,f16,f124,f152&secids={secIds}";
        try
        {
            using var document = await GetDocumentAsync(url, cancellationToken);
            if (TryGetDiffRows(document, out var rows))
            {
                var parsed = rows
                    .Select(ParseBatchQuote)
                    .Where(item => item is not null)
                    .Cast<BatchStockQuoteDto>()
                    .ToDictionary(item => item.Symbol, item => item, StringComparer.OrdinalIgnoreCase);

                if (parsed.Count > 0)
                {
                    return normalizedSymbols
                        .Where(parsed.ContainsKey)
                        .Select(symbol => parsed[symbol])
                        .ToArray();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }

        return await GetBatchQuotesBySingleRequestsAsync(normalizedSymbols, cancellationToken);
    }

    private async Task<IReadOnlyList<BatchStockQuoteDto>> GetBatchQuotesBySingleRequestsAsync(
        IReadOnlyList<string> normalizedSymbols,
        CancellationToken cancellationToken)
    {
        var tasks = normalizedSymbols.Select(async symbol =>
        {
            try
            {
                return await GetSingleQuoteAsync(symbol, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return null;
            }
        });

        var quotes = await Task.WhenAll(tasks);

        return quotes
            .Where(item => item is not null)
            .Cast<BatchStockQuoteDto>()
            .ToArray();
    }

    private async Task<BatchStockQuoteDto?> GetSingleQuoteAsync(string symbol, CancellationToken cancellationToken)
    {
        var secId = ToEastmoneySecId(symbol);
        var url = $"https://push2.eastmoney.com/api/qt/stock/get?secid={secId}&fields=f12,f13,f14,f2,f3,f4,f5,f6,f8,f9,f10,f15,f16,f124,f152";
        using var document = await GetDocumentAsync(url, cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var dataNode)
            || dataNode.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ParseBatchQuote(dataNode);
    }

    public async Task<MarketCapitalFlowSnapshotDto?> GetMainCapitalFlowAsync(CancellationToken cancellationToken = default)
    {
        const string url = "https://push2.eastmoney.com/api/qt/stock/fflow/kline/get?lmt=241&klt=1&secid=1.000001&secid2=0.399001&fields1=f1,f2,f3,f7&fields2=f51,f52,f53,f54,f55,f56,f57,f58,f59,f60,f61,f62,f63";
        using var document = await GetDocumentAsync(url, cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var dataNode)
            || !dataNode.TryGetProperty("klines", out var klineNode)
            || klineNode.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var points = new List<MarketCapitalFlowPointDto>();
        foreach (var item in klineNode.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var point = ParseMainCapitalFlowPoint(item.GetString());
            if (point is not null)
            {
                points.Add(point);
            }
        }

        if (points.Count == 0)
        {
            return null;
        }

        var latest = points[^1];
        return new MarketCapitalFlowSnapshotDto(
            latest.Timestamp,
            DateOnly.FromDateTime(latest.Timestamp),
            "亿元",
            latest.MainNetInflow,
            latest.SmallOrderNetInflow,
            latest.MediumOrderNetInflow,
            latest.LargeOrderNetInflow,
            latest.SuperLargeOrderNetInflow,
            points);
    }

    public async Task<NorthboundFlowSnapshotDto?> GetNorthboundFlowAsync(CancellationToken cancellationToken = default)
    {
        const string url = "https://push2.eastmoney.com/api/qt/kamt.rtmin/get?fields1=f1,f2,f3,f4&fields2=f51,f52,f53,f54,f55,f56";
        using var document = await GetDocumentAsync(url, cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var dataNode))
        {
            return await GetNorthboundSnapshotAsync(cancellationToken);
        }

        var label = dataNode.TryGetProperty("s2nDate", out var dateNode) ? dateNode.GetString() ?? string.Empty : string.Empty;
        if (!dataNode.TryGetProperty("s2n", out var s2nNode) || s2nNode.ValueKind != JsonValueKind.Array)
        {
            return await GetNorthboundSnapshotAsync(cancellationToken);
        }

        var points = new List<NorthboundFlowPointDto>();
        foreach (var item in s2nNode.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var point = ParseNorthboundPoint(item.GetString());
            if (point is not null)
            {
                points.Add(point);
            }
        }

        if (points.Count == 0)
        {
            return await GetNorthboundSnapshotAsync(cancellationToken);
        }

        var latest = points[^1];
        var snapshotTime = CombineTradingDateAndTime(label, latest.Time);
        return new NorthboundFlowSnapshotDto(
            snapshotTime,
            label,
            "亿元",
            latest.ShanghaiNetInflow,
            latest.ShanghaiBalance,
            latest.ShenzhenNetInflow,
            latest.ShenzhenBalance,
            latest.TotalNetInflow,
            points);
    }

    private async Task<NorthboundFlowSnapshotDto?> GetNorthboundSnapshotAsync(CancellationToken cancellationToken)
    {
        const string url = "https://push2.eastmoney.com/api/qt/kamt/get?fields1=f1,f2,f3,f4&fields2=f51,f52,f53,f54,f55,f56";
        using var document = await GetDocumentAsync(url, cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var dataNode)
            || dataNode.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var shanghai = ParseNorthboundSnapshotChannel(dataNode, "hk2sh");
        var shenzhen = ParseNorthboundSnapshotChannel(dataNode, "hk2sz");
        if (shanghai is null && shenzhen is null)
        {
            return null;
        }

        var snapshotDate = shanghai?.Date
            ?? shenzhen?.Date
            ?? DateOnly.FromDateTime(DateTime.Today);
        var snapshotTime = snapshotDate.ToDateTime(new TimeOnly(15, 0));
        var label = shanghai?.Label
            ?? shenzhen?.Label
            ?? snapshotDate.ToString("MM-dd", CultureInfo.InvariantCulture);
        var shanghaiNetInflow = shanghai?.NetInflow ?? 0m;
        var shanghaiBalance = shanghai?.Balance ?? 0m;
        var shenzhenNetInflow = shenzhen?.NetInflow ?? 0m;
        var shenzhenBalance = shenzhen?.Balance ?? 0m;

        return new NorthboundFlowSnapshotDto(
            snapshotTime,
            label,
            "亿元",
            shanghaiNetInflow,
            shanghaiBalance,
            shenzhenNetInflow,
            shenzhenBalance,
            shanghaiNetInflow + shenzhenNetInflow,
            Array.Empty<NorthboundFlowPointDto>());
    }

    public async Task<MarketBreadthDistributionDto?> GetBreadthDistributionAsync(CancellationToken cancellationToken = default)
    {
        const string url = "https://push2ex.eastmoney.com/getTopicZDFenBu?cb=callbackdata7930743&ut=7eea3edcaed734bea9cbfc24409ed989&dpt=wz.ztzt";
        var payload = await GetStringAsync(url, cancellationToken);
        var json = ExtractJsonpPayload(payload);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var dataNode)
            || !dataNode.TryGetProperty("qdate", out var qdateNode)
            || !dataNode.TryGetProperty("fenbu", out var fenbuNode)
            || fenbuNode.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var tradingDate = ParseTradingDate(qdateNode.GetRawText().Trim('"'));
        var buckets = new List<MarketBreadthBucketDto>();
        var advancers = 0;
        var decliners = 0;
        var flatCount = 0;
        var limitUpCount = 0;
        var limitDownCount = 0;

        foreach (var item in fenbuNode.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var property in item.EnumerateObject())
            {
                if (!int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bucket))
                {
                    continue;
                }

                var count = property.Value.TryGetInt32(out var parsedCount) ? parsedCount : 0;
                buckets.Add(new MarketBreadthBucketDto(bucket, FormatBreadthLabel(bucket), count));

                if (bucket > 0)
                {
                    advancers += count;
                }
                else if (bucket < 0)
                {
                    decliners += count;
                }
                else
                {
                    flatCount += count;
                }

                if (bucket >= 10)
                {
                    limitUpCount += count;
                }

                if (bucket <= -10)
                {
                    limitDownCount += count;
                }
            }
        }

        return new MarketBreadthDistributionDto(
            tradingDate,
            advancers,
            decliners,
            flatCount,
            limitUpCount,
            limitDownCount,
            buckets.OrderBy(item => item.ChangeBucket).ToArray());
    }

    private async Task<JsonDocument> GetDocumentAsync(string url, CancellationToken cancellationToken)
    {
        var payload = await GetStringAsync(url, cancellationToken);
        return JsonDocument.Parse(payload);
    }

    private async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        request.Headers.TryAddWithoutValidation("Referer", "https://quote.eastmoney.com/");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static bool TryGetDiffRows(JsonDocument document, out JsonElement.ArrayEnumerator rows)
    {
        if (document.RootElement.TryGetProperty("data", out var dataNode)
            && dataNode.TryGetProperty("diff", out var diffNode)
            && diffNode.ValueKind == JsonValueKind.Array)
        {
            rows = diffNode.EnumerateArray();
            return true;
        }

        rows = default;
        return false;
    }

    private static BatchStockQuoteDto? ParseBatchQuote(JsonElement item)
    {
        var code = GetString(item, "f12");
        var market = GetInt(item, "f13");
        var name = GetString(item, "f14");
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var timestampSeconds = GetLong(item, "f124");
        var timestamp = timestampSeconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(timestampSeconds).UtcDateTime
            : DateTime.UtcNow;

        return new BatchStockQuoteDto(
            ResolveSymbol(market, code),
            name,
            GetDecimal(item, "f2"),
            GetDecimal(item, "f4"),
            GetDecimal(item, "f3"),
            GetDecimal(item, "f15"),
            GetDecimal(item, "f16"),
            GetDecimal(item, "f8"),
            GetDecimal(item, "f9"),
            GetDecimal(item, "f6"),
            GetDecimal(item, "f10"),
            timestamp);
    }

    private static MarketCapitalFlowPointDto? ParseMainCapitalFlowPoint(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var parts = raw.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 6 || !DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var timestamp))
        {
            return null;
        }

        return new MarketCapitalFlowPointDto(
            timestamp,
            ToHundredMillion(parts[1], YuanPerHundredMillion),
            ToHundredMillion(parts[2], YuanPerHundredMillion),
            ToHundredMillion(parts[3], YuanPerHundredMillion),
            ToHundredMillion(parts[4], YuanPerHundredMillion),
            ToHundredMillion(parts[5], YuanPerHundredMillion));
    }

    private static NorthboundFlowPointDto? ParseNorthboundPoint(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var parts = raw.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 6 || parts[1] == "-" || !TimeSpan.TryParse(parts[0], CultureInfo.InvariantCulture, out var time))
        {
            return null;
        }

        return new NorthboundFlowPointDto(
            time,
            ToHundredMillion(parts[1], TenThousandPerHundredMillion),
            ToHundredMillion(parts[2], TenThousandPerHundredMillion),
            ToHundredMillion(parts[3], TenThousandPerHundredMillion),
            ToHundredMillion(parts[4], TenThousandPerHundredMillion),
            ToHundredMillion(parts[5], TenThousandPerHundredMillion));
    }

    private static NorthboundSnapshotChannelDto? ParseNorthboundSnapshotChannel(JsonElement dataNode, string propertyName)
    {
        if (!dataNode.TryGetProperty(propertyName, out var channelNode)
            || channelNode.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var label = GetString(channelNode, "date");
        var dateRaw = GetString(channelNode, "date2");
        var date = DateOnly.TryParseExact(dateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
            ? parsedDate
            : DateOnly.FromDateTime(DateTime.Today);

        return new NorthboundSnapshotChannelDto(
            label,
            date,
            ToHundredMillion(GetString(channelNode, "dayNetAmtIn"), TenThousandPerHundredMillion),
            ToHundredMillion(GetString(channelNode, "dayAmtRemain"), TenThousandPerHundredMillion));
    }

    private static decimal ToHundredMillion(string? raw, decimal divisor)
    {
        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? decimal.Round(value / divisor, 2)
            : 0m;
    }

    private static string ExtractJsonpPayload(string payload)
    {
        var start = payload.IndexOf('(');
        var end = payload.LastIndexOf(')');
        return start >= 0 && end > start ? payload[(start + 1)..end] : string.Empty;
    }

    private static DateOnly ParseTradingDate(string raw)
    {
        return DateOnly.TryParseExact(raw, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tradingDate)
            ? tradingDate
            : DateOnly.FromDateTime(DateTime.Today);
    }

    private static DateTime CombineTradingDateAndTime(string monthDay, TimeSpan time)
    {
        var year = DateTime.Today.Year;
        if (DateOnly.TryParseExact($"{year}-{monthDay}", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date.ToDateTime(TimeOnly.FromTimeSpan(time));
        }

        return DateTime.Today.Date.Add(time);
    }

    private static string FormatBreadthLabel(int bucket)
    {
        return bucket switch
        {
            <= -11 => "跌停",
            -10 => "-10%",
            0 => "0%",
            10 => "10%",
            >= 11 => "涨停",
            _ when bucket < 0 => $"{bucket}%",
            _ => $"{bucket}%"
        };
    }

    private static string ToEastmoneySecId(string symbol)
    {
        var raw = symbol.Trim();
        if (GlobalIndexSecIds.TryGetValue(raw, out var rawGlobalSecId))
        {
            return rawGlobalSecId;
        }

        var normalized = StockSymbolNormalizer.Normalize(symbol);
        if (GlobalIndexSecIds.TryGetValue(normalized, out var globalSecId))
        {
            return globalSecId;
        }

        if (normalized.StartsWith("bj", StringComparison.OrdinalIgnoreCase))
        {
            return $"0.{normalized[2..]}";
        }

        var code = normalized.Replace("sh", string.Empty).Replace("sz", string.Empty);
        var market = normalized.StartsWith("sh", StringComparison.OrdinalIgnoreCase) ? "1" : "0";
        return $"{market}.{code}";
    }

    private static string ResolveSymbol(int market, string code)
    {
        return market switch
        {
            1 => $"sh{code}",
            0 when code.StartsWith('8') || code.StartsWith('4') => $"bj{code}",
            0 => $"sz{code}",
            2 => $"bj{code}",
            _ => code.ToLowerInvariant()
        };
    }

    private static string GetString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var node))
        {
            return string.Empty;
        }

        return node.ValueKind switch
        {
            JsonValueKind.String => node.GetString() ?? string.Empty,
            JsonValueKind.Number => node.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => string.Empty
        };
    }

    private static decimal GetDecimal(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var node))
        {
            return 0m;
        }

        return node.ValueKind switch
        {
            JsonValueKind.Number when node.TryGetDecimal(out var value) => value,
            JsonValueKind.String when decimal.TryParse(node.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) => value,
            _ => 0m
        };
    }

    private static int GetInt(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var node))
        {
            return 0;
        }

        return node.ValueKind switch
        {
            JsonValueKind.Number when node.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(node.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            _ => 0
        };
    }

    private static long GetLong(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var node))
        {
            return 0;
        }

        return node.ValueKind switch
        {
            JsonValueKind.Number when node.TryGetInt64(out var value) => value,
            JsonValueKind.String when long.TryParse(node.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            _ => 0
        };
    }

    private sealed record NorthboundSnapshotChannelDto(string Label, DateOnly Date, decimal NetInflow, decimal Balance);
}