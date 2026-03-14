using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public sealed class CompositeStockCrawler : IStockCrawler
{
    private const string EastmoneySourceName = "东方财富";
    private readonly IEnumerable<IStockCrawlerSource> _crawlers;

    public CompositeStockCrawler(IEnumerable<IStockCrawlerSource> crawlers)
    {
        _crawlers = crawlers.ToArray();
    }

    public string SourceName => "聚合";

    public async Task<StockQuoteDto> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var quotes = new List<(string SourceName, StockQuoteDto Quote)>();
        foreach (var crawler in _crawlers)
        {
            try
            {
                var quote = await crawler.GetQuoteAsync(symbol, cancellationToken);
                if (IsUsableQuote(quote))
                {
                    quotes.Add((crawler.SourceName, quote));
                }
            }
            catch
            {
                // 忽略单一来源错误
            }
        }

        var baseQuote = quotes
            .OrderByDescending(item => GetRealtimeScore(item.Quote))
            .Select(item => item.Quote)
            .FirstOrDefault(item => item.Price > 0m)
            ?? quotes.Select(item => item.Quote).FirstOrDefault()
            ?? new StockQuoteDto(
                symbol,
                $"{symbol} 示例名称",
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                DateTime.UtcNow,
                Array.Empty<StockNewsDto>(),
                Array.Empty<StockIndicatorDto>()
            );

        var mergedQuote = quotes.Select(item => item.Quote).Aggregate(baseQuote, MergeQuote);
        var preferredFundamentals = quotes
            .Where(item => HasFundamentalData(item.Quote))
            .OrderByDescending(item => string.Equals(item.SourceName, EastmoneySourceName, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(item => GetFundamentalsScore(item.Quote))
            .Select(item => item.Quote)
            .FirstOrDefault();

        if (preferredFundamentals is not null)
        {
            mergedQuote = MergeFundamentals(mergedQuote, preferredFundamentals);
        }

        return mergedQuote with
        {
            News = BuildPlaceholderNews(mergedQuote.Symbol),
            Indicators = BuildPlaceholderIndicators()
        };
    }

    public async Task<MarketIndexDto> GetMarketIndexAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var result = await TryGetAsync(c => c.GetMarketIndexAsync(symbol, cancellationToken));
        return result ?? new MarketIndexDto(symbol, symbol, 0m, 0m, 0m, DateTime.UtcNow);
    }

    public async Task<IReadOnlyList<KLinePointDto>> GetKLineAsync(string symbol, string interval, int count, CancellationToken cancellationToken = default)
    {
        var result = await TryGetAsync(c => c.GetKLineAsync(symbol, interval, count, cancellationToken));
        return result ?? Array.Empty<KLinePointDto>();
    }

    public async Task<IReadOnlyList<MinuteLinePointDto>> GetMinuteLineAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var result = await TryGetAsync(c => c.GetMinuteLineAsync(symbol, cancellationToken));
        return result ?? Array.Empty<MinuteLinePointDto>();
    }

    public async Task<IReadOnlyList<IntradayMessageDto>> GetIntradayMessagesAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var all = new List<IntradayMessageDto>();
        foreach (var crawler in _crawlers)
        {
            try
            {
                var result = await crawler.GetIntradayMessagesAsync(symbol, cancellationToken);
                if (result.Count > 0)
                {
                    all.AddRange(result);
                }
            }
            catch
            {
                // 忽略单一来源错误
            }
        }

        return all
            .OrderByDescending(x => x.PublishedAt)
            .Take(30)
            .ToArray();
    }

    private async Task<T?> TryGetAsync<T>(Func<IStockCrawlerSource, Task<T>> action) where T : class
    {
        foreach (var crawler in _crawlers)
        {
            try
            {
                var result = await action(crawler);
                if (result is not null)
                {
                    return result;
                }
            }
            catch
            {
                // 忽略单一来源错误
            }
        }

        return null;
    }

    private static IReadOnlyList<StockNewsDto> BuildPlaceholderNews(string symbol)
    {
        return new List<StockNewsDto>
        {
            new(
                $"{symbol} 新闻示例标题",
                "https://example.com",
                "示例来源",
                DateTime.UtcNow.AddHours(-2)
            )
        };
    }

    private static IReadOnlyList<StockIndicatorDto> BuildPlaceholderIndicators()
    {
        return new List<StockIndicatorDto>
        {
            new("MA5", 0m, null),
            new("MA10", 0m, null),
            new("RSI", 0m, null)
        };
    }

    private static bool IsUsableQuote(StockQuoteDto quote)
    {
        return quote.Price > 0m
            || quote.Change != 0m
            || quote.ChangePercent != 0m
            || quote.TurnoverRate != 0m
            || quote.PeRatio != 0m
            || quote.High != 0m
            || quote.Low != 0m
            || quote.Speed != 0m
            || quote.FloatMarketCap != 0m
            || quote.VolumeRatio != 0m
            || quote.ShareholderCount.HasValue
            || !string.IsNullOrWhiteSpace(quote.SectorName)
            || (!string.IsNullOrWhiteSpace(quote.Name) && !quote.Name.Contains("示例", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasFundamentalData(StockQuoteDto quote)
    {
        return quote.FloatMarketCap > 0m
            || quote.VolumeRatio > 0m
            || quote.ShareholderCount.HasValue
            || !string.IsNullOrWhiteSpace(quote.SectorName)
            || quote.PeRatio > 0m;
    }

    private static int GetRealtimeScore(StockQuoteDto quote)
    {
        var score = 0;
        if (quote.Price > 0m)
        {
            score += 4;
        }

        if (quote.High > 0m || quote.Low > 0m)
        {
            score += 2;
        }

        if (quote.Change != 0m || quote.ChangePercent != 0m)
        {
            score += 1;
        }

        if (quote.TurnoverRate > 0m || quote.Speed > 0m)
        {
            score += 1;
        }

        return score;
    }

    private static int GetFundamentalsScore(StockQuoteDto quote)
    {
        var score = 0;
        if (quote.FloatMarketCap > 0m)
        {
            score += 3;
        }

        if (quote.PeRatio > 0m)
        {
            score += 2;
        }

        if (quote.VolumeRatio > 0m)
        {
            score += 1;
        }

        if (quote.ShareholderCount.HasValue)
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(quote.SectorName))
        {
            score += 2;
        }

        return score;
    }

    private static StockQuoteDto MergeQuote(StockQuoteDto current, StockQuoteDto candidate)
    {
        return current with
        {
            Name = PreferText(current.Name, candidate.Name, current.Symbol) ?? current.Name,
            Price = PreferDecimal(current.Price, candidate.Price),
            Change = PreferDecimal(current.Change, candidate.Change),
            ChangePercent = PreferDecimal(current.ChangePercent, candidate.ChangePercent),
            TurnoverRate = PreferDecimal(current.TurnoverRate, candidate.TurnoverRate),
            PeRatio = PreferDecimal(current.PeRatio, candidate.PeRatio),
            High = PreferDecimal(current.High, candidate.High),
            Low = PreferDecimal(current.Low, candidate.Low),
            Speed = PreferDecimal(current.Speed, candidate.Speed),
            Timestamp = current.Timestamp >= candidate.Timestamp ? current.Timestamp : candidate.Timestamp,
            FloatMarketCap = PreferDecimal(current.FloatMarketCap, candidate.FloatMarketCap),
            VolumeRatio = PreferDecimal(current.VolumeRatio, candidate.VolumeRatio),
            ShareholderCount = current.ShareholderCount ?? candidate.ShareholderCount,
            SectorName = PreferText(current.SectorName, candidate.SectorName, current.Symbol)
        };
    }

    private static StockQuoteDto MergeFundamentals(StockQuoteDto current, StockQuoteDto preferred)
    {
        return current with
        {
            FloatMarketCap = preferred.FloatMarketCap > 0m ? preferred.FloatMarketCap : current.FloatMarketCap,
            PeRatio = preferred.PeRatio > 0m ? preferred.PeRatio : current.PeRatio,
            VolumeRatio = preferred.VolumeRatio > 0m ? preferred.VolumeRatio : current.VolumeRatio,
            ShareholderCount = preferred.ShareholderCount ?? current.ShareholderCount,
            SectorName = !string.IsNullOrWhiteSpace(preferred.SectorName) ? preferred.SectorName : current.SectorName
        };
    }

    private static decimal PreferDecimal(decimal current, decimal candidate)
    {
        return current != 0m ? current : candidate;
    }

    private static string? PreferText(string? current, string? candidate, string symbol)
    {
        if (!string.IsNullOrWhiteSpace(current)
            && !string.Equals(current, symbol, StringComparison.OrdinalIgnoreCase)
            && !current.Contains("示例", StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        return string.IsNullOrWhiteSpace(candidate) ? current : candidate;
    }
}
