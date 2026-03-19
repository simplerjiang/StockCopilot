using Microsoft.Extensions.Caching.Memory;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public sealed class StockDataService : IStockDataService
{
    private const string TencentSourceName = "腾讯";
    private const string EastmoneySourceName = "东方财富";
    private readonly IMemoryCache _cache;
    private readonly IStockCrawler _defaultCrawler;
    private readonly ILocalFactIngestionService _localFactIngestionService;
    private readonly IQueryLocalFactDatabaseTool _queryLocalFactDatabaseTool;
    private readonly IReadOnlyList<IStockCrawlerSource> _sources;
    private readonly IReadOnlyDictionary<string, IStockCrawlerSource> _sourcesByName;

    public StockDataService(
        IMemoryCache cache,
        IStockCrawler defaultCrawler,
        ILocalFactIngestionService localFactIngestionService,
        IQueryLocalFactDatabaseTool queryLocalFactDatabaseTool,
        IEnumerable<IStockCrawlerSource> sources)
    {
        _cache = cache;
        _defaultCrawler = defaultCrawler;
        _localFactIngestionService = localFactIngestionService;
        _queryLocalFactDatabaseTool = queryLocalFactDatabaseTool;
        _sources = sources.ToArray();
        _sourcesByName = _sources
            .GroupBy(item => item.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<StockQuoteDto> GetQuoteAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
    {
        var crawler = ResolveSource(source);
        var cacheKey = $"quote:{crawler.SourceName}:{symbol}";
        var result = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5);
            return await crawler.GetQuoteAsync(symbol, cancellationToken);
        });

        return result ?? new StockQuoteDto(symbol, symbol, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, DateTime.UtcNow, Array.Empty<StockNewsDto>(), Array.Empty<StockIndicatorDto>());
    }

    public async Task<MarketIndexDto> GetMarketIndexAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
    {
        var crawler = ResolveSource(source);
        var cacheKey = $"market:{crawler.SourceName}:{symbol}";
        var result = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return await crawler.GetMarketIndexAsync(symbol, cancellationToken);
        });

        return result ?? new MarketIndexDto(symbol, symbol, 0m, 0m, 0m, DateTime.UtcNow);
    }

    public async Task<IReadOnlyList<KLinePointDto>> GetKLineAsync(string symbol, string interval, int count, string? source = null, CancellationToken cancellationToken = default)
    {
        foreach (var crawler in ResolveKLineSources(source))
        {
            try
            {
                var cacheKey = $"kline:{crawler.SourceName}:{symbol}:{interval}:{count}";
                var result = await _cache.GetOrCreateAsync(cacheKey, async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                    return await crawler.GetKLineAsync(symbol, interval, count, cancellationToken);
                });

                if (result is { Count: > 0 })
                {
                    return result;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // 自动回退到下一来源
            }
        }

        return Array.Empty<KLinePointDto>();
    }

    public async Task<IReadOnlyList<MinuteLinePointDto>> GetMinuteLineAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
    {
        foreach (var crawler in ResolveMinuteSources(source))
        {
            try
            {
                var cacheKey = $"minute:{crawler.SourceName}:{symbol}";
                var result = await _cache.GetOrCreateAsync(cacheKey, async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                    return await crawler.GetMinuteLineAsync(symbol, cancellationToken);
                });

                if (result is { Count: > 0 })
                {
                    return result;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // 自动回退到下一来源
            }
        }

        return Array.Empty<MinuteLinePointDto>();
    }

    public async Task<IReadOnlyList<IntradayMessageDto>> GetIntradayMessagesAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
    {
        await _localFactIngestionService.EnsureFreshAsync(symbol, cancellationToken);

        var localBucket = await _queryLocalFactDatabaseTool.QueryLevelAsync(symbol, "stock", cancellationToken);
        if (localBucket.Items.Count > 0)
        {
            return localBucket.Items
                .Select(item => new IntradayMessageDto(item.Title, item.Source, item.PublishTime, item.Url))
                .ToArray();
        }

        var crawler = ResolveSource(source);
        var cacheKey = $"messages:{crawler.SourceName}:{symbol}";
        var result = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            return await crawler.GetIntradayMessagesAsync(symbol, cancellationToken);
        });

        return result ?? Array.Empty<IntradayMessageDto>();
    }

    private IStockCrawler ResolveSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return _defaultCrawler;
        }

        var match = _sources.FirstOrDefault(s => s.SourceName.Equals(source, StringComparison.OrdinalIgnoreCase));
        return match ?? _defaultCrawler;
    }

    private IReadOnlyList<IStockCrawler> ResolveKLineSources(string? source)
    {
        return ResolvePreferredSources(source, EastmoneySourceName, TencentSourceName);
    }

    private IReadOnlyList<IStockCrawler> ResolveMinuteSources(string? source)
    {
        return ResolvePreferredSources(source, EastmoneySourceName, TencentSourceName);
    }

    private IReadOnlyList<IStockCrawler> ResolvePreferredSources(string? source, params string[] preferredSourceNames)
    {
        if (!string.IsNullOrWhiteSpace(source))
        {
            return new[] { ResolveSource(source) };
        }

        var result = new List<IStockCrawler>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var preferredName in preferredSourceNames)
        {
            if (_sourcesByName.TryGetValue(preferredName, out var crawler) && seen.Add(crawler.SourceName))
            {
                result.Add(crawler);
            }
        }

        foreach (var crawler in _sources)
        {
            if (seen.Add(crawler.SourceName))
            {
                result.Add(crawler);
            }
        }

        if (seen.Add(_defaultCrawler.SourceName))
        {
            result.Add(_defaultCrawler);
        }

        return result;
    }
}
