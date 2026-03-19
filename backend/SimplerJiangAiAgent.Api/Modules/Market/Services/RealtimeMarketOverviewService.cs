using Microsoft.Extensions.Caching.Memory;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace SimplerJiangAiAgent.Api.Modules.Market.Services;

public sealed class RealtimeMarketOverviewService : IRealtimeMarketOverviewService
{
    private static readonly string[] DefaultIndexSymbols = ["sh000001", "sz399001", "sz399006"];
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CacheGates = new(StringComparer.Ordinal);
    private static readonly TimeSpan BatchFreshTtl = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BatchStaleTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FlowFreshTtl = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan FlowStaleTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan QuoteTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan OverviewTimeout = TimeSpan.FromSeconds(5);
    private readonly IMemoryCache _cache;
    private readonly IEastmoneyRealtimeMarketClient _client;
    private readonly TimeProvider _timeProvider;

    public RealtimeMarketOverviewService(IMemoryCache cache, IEastmoneyRealtimeMarketClient client, TimeProvider? timeProvider = null)
    {
        _cache = cache;
        _client = client;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<BatchStockQuoteDto>> GetBatchQuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken = default)
    {
        var normalizedSymbols = NormalizeSymbols(symbols, fallbackToDefault: false);
        if (normalizedSymbols.Count == 0)
        {
            return Array.Empty<BatchStockQuoteDto>();
        }

        var cacheKey = $"market:realtime:batch:{string.Join(',', normalizedSymbols)}";
        return await GetCachedAsync(
            cacheKey,
            BatchFreshTtl,
            BatchStaleTtl,
            QuoteTimeout,
            ct => _client.GetBatchQuotesAsync(normalizedSymbols, ct),
            Array.Empty<BatchStockQuoteDto>(),
            cancellationToken);
    }

    public async Task<MarketRealtimeOverviewDto> GetOverviewAsync(IReadOnlyList<string>? indexSymbols = null, CancellationToken cancellationToken = default)
    {
        var safeSymbols = NormalizeSymbols(indexSymbols, fallbackToDefault: true);
        var quotesTask = TryGetBatchQuotesAsync(safeSymbols, cancellationToken);
        var mainFlowTask = GetCachedAsync<MarketCapitalFlowSnapshotDto?>(
            "market:realtime:main-flow",
            FlowFreshTtl,
            FlowStaleTtl,
            OverviewTimeout,
            ct => _client.GetMainCapitalFlowAsync(ct),
            null,
            cancellationToken);
        var northboundTask = GetCachedAsync<NorthboundFlowSnapshotDto?>(
            "market:realtime:northbound",
            FlowFreshTtl,
            FlowStaleTtl,
            OverviewTimeout,
            ct => _client.GetNorthboundFlowAsync(ct),
            null,
            cancellationToken);
        var breadthTask = GetCachedAsync<MarketBreadthDistributionDto?>(
            "market:realtime:breadth",
            FlowFreshTtl,
            FlowStaleTtl,
            OverviewTimeout,
            ct => _client.GetBreadthDistributionAsync(ct),
            null,
            cancellationToken);

        await Task.WhenAll(quotesTask, mainFlowTask, northboundTask, breadthTask);

        var quotes = quotesTask.Result;
        var mainFlow = mainFlowTask.Result;
        var northbound = northboundTask.Result;
        var breadth = breadthTask.Result;
        var snapshotTime = ResolveSnapshotTime(quotes, mainFlow, northbound);

        return new MarketRealtimeOverviewDto(snapshotTime, quotes, mainFlow, northbound, breadth);
    }

    private async Task<IReadOnlyList<BatchStockQuoteDto>> TryGetBatchQuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken)
    {
        try
        {
            return await GetBatchQuotesAsync(symbols, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Array.Empty<BatchStockQuoteDto>();
        }
    }

    private async Task<T> GetCachedAsync<T>(
        string cacheKey,
        TimeSpan freshTtl,
        TimeSpan staleTtl,
        TimeSpan requestTimeout,
        Func<CancellationToken, Task<T>> factory,
        T fallback,
        CancellationToken cancellationToken)
    {
        var cached = _cache.Get<CachedRealtimeValue<T>>(cacheKey);
        if (IsFresh(cached, freshTtl))
        {
            return cached!.Value;
        }

        var gate = CacheGates.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            cached = _cache.Get<CachedRealtimeValue<T>>(cacheKey);
            if (IsFresh(cached, freshTtl))
            {
                return cached!.Value;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(requestTimeout);
            var value = await factory(timeoutCts.Token);

            if (value is null)
            {
                return cached is not null ? cached.Value : fallback;
            }

            _cache.Set(
                cacheKey,
                new CachedRealtimeValue<T>(value, _timeProvider.GetUtcNow().UtcDateTime),
                staleTtl);

            return value;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return cached is not null ? cached.Value : fallback;
        }
        catch
        {
            return cached is not null ? cached.Value : fallback;
        }
        finally
        {
            gate.Release();
        }
    }

    private static IReadOnlyList<string> NormalizeSymbols(IReadOnlyList<string>? symbols, bool fallbackToDefault)
    {
        var source = symbols is { Count: > 0 } ? symbols : (fallbackToDefault ? DefaultIndexSymbols : Array.Empty<string>());
        return source
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(StockSymbolNormalizer.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();
    }

    private static DateTime ResolveSnapshotTime(
        IReadOnlyList<BatchStockQuoteDto> quotes,
        MarketCapitalFlowSnapshotDto? mainFlow,
        NorthboundFlowSnapshotDto? northbound)
    {
        var candidates = new List<DateTime>();
        if (quotes.Count > 0)
        {
            candidates.Add(quotes.Max(item => item.Timestamp));
        }

        if (mainFlow is not null)
        {
            candidates.Add(mainFlow.SnapshotTime);
        }

        if (northbound is not null)
        {
            candidates.Add(northbound.SnapshotTime);
        }

        return candidates.Count > 0 ? candidates.Max() : TimeProvider.System.GetUtcNow().UtcDateTime;
    }

    private bool IsFresh<T>(CachedRealtimeValue<T>? cached, TimeSpan freshTtl)
    {
        return cached is not null && _timeProvider.GetUtcNow().UtcDateTime - cached.FetchedAtUtc <= freshTtl;
    }

    private sealed record CachedRealtimeValue<T>(T Value, DateTime FetchedAtUtc);
}

public static class RealtimeMarketCacheServiceCollectionExtensions
{
    public static IServiceCollection AddRealtimeMarketMemoryCache(this IServiceCollection services)
    {
        services.AddMemoryCache();
        return services;
    }
}