using System.Globalization;
using System.Text.Json;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public sealed class TencentStockCrawler : IStockCrawlerSource
{
    private static readonly IReadOnlyDictionary<string, string> QuoteSymbolAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["hsi"] = "r_hkHSI",
        ["hstech"] = "r_hkHSTECH",
        ["ndx"] = "usNDX",
        ["spx"] = "usINX"
    };
    private readonly HttpClient _httpClient;

    public TencentStockCrawler(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string SourceName => "腾讯";

    public async Task<StockQuoteDto> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = StockSymbolNormalizer.Normalize(symbol);
        var quoteSymbol = ResolveQuoteSymbol(normalized);
        var url = $"https://qt.gtimg.cn/q={quoteSymbol}";
        var raw = await _httpClient.GetStringAsync(url, cancellationToken);
        var payload = TencentStockParser.ExtractPayload(raw);
        return TencentStockParser.ParseQuote(normalized, payload);
    }

    public async Task<MarketIndexDto> GetMarketIndexAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var quote = await GetQuoteAsync(symbol, cancellationToken);
        return new MarketIndexDto(
            quote.Symbol,
            quote.Name,
            quote.Price,
            quote.Change,
            quote.ChangePercent,
            quote.Timestamp
        );
    }

    public async Task<IReadOnlyList<KLinePointDto>> GetKLineAsync(string symbol, string interval, int count, CancellationToken cancellationToken = default)
    {
        var normalized = StockSymbolNormalizer.Normalize(symbol);
        var safeInterval = NormalizeInterval(interval);
        var fetchInterval = safeInterval == "year" ? "month" : safeInterval;
        var safeCount = Math.Max(count, 60);
        var requestCount = CalculateRequestCount(safeInterval, safeCount);
        var end = DateTime.UtcNow;
        var start = end.AddDays(-CalculateLookbackDays(safeInterval, safeCount));
        var url = $"https://web.ifzq.gtimg.cn/appstock/app/fqkline/get?param={normalized},{fetchInterval},{start:yyyy-MM-dd},{end:yyyy-MM-dd},{requestCount},qfq";

        var json = await _httpClient.GetStringAsync(url, cancellationToken);
        using var document = JsonDocument.Parse(json);

        var data = document.RootElement.GetProperty("data");
        if (!data.TryGetProperty(normalized, out var symbolNode))
        {
            return Array.Empty<KLinePointDto>();
        }

        JsonElement klineNode;
        if (fetchInterval == "day")
        {
            if (!symbolNode.TryGetProperty("qfqday", out klineNode) || klineNode.ValueKind != JsonValueKind.Array)
            {
                if (!symbolNode.TryGetProperty("day", out klineNode) || klineNode.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<KLinePointDto>();
                }
            }
        }
        else if (fetchInterval == "week")
        {
            if (!symbolNode.TryGetProperty("qfqweek", out klineNode) || klineNode.ValueKind != JsonValueKind.Array)
            {
                if (!symbolNode.TryGetProperty("week", out klineNode) || klineNode.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<KLinePointDto>();
                }
            }
        }
        else if (fetchInterval == "month")
        {
            if (!symbolNode.TryGetProperty("qfqmonth", out klineNode) || klineNode.ValueKind != JsonValueKind.Array)
            {
                if (!symbolNode.TryGetProperty("month", out klineNode) || klineNode.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<KLinePointDto>();
                }
            }
        }
        else
        {
            if (!symbolNode.TryGetProperty(fetchInterval, out klineNode) || klineNode.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<KLinePointDto>();
            }
        }

        var points = new List<KLinePointDto>();
        foreach (var item in klineNode.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 6)
            {
                continue;
            }

            var date = DateTime.Parse(item[0].GetString() ?? string.Empty, CultureInfo.InvariantCulture);
            var open = TencentStockParser.ParseDecimal(item[1].GetString());
            var close = TencentStockParser.ParseDecimal(item[2].GetString());
            var high = TencentStockParser.ParseDecimal(item[3].GetString());
            var low = TencentStockParser.ParseDecimal(item[4].GetString());
            var volume = TencentStockParser.ParseDecimal(item[5].GetString());

            points.Add(new KLinePointDto(date, open, close, high, low, volume));
        }

        var trimmed = points.TakeLast(requestCount).ToArray();
        if (safeInterval == "year")
        {
            return AggregateYearly(trimmed).TakeLast(safeCount).ToArray();
        }

        return trimmed.TakeLast(safeCount).ToArray();
    }

    public async Task<IReadOnlyList<MinuteLinePointDto>> GetMinuteLineAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = StockSymbolNormalizer.Normalize(symbol);
        var url = $"https://web.ifzq.gtimg.cn/appstock/app/minute/query?code={normalized}";

        var json = await _httpClient.GetStringAsync(url, cancellationToken);
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("data", out var dataNode))
        {
            return Array.Empty<MinuteLinePointDto>();
        }

        if (!dataNode.TryGetProperty(normalized, out var symbolNode))
        {
            return Array.Empty<MinuteLinePointDto>();
        }

        if (!symbolNode.TryGetProperty("data", out var innerNode))
        {
            return Array.Empty<MinuteLinePointDto>();
        }

        if (!innerNode.TryGetProperty("data", out var listNode) || listNode.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MinuteLinePointDto>();
        }

        var dateText = innerNode.TryGetProperty("date", out var dateNode) ? dateNode.GetString() : null;
        var dataDate = DateOnly.TryParseExact(dateText, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
            ? parsedDate
            : DateOnly.FromDateTime(DateTime.Today);

        var points = new List<MinuteLinePointDto>();
        foreach (var item in listNode.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() >= 4)
            {
                var time = ParseMinuteTime(item[0].GetString() ?? "00:00");
                var price = TencentStockParser.ParseDecimal(item[1].GetString());
                var avg = TencentStockParser.ParseDecimal(item[2].GetString());
                var volume = TencentStockParser.ParseDecimal(item[3].GetString());
                points.Add(new MinuteLinePointDto(dataDate, time, price, avg, volume));
                continue;
            }

            if (item.ValueKind == JsonValueKind.String)
            {
                var raw = item.GetString() ?? string.Empty;
                var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 3)
                {
                    var time = ParseMinuteTime(parts[0]);
                    var price = TencentStockParser.ParseDecimal(parts[1]);
                    var volume = TencentStockParser.ParseDecimal(parts[2]);
                    var avg = price;
                    points.Add(new MinuteLinePointDto(dataDate, time, price, avg, volume));
                }
            }
        }

        return points;
    }

    public Task<IReadOnlyList<IntradayMessageDto>> GetIntradayMessagesAsync(string symbol, CancellationToken cancellationToken = default)
    {
        // TODO: 接入盘中消息来源
        IReadOnlyList<IntradayMessageDto> result = Array.Empty<IntradayMessageDto>();
        return Task.FromResult(result);
    }

    private static string ResolveQuoteSymbol(string symbol)
    {
        return QuoteSymbolAliases.TryGetValue(symbol, out var mapped) ? mapped : symbol;
    }

    private static string NormalizeInterval(string interval)
    {
        var value = interval?.Trim().ToLowerInvariant();
        return value switch
        {
            "week" => "week",
            "month" => "month",
            "year" => "year",
            _ => "day"
        };
    }

    private static TimeSpan ParseMinuteTime(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return TimeSpan.Zero;
        }

        var trimmed = raw.Trim();

        if (trimmed.Contains(':') && TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        if (trimmed.Length <= 4 && int.TryParse(trimmed, out var hhmm))
        {
            var hour = hhmm / 100;
            var minute = hhmm % 100;
            if (hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59)
            {
                return new TimeSpan(hour, minute, 0);
            }
        }

        if (TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out var fallback))
        {
            return fallback;
        }

        return TimeSpan.Zero;
    }

    private static IReadOnlyList<KLinePointDto> AggregateYearly(IEnumerable<KLinePointDto> points)
    {
        return points
            .GroupBy(p => p.Date.Year)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var ordered = g.OrderBy(x => x.Date).ToList();
                var open = ordered.First().Open;
                var close = ordered.Last().Close;
                var high = ordered.Max(x => x.High);
                var low = ordered.Min(x => x.Low);
                var volume = ordered.Sum(x => x.Volume);
                return new KLinePointDto(new DateTime(g.Key, 1, 1), open, close, high, low, volume);
            })
            .ToArray();
    }

    private static int CalculateLookbackDays(string interval, int count)
    {
        return interval switch
        {
            "week" => Math.Max(365, count * 12),
            "month" => Math.Max(365 * 3, count * 35),
            "year" => Math.Max(365 * 10, count * 370),
            _ => Math.Max(365, count * 2)
        };
    }

    private static int CalculateRequestCount(string interval, int count)
    {
        return interval switch
        {
            "week" => Math.Max(120, count * 2),
            "month" => Math.Max(180, count + 24),
            "year" => Math.Max(240, count * 12),
            _ => Math.Max(120, count)
        };
    }
}
