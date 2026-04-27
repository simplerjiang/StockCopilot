using System.Globalization;
using System.Text.Json;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

internal static class EastmoneyStockParser
{
    private static readonly TimeZoneInfo ChinaTimeZone = ResolveChinaTimeZone();

    public static StockQuoteDto? ParseQuote(string symbol, string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var dataNode))
        {
            return null;
        }

        var rawName = dataNode.TryGetProperty("f58", out var nameNode) ? nameNode.GetString() ?? symbol : symbol;
        var name = StockNameNormalizer.NormalizeDisplayName(rawName);
        var price = ParseScaledDecimal(dataNode, "f43");
        var prevClose = ParseScaledDecimal(dataNode, "f60");
        var percent = ParseScaledDecimal(dataNode, "f170");
        var floatMarketCap = ParseDecimal(dataNode, "f117");
        var peRatio = ParseScaledDecimal(dataNode, "f162");
        var volumeRatio = ParseScaledDecimal(dataNode, "f10");

        if (price <= 0m
            && prevClose <= 0m
            && percent == 0m
            && floatMarketCap <= 0m
            && peRatio <= 0m
            && volumeRatio <= 0m
            && string.Equals(name, symbol, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var change = price - prevClose;
        var changePercent = prevClose == 0 ? percent : Math.Round(change / prevClose * 100, 2);

        return new StockQuoteDto(symbol, name, price, change, changePercent, 0m, peRatio, 0m, 0m, 0m, DateTime.UtcNow,
            Array.Empty<StockNewsDto>(), Array.Empty<StockIndicatorDto>(), floatMarketCap, volumeRatio, null, null);
    }

    public static IReadOnlyList<MinuteLinePointDto> ParseTrends(string symbol, string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var dataNode))
        {
            return Array.Empty<MinuteLinePointDto>();
        }

        if (!dataNode.TryGetProperty("trends", out var trendsNode) || trendsNode.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MinuteLinePointDto>();
        }

        var points = new List<MinuteLinePointDto>();
        foreach (var item in trendsNode.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var raw = item.GetString() ?? string.Empty;
            var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 6)
            {
                continue;
            }

            if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dateTime))
            {
                continue;
            }

            var price = ParseDecimal(parts[1]);
            var avg = ParseDecimal(parts[2]);
            var volume = ParseDecimal(parts[5]);
            points.Add(new MinuteLinePointDto(DateOnly.FromDateTime(dateTime), dateTime.TimeOfDay, price, avg, volume));
        }

        return points;
    }

    public static IReadOnlyList<IntradayMessageDto> ParseIntradayMessages(string symbol, string json, DateTimeOffset fetchedAtUtc, int take = 20)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var dataNode))
        {
            return Array.Empty<IntradayMessageDto>();
        }

        if (!dataNode.TryGetProperty("details", out var detailsNode) || detailsNode.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<IntradayMessageDto>();
        }

        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fetchedAtUtc, ChinaTimeZone).DateTime);
        var messages = new List<IntradayMessageDto>();
        foreach (var item in detailsNode.EnumerateArray().Reverse())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var raw = item.GetString() ?? string.Empty;
            var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 5)
            {
                continue;
            }

            if (!TimeOnly.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                continue;
            }

            var price = ParseDecimal(parts[1]);
            var volume = ParseDecimal(parts[2]);
            var side = parts[4] switch
            {
                "1" => "卖",
                "2" => "买",
                _ => "中性"
            };

            var localDateTime = localDate.ToDateTime(time, DateTimeKind.Unspecified);
            var publishedAt = new DateTimeOffset(localDateTime, ChinaTimeZone.GetUtcOffset(localDateTime)).UtcDateTime;
            messages.Add(new IntradayMessageDto(
                $"{parts[0]} {side}盘 {volume:0.##}手 @ {price:0.00}",
                "东方财富逐笔",
                publishedAt,
                null));

            if (messages.Count >= take)
            {
                break;
            }
        }

        return messages;
    }

    private static decimal ParseScaledDecimal(JsonElement dataNode, string field)
    {
        if (!dataNode.TryGetProperty(field, out var node))
        {
            return 0m;
        }

        if (node.ValueKind == JsonValueKind.Number && node.TryGetDecimal(out var value))
        {
            return value / 100m;
        }

        if (node.ValueKind == JsonValueKind.String && decimal.TryParse(node.GetString(), out var textValue))
        {
            return textValue / 100m;
        }

        return 0m;
    }

    private static decimal ParseDecimal(JsonElement dataNode, string field)
    {
        if (!dataNode.TryGetProperty(field, out var node))
        {
            return 0m;
        }

        if (node.ValueKind == JsonValueKind.Number && node.TryGetDecimal(out var value))
        {
            return value;
        }

        if (node.ValueKind == JsonValueKind.String && decimal.TryParse(node.GetString(), out var textValue))
        {
            return textValue;
        }

        return 0m;
    }

    private static decimal ParseDecimal(string? input)
    {
        if (decimal.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }
        return 0m;
    }

    private static TimeZoneInfo ResolveChinaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("China Standard Time", TimeSpan.FromHours(8), "China Standard Time", "China Standard Time");
        }
    }
}
