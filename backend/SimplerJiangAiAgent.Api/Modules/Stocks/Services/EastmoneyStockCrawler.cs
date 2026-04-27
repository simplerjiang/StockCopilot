using System.Globalization;
using System.Text.Json;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public sealed class EastmoneyStockCrawler : IStockCrawlerSource
{
    private readonly HttpClient _httpClient;

    public EastmoneyStockCrawler(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string SourceName => "东方财富";

    public async Task<StockQuoteDto?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = StockSymbolNormalizer.Normalize(symbol);
        var secId = ToEastmoneySecId(normalized);
        var marketPrefix = normalized.StartsWith("sh", StringComparison.OrdinalIgnoreCase) ? "SH"
            : normalized.StartsWith("bj", StringComparison.OrdinalIgnoreCase) ? "BJ"
            : "SZ";
        var code = normalized[2..];
        var quoteUrl = $"https://push2.eastmoney.com/api/qt/stock/get?secid={secId}&fields=f58,f43,f60,f170,f10,f117,f162";
        var surveyUrl = $"https://emweb.securities.eastmoney.com/PC_HSF10/CompanySurvey/CompanySurveyAjax?code={marketPrefix}{code}";
        var shareholderUrl = $"https://emweb.securities.eastmoney.com/PC_HSF10/ShareholderResearch/PageAjax?code={marketPrefix}{code}";

        var quoteTask = _httpClient.GetStringAsync(quoteUrl, cancellationToken);
        var surveyTask = TryGetStringAsync(surveyUrl, cancellationToken);
        var shareholderTask = TryGetStringAsync(shareholderUrl, cancellationToken);

        await Task.WhenAll(new Task[] { quoteTask, surveyTask, shareholderTask });

        var quote = EastmoneyStockParser.ParseQuote(normalized, await quoteTask);
        if (quote is null)
        {
            return null;
        }

        var surveyJson = await surveyTask;
        var shareholderJson = await shareholderTask;
        var profile = surveyJson is null
            ? new EastmoneyCompanyProfileDto(quote.Symbol, quote.Name, null, null, Array.Empty<StockFundamentalFactDto>())
            : EastmoneyCompanyProfileParser.Parse(normalized, surveyJson, shareholderJson);

        return quote with
        {
            Name = string.IsNullOrWhiteSpace(quote.Name) || string.Equals(quote.Name, quote.Symbol, StringComparison.OrdinalIgnoreCase)
                ? profile.Name
                : quote.Name,
            ShareholderCount = quote.ShareholderCount ?? profile.ShareholderCount,
            SectorName = quote.SectorName ?? profile.SectorName
        };
    }

    public Task<MarketIndexDto> GetMarketIndexAsync(string symbol, CancellationToken cancellationToken = default)
    {
        return GetQuoteAsync(symbol, cancellationToken)
            .ContinueWith(task =>
            {
                var quote = task.Result;
                if (quote is null)
                {
                    throw new InvalidOperationException($"东方财富行情不可用: {symbol}");
                }

                return new MarketIndexDto(quote.Symbol, quote.Name, quote.Price, quote.Change, quote.ChangePercent, quote.Timestamp);
            }, cancellationToken);
    }

    public Task<IReadOnlyList<KLinePointDto>> GetKLineAsync(string symbol, string interval, int count, CancellationToken cancellationToken = default)
    {
        return GetKLineInternalAsync(symbol, interval, count, cancellationToken);
    }

    public async Task<IReadOnlyList<MinuteLinePointDto>> GetMinuteLineAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var secId = ToEastmoneySecId(symbol);
        var url = $"https://push2.eastmoney.com/api/qt/stock/trends2/get?secid={secId}&fields1=f1,f2,f3&fields2=f51,f52,f53,f54,f55,f56,f57,f58&ndays=1";
        var json = await _httpClient.GetStringAsync(url, cancellationToken);
        return EastmoneyStockParser.ParseTrends(symbol, json);
    }

    public async Task<IReadOnlyList<IntradayMessageDto>> GetIntradayMessagesAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var secId = ToEastmoneySecId(symbol);
        var url = $"https://push2.eastmoney.com/api/qt/stock/details/get?secid={secId}&fields1=f1,f2,f3,f4&fields2=f51,f52,f53,f54,f55";
        var json = await _httpClient.GetStringAsync(url, cancellationToken);
        return EastmoneyStockParser.ParseIntradayMessages(symbol, json, DateTimeOffset.UtcNow);
    }

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

    private static string ToEastmoneySecId(string symbol)
    {
        var raw = symbol.Trim();
        if (GlobalIndexSecIds.TryGetValue(raw, out var rawSecId))
            return rawSecId;

        var normalized = StockSymbolNormalizer.Normalize(symbol);
        if (GlobalIndexSecIds.TryGetValue(normalized, out var globalSecId))
            return globalSecId;

        var code = normalized.Replace("sh", string.Empty).Replace("sz", string.Empty).Replace("bj", string.Empty);
        var market = normalized.StartsWith("sh") ? "1" : "0";
        return $"{market}.{code}";
    }

    private async Task<IReadOnlyList<KLinePointDto>> GetKLineInternalAsync(string symbol, string interval, int count, CancellationToken cancellationToken)
    {
        var normalized = StockSymbolNormalizer.Normalize(symbol);
        var safeInterval = NormalizeInterval(interval);
        var fetchInterval = safeInterval == "year" ? "month" : safeInterval;
        var safeCount = Math.Max(count, 60);
        var end = DateTime.UtcNow.Date;
        var start = end.AddDays(-CalculateLookbackDays(safeInterval, safeCount));
        var klt = fetchInterval switch
        {
            "week" => 102,
            "month" => 103,
            _ => 101
        };

        var url = $"https://push2his.eastmoney.com/api/qt/stock/kline/get?secid={ToEastmoneySecId(normalized)}&fields1=f1,f2,f3,f4,f5,f6&fields2=f51,f52,f53,f54,f55,f56,f57,f58,f59,f60,f61&klt={klt}&fqt=1&beg={start:yyyyMMdd}&end={end:yyyyMMdd}";
        var json = await _httpClient.GetStringAsync(url, cancellationToken);
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("data", out var dataNode))
        {
            return Array.Empty<KLinePointDto>();
        }

        if (!dataNode.TryGetProperty("klines", out var klineNode) || klineNode.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<KLinePointDto>();
        }

        var points = new List<KLinePointDto>();
        foreach (var item in klineNode.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var point = ParseKLinePoint(item.GetString());
            if (point is not null)
            {
                points.Add(point);
            }
        }

        var trimmed = points.TakeLast(safeCount).ToArray();
        if (safeInterval == "year")
        {
            return AggregateYearly(trimmed).TakeLast(count).ToArray();
        }

        return trimmed.TakeLast(count).ToArray();
    }

    private static KLinePointDto? ParseKLinePoint(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var parts = raw.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 6)
        {
            return null;
        }

        if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return null;
        }

        var open = ParseDecimal(parts[1]);
        var close = ParseDecimal(parts[2]);
        var high = ParseDecimal(parts[3]);
        var low = ParseDecimal(parts[4]);
        var volume = ParseDecimal(parts[5]);
        return new KLinePointDto(date, open, close, high, low, volume);
    }

    private static decimal ParseDecimal(string? input)
    {
        return decimal.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;
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

    private static IReadOnlyList<KLinePointDto> AggregateYearly(IEnumerable<KLinePointDto> points)
    {
        return points
            .GroupBy(item => item.Date.Year)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var ordered = group.OrderBy(item => item.Date).ToList();
                return new KLinePointDto(
                    new DateTime(group.Key, 1, 1),
                    ordered.First().Open,
                    ordered.Last().Close,
                    ordered.Max(item => item.High),
                    ordered.Min(item => item.Low),
                    ordered.Sum(item => item.Volume));
            })
            .ToArray();
    }

    private async Task<string?> TryGetStringAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.GetStringAsync(url, cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
