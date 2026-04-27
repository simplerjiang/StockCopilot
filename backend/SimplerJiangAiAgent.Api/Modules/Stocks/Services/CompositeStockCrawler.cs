using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public sealed class CompositeStockCrawler : IStockCrawler
{
    private const string EastmoneySourceName = "东方财富";
    private const string TencentSourceName = "腾讯";
    private readonly IReadOnlyList<IStockCrawlerSource> _crawlers;

    public CompositeStockCrawler(IEnumerable<IStockCrawlerSource> crawlers)
    {
        _crawlers = OrderCrawlers(crawlers);
    }

    public string SourceName => "聚合";

    public async Task<StockQuoteDto?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var quotes = new List<(string SourceName, StockQuoteDto Quote)>();
        var preferredQuote = await TryGetQuoteAsync(symbol, EastmoneySourceName, cancellationToken);
        if (preferredQuote is { } fastQuote && HasCompletePreferredQuote(fastQuote.Quote))
        {
            var cleanName = StockNameNormalizer.NormalizeDisplayName(fastQuote.Quote.Name);
            return fastQuote.Quote with
            {
                Name = cleanName,
            };
        }

        if (preferredQuote is { } partialQuote && IsUsableQuote(partialQuote.Quote))
        {
            quotes.Add(partialQuote);
        }

        foreach (var crawler in _crawlers)
        {
            if (preferredQuote is { } existing && string.Equals(existing.SourceName, crawler.SourceName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var quote = await crawler.GetQuoteAsync(symbol, cancellationToken);
                if (quote is not null && IsUsableQuote(quote))
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
            ?? quotes.Select(item => item.Quote).FirstOrDefault();

        if (baseQuote is null)
        {
            return null;
        }

        var mergedQuote = quotes.Select(item => item.Quote).Aggregate(baseQuote, MergeQuote);

        // B34: Normalize special ST-prefix spacing without touching ordinary company spaces.
        if (!string.IsNullOrWhiteSpace(mergedQuote.Name))
        {
            mergedQuote = mergedQuote with { Name = StockNameNormalizer.NormalizeDisplayName(mergedQuote.Name) };
        }

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

        return mergedQuote;
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

    private async Task<(string SourceName, StockQuoteDto Quote)?> TryGetQuoteAsync(string symbol, string sourceName, CancellationToken cancellationToken)
    {
        var crawler = _crawlers.FirstOrDefault(item => string.Equals(item.SourceName, sourceName, StringComparison.OrdinalIgnoreCase));
        if (crawler is null)
        {
            return null;
        }

        try
        {
            var quote = await crawler.GetQuoteAsync(symbol, cancellationToken);
            if (quote is null)
            {
                return null;
            }

            return (crawler.SourceName, quote);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<IStockCrawlerSource> OrderCrawlers(IEnumerable<IStockCrawlerSource> crawlers)
    {
        return crawlers
            .OrderByDescending(crawler => string.Equals(crawler.SourceName, EastmoneySourceName, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(crawler => string.Equals(crawler.SourceName, TencentSourceName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
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
            || HasUsableName(quote);
    }

    private static bool HasUsableName(StockQuoteDto quote)
    {
        return !string.IsNullOrWhiteSpace(quote.Name)
            && !quote.Name.Contains("示例", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(quote.Name.Trim(), quote.Symbol.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasFundamentalData(StockQuoteDto quote)
    {
        return quote.FloatMarketCap > 0m
            || quote.VolumeRatio > 0m
            || quote.ShareholderCount.HasValue
            || !string.IsNullOrWhiteSpace(quote.SectorName)
            || quote.PeRatio > 0m;
    }

    private static bool HasCompletePreferredQuote(StockQuoteDto quote)
    {
        return IsUsableQuote(quote)
            && HasFundamentalData(quote)
            && quote.High > 0m
            && quote.Low > 0m;
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
