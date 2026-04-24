using System.Globalization;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

internal static class TencentStockParser
{
    public static string ExtractPayload(string raw)
    {
        var start = raw.IndexOf('"');
        var end = raw.LastIndexOf('"');
        if (start >= 0 && end > start)
        {
            return raw.Substring(start + 1, end - start - 1);
        }

        return raw;
    }

    public static StockQuoteDto ParseQuote(string symbol, string payload)
    {
        var fields = payload.Split('~');
        var name = GetField(fields, 1, symbol);
        var price = ParseDecimal(GetField(fields, 3));
        var prevClose = ParseDecimal(GetField(fields, 4));
        if (prevClose <= 0)
        {
            prevClose = ParseDecimal(GetField(fields, 5));
        }

        var change = price - prevClose;
        var changePercent = prevClose == 0 ? 0m : Math.Round(change / prevClose * 100, 2);

        var high = ParseDecimal(GetField(fields, 33));
        var low = ParseDecimal(GetField(fields, 34));
        var turnoverRate = ParseDecimal(GetField(fields, 38));
        var peRatio = ParseDecimal(GetField(fields, 39));
        var volumeRatio = ParseDecimal(GetField(fields, 43));
        var floatMarketCapWan = ParseDecimal(GetField(fields, 45)); // 流通市值 in 万元
        var floatMarketCap = floatMarketCapWan > 0 ? floatMarketCapWan * 10000m : 0m; // convert to 元
        var speed = ParseDecimal(GetField(fields, 49));

        return new StockQuoteDto(
            symbol,
            name,
            price,
            change,
            changePercent,
            turnoverRate,
            peRatio,
            high,
            low,
            speed,
            DateTime.UtcNow,
            Array.Empty<StockNewsDto>(),
            Array.Empty<StockIndicatorDto>(),
            floatMarketCap,
            volumeRatio
        );
     }

    public static decimal ParseDecimal(string? input)
    {
        if (decimal.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }
        return 0m;
    }

    private static string GetField(string[] fields, int index, string fallback = "")
    {
        if (index >= 0 && index < fields.Length)
        {
            return fields[index];
        }
        return fallback;
    }
 }
