using Microsoft.Extensions.Caching.Memory;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using System.Collections.Concurrent;

namespace SimplerJiangAiAgent.Api.Modules.Market.Services;

public sealed class RealtimeSectorBoardService : IRealtimeSectorBoardService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CacheGates = new(StringComparer.Ordinal);
    private static readonly TimeSpan FreshTtl = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StaleTtl = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private readonly IMemoryCache _cache;
    private readonly IEastmoneySectorRotationClient _client;
    private readonly TimeProvider _timeProvider;

    public RealtimeSectorBoardService(IMemoryCache cache, IEastmoneySectorRotationClient client, TimeProvider? timeProvider = null)
    {
        _cache = cache;
        _client = client;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<RealtimeSectorBoardPageDto> GetPageAsync(string boardType, int take, string? sort = null, CancellationToken cancellationToken = default)
    {
        var normalizedBoardType = NormalizeBoardType(boardType);
        var normalizedSort = NormalizeSort(sort);
        var safeTake = Math.Clamp(take, 1, 120);
        var cacheKey = $"market:realtime:sectors:{normalizedBoardType}:{safeTake}:{normalizedSort}";

        return await GetCachedAsync(
            cacheKey,
            FreshTtl,
            StaleTtl,
            RequestTimeout,
            async ct =>
            {
                var rows = await _client.GetBoardRankingsAsync(normalizedBoardType, safeTake, ct);
                var snapshotTime = _timeProvider.GetUtcNow().UtcDateTime;
                var items = SortRows(rows, normalizedSort)
                    .Take(safeTake)
                    .Select(row => new RealtimeSectorBoardItemDto(
                        row.BoardType,
                        row.SectorCode,
                        row.SectorName,
                        row.ChangePercent,
                        row.MainNetInflow,
                        row.SuperLargeNetInflow,
                        row.LargeNetInflow,
                        row.MediumNetInflow,
                        row.SmallNetInflow,
                        row.TurnoverAmount,
                        row.TurnoverShare,
                        row.RankNo,
                        snapshotTime))
                    .ToArray();

                return new RealtimeSectorBoardPageDto(normalizedBoardType, safeTake, normalizedSort, snapshotTime, items);
            },
            new RealtimeSectorBoardPageDto(normalizedBoardType, safeTake, normalizedSort, _timeProvider.GetUtcNow().UtcDateTime, Array.Empty<RealtimeSectorBoardItemDto>()),
            cancellationToken);
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
            _cache.Set(cacheKey, new CachedRealtimeValue<T>(value, _timeProvider.GetUtcNow().UtcDateTime), staleTtl);
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

    private static IReadOnlyList<EastmoneySectorBoardRow> SortRows(IReadOnlyList<EastmoneySectorBoardRow> rows, string sort)
    {
        return sort switch
        {
            "change" => rows.OrderByDescending(row => row.ChangePercent).ThenBy(row => row.RankNo).ToArray(),
            "flow" => rows.OrderByDescending(row => row.MainNetInflow).ThenByDescending(row => row.ChangePercent).ToArray(),
            _ => rows.OrderBy(row => row.RankNo).ThenByDescending(row => row.ChangePercent).ToArray()
        };
    }

    private static string NormalizeBoardType(string? boardType)
    {
        var value = string.IsNullOrWhiteSpace(boardType) ? SectorBoardTypes.Concept : boardType.Trim().ToLowerInvariant();
        return value switch
        {
            SectorBoardTypes.Industry => SectorBoardTypes.Industry,
            SectorBoardTypes.Style => SectorBoardTypes.Style,
            _ => SectorBoardTypes.Concept
        };
    }

    private static string NormalizeSort(string? sort)
    {
        var value = string.IsNullOrWhiteSpace(sort) ? "rank" : sort.Trim().ToLowerInvariant();
        return value is "change" or "flow" ? value : "rank";
    }

    private bool IsFresh<T>(CachedRealtimeValue<T>? cached, TimeSpan freshTtl)
    {
        return cached is not null && _timeProvider.GetUtcNow().UtcDateTime - cached.FetchedAtUtc <= freshTtl;
    }

    private sealed record CachedRealtimeValue<T>(T Value, DateTime FetchedAtUtc);
}