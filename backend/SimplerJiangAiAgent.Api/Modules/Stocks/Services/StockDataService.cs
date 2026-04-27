using Microsoft.Extensions.Caching.Memory;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public sealed class StockDataService : IStockDataService
{
    private const string TencentSourceName = "腾讯";
    private const string EastmoneySourceName = "东方财富";
    private static readonly TimeSpan RequestPathLocalFactRefreshTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RequestPathCrawlerMessagesTimeout = TimeSpan.FromSeconds(3);
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

    public async Task<StockQuoteDto?> GetQuoteAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
    {
        var crawler = ResolveSource(source);
        var cacheKey = $"quote:{crawler.SourceName}:{symbol}";
        var result = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5);
            return await crawler.GetQuoteAsync(symbol, cancellationToken);
        });

        return result;
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
                    // 过滤掉 High==0 && Low==0 的无效 K 线条目
                    var filtered = result.Where(k => !(k.High == 0m && k.Low == 0m)).ToList();
                    if (filtered.Count > 0)
                    {
                        return filtered;
                    }
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
        var result = await GetIntradayMessagesResultAsync(symbol, source, cancellationToken);
        return result.Messages;
    }

    public async Task<IntradayMessagesResultDto> GetIntradayMessagesResultAsync(string symbol, string? source = null, CancellationToken cancellationToken = default)
    {
        var normalized = StockSymbolNormalizer.Normalize(symbol);
        var warnings = new List<string>();

        if (StockSymbolNormalizer.IsIndex(normalized))
        {
            warnings.Add("指数代码已跳过普通股票消息刷新和 AI 清洗链路。");
            return BuildIntradayMessagesResult(Array.Empty<IntradayMessageDto>(), warnings);
        }

        var localMessages = await TryQueryLocalMessagesAsync(normalized, warnings, cancellationToken);
        if (localMessages.Count > 0)
        {
            return BuildIntradayMessagesResult(localMessages, warnings);
        }

        await TryRefreshLocalFactsForMessagesAsync(normalized, warnings, cancellationToken);

        localMessages = await TryQueryLocalMessagesAsync(normalized, warnings, cancellationToken);
        if (localMessages.Count > 0)
        {
            return BuildIntradayMessagesResult(localMessages, warnings);
        }

        var crawlerMessages = await TryGetCrawlerMessagesAsync(normalized, source, warnings, cancellationToken);
        return BuildIntradayMessagesResult(crawlerMessages, warnings);
    }

    private async Task<IReadOnlyList<IntradayMessageDto>> TryQueryLocalMessagesAsync(
        string symbol,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var localBucket = await _queryLocalFactDatabaseTool.QueryLevelAsync(symbol, "stock", cancellationToken);
            if (localBucket.Items.Count > 0)
            {
                return localBucket.Items
                    .Select(item => new IntradayMessageDto(item.Title, item.Source, item.PublishTime, item.Url))
                    .ToArray();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            warnings.Add("本地消息缓存读取失败，已降级到实时消息源。");
        }

        return Array.Empty<IntradayMessageDto>();
    }

    private async Task TryRefreshLocalFactsForMessagesAsync(
        string symbol,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RequestPathLocalFactRefreshTimeout);

        try
        {
            await _localFactIngestionService.EnsureFreshAsync(symbol, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            warnings.Add("本地消息刷新或 AI 清洗超时，已返回缓存/实时可用消息。");
        }
        catch
        {
            warnings.Add("本地消息刷新或 AI 清洗失败，已返回缓存/实时可用消息。");
        }
    }

    private async Task<IReadOnlyList<IntradayMessageDto>> TryGetCrawlerMessagesAsync(
        string symbol,
        string? source,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        var crawler = ResolveSource(source);
        var cacheKey = $"messages:{crawler.SourceName}:{symbol}";
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RequestPathCrawlerMessagesTimeout);

        try
        {
            var result = await _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
                return await crawler.GetIntradayMessagesAsync(symbol, timeoutCts.Token);
            });

            return result ?? Array.Empty<IntradayMessageDto>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            warnings.Add("实时消息源请求超时，已返回空消息列表。");
        }
        catch
        {
            warnings.Add("实时消息源请求失败，已返回空消息列表。");
        }

        return Array.Empty<IntradayMessageDto>();
    }

    private static IntradayMessagesResultDto BuildIntradayMessagesResult(
        IReadOnlyList<IntradayMessageDto> messages,
        IReadOnlyCollection<string> warnings)
    {
        var warning = warnings.Count == 0
            ? null
            : string.Join("；", warnings.Distinct(StringComparer.Ordinal));

        return new IntradayMessagesResultDto(messages, warning is not null, warning);
    }

    private IStockCrawler ResolveSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return _defaultCrawler;
        }

        var sourceName = source.Trim();
        if (_sourcesByName.TryGetValue(sourceName, out var match))
        {
            return match;
        }

        if (string.Equals(sourceName, _defaultCrawler.SourceName, StringComparison.OrdinalIgnoreCase))
        {
            return _defaultCrawler;
        }

        throw new UnsupportedStockSourceException(sourceName);
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
