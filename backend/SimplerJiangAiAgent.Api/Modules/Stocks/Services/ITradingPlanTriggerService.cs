using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface ITradingPlanTriggerService
{
    Task<int> EvaluateAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TradingPlanEvent>> GetEventsAsync(string? symbol, long? planId, int take = 20, CancellationToken cancellationToken = default);
}

public sealed class TradingPlanTriggerService : ITradingPlanTriggerService
{
    private readonly AppDbContext _dbContext;
    private readonly TradingPlanTriggerOptions _options;

    public TradingPlanTriggerService(AppDbContext dbContext, IOptions<TradingPlanTriggerOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public async Task<int> EvaluateAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || !ChinaAStockMarketClock.IsTradingSession(now))
        {
            return 0;
        }

        var activeSymbols = await _dbContext.ActiveWatchlists
            .AsNoTracking()
            .Where(item => item.IsEnabled)
            .Select(item => item.Symbol)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        if (activeSymbols.Length == 0)
        {
            return 0;
        }

        var pendingPlans = await _dbContext.TradingPlans
            .Where(item => item.Status == TradingPlanStatus.Pending && activeSymbols.Contains(item.Symbol))
            .OrderBy(item => item.CreatedAt)
            .Take(Math.Max(1, _options.MaxPlansPerPass))
            .ToListAsync(cancellationToken);

        if (pendingPlans.Count == 0)
        {
            return 0;
        }

        var symbols = pendingPlans
            .Select(item => item.Symbol)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var latestQuotes = await _dbContext.StockQuoteSnapshots
            .Where(item => symbols.Contains(item.Symbol))
            .GroupBy(item => item.Symbol)
            .Select(group => group.OrderByDescending(item => item.Timestamp).First())
            .ToListAsync(cancellationToken);

        var quoteBySymbol = latestQuotes.ToDictionary(item => item.Symbol, item => item, StringComparer.OrdinalIgnoreCase);

        var cutoff = now.UtcDateTime.AddMinutes(-Math.Max(5, _options.DivergenceLookbackMinutes));
        var recentMinutePoints = await _dbContext.MinuteLinePoints
            .Where(item => symbols.Contains(item.Symbol))
            .ToListAsync(cancellationToken);

        var minutePointsBySymbol = recentMinutePoints
            .Where(item => CombinePointTimestamp(item) >= cutoff)
            .GroupBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(item => item.Date).ThenBy(item => item.Time).ToList(),
                StringComparer.OrdinalIgnoreCase);

        var planIds = pendingPlans.Select(item => item.Id).ToArray();
        var latestWarningByPlanId = await _dbContext.TradingPlanEvents
            .Where(item => planIds.Contains(item.PlanId) && item.EventType == TradingPlanEventType.VolumeDivergenceWarning)
            .GroupBy(item => item.PlanId)
            .Select(group => group.OrderByDescending(item => item.OccurredAt).First())
            .ToListAsync(cancellationToken);

        var latestWarningMap = latestWarningByPlanId.ToDictionary(item => item.PlanId, item => item);

        var changes = 0;

        foreach (var plan in pendingPlans)
        {
            if (!quoteBySymbol.TryGetValue(plan.Symbol, out var latestQuote))
            {
                continue;
            }

            var occurredAt = latestQuote.Timestamp == default ? now.UtcDateTime : latestQuote.Timestamp;
            var transition = EvaluateTransition(plan, latestQuote.Price);
            if (transition is not null)
            {
                ApplyTransition(plan, transition, occurredAt, latestQuote.Price);
                _dbContext.TradingPlanEvents.Add(new TradingPlanEvent
                {
                    PlanId = plan.Id,
                    Symbol = plan.Symbol,
                    EventType = transition.EventType,
                    Severity = transition.Severity,
                    Message = transition.Message,
                    SnapshotPrice = latestQuote.Price,
                    MetadataJson = JsonSerializer.Serialize(new { quoteTimestamp = occurredAt }),
                    OccurredAt = occurredAt
                });
                changes++;
                continue;
            }

            if (!minutePointsBySymbol.TryGetValue(plan.Symbol, out var points))
            {
                continue;
            }

            var divergence = DetectVolumeDivergence(points);
            if (divergence is null)
            {
                continue;
            }

            var metadataJson = JsonSerializer.Serialize(new
            {
                warningWindowKey = BuildWarningWindowKey(divergence.LatestPointAt, Math.Max(5, _options.DivergenceLookbackMinutes)),
                latestMinuteAt = divergence.LatestPointAt,
                priceSlope = divergence.PriceSlope,
                volumeSlope = divergence.VolumeSlope
            });

            if (latestWarningMap.TryGetValue(plan.Id, out var latestWarning)
                && string.Equals(TryGetWarningWindowKey(latestWarning.MetadataJson), TryGetWarningWindowKey(metadataJson), StringComparison.Ordinal))
            {
                continue;
            }

            _dbContext.TradingPlanEvents.Add(new TradingPlanEvent
            {
                PlanId = plan.Id,
                Symbol = plan.Symbol,
                EventType = TradingPlanEventType.VolumeDivergenceWarning,
                Severity = TradingPlanEventSeverity.Warning,
                Message = $"最近{Math.Max(5, _options.DivergenceLookbackMinutes)}分钟价格走高但量能走弱，请注意量价背离。",
                SnapshotPrice = latestQuote.Price,
                MetadataJson = metadataJson,
                OccurredAt = divergence.LatestPointAt
            });
            latestWarningMap[plan.Id] = new TradingPlanEvent
            {
                PlanId = plan.Id,
                MetadataJson = metadataJson,
                OccurredAt = divergence.LatestPointAt
            };
            changes++;
        }

        if (changes > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return changes;
    }

    public async Task<IReadOnlyList<TradingPlanEvent>> GetEventsAsync(string? symbol, long? planId, int take = 20, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.TradingPlanEvents
            .AsNoTracking()
            .OrderByDescending(item => item.OccurredAt)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(symbol))
        {
            var normalized = StockSymbolNormalizer.Normalize(symbol);
            query = query.Where(item => item.Symbol == normalized);
        }

        if (planId.HasValue && planId.Value > 0)
        {
            query = query.Where(item => item.PlanId == planId.Value);
        }

        return await query
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken);
    }

    private static TradingPlanTransition? EvaluateTransition(TradingPlan plan, decimal latestPrice)
    {
        var invalidMatched = IsInvalidMatched(plan, latestPrice);
        if (invalidMatched)
        {
            return new TradingPlanTransition(
                TradingPlanStatus.Invalid,
                TradingPlanEventType.Invalidated,
                TradingPlanEventSeverity.Critical,
                $"失效价已命中，最新价 {latestPrice:0.00} 已触发计划失效。"
            );
        }

        var triggerMatched = IsTriggerMatched(plan, latestPrice);
        if (triggerMatched)
        {
            return new TradingPlanTransition(
                TradingPlanStatus.Triggered,
                TradingPlanEventType.Triggered,
                TradingPlanEventSeverity.Info,
                $"触发价已命中，最新价 {latestPrice:0.00} 已满足计划执行条件。"
            );
        }

        return null;
    }

    private static bool IsTriggerMatched(TradingPlan plan, decimal latestPrice)
    {
        if (!plan.TriggerPrice.HasValue)
        {
            return false;
        }

        return plan.Direction == TradingPlanDirection.Short
            ? latestPrice <= plan.TriggerPrice.Value
            : latestPrice >= plan.TriggerPrice.Value;
    }

    private static bool IsInvalidMatched(TradingPlan plan, decimal latestPrice)
    {
        if (!plan.InvalidPrice.HasValue)
        {
            return false;
        }

        return plan.Direction == TradingPlanDirection.Short
            ? latestPrice >= plan.InvalidPrice.Value
            : latestPrice <= plan.InvalidPrice.Value;
    }

    private static void ApplyTransition(TradingPlan plan, TradingPlanTransition transition, DateTime occurredAt, decimal latestPrice)
    {
        plan.Status = transition.NextStatus;
        plan.UpdatedAt = occurredAt;
        if (transition.NextStatus == TradingPlanStatus.Triggered)
        {
            plan.TriggeredAt = occurredAt;
        }
        else if (transition.NextStatus == TradingPlanStatus.Invalid)
        {
            plan.InvalidatedAt = occurredAt;
        }
    }

    private static VolumeDivergenceSnapshot? DetectVolumeDivergence(IReadOnlyList<MinuteLinePointEntity> points)
    {
        if (points.Count < 4)
        {
            return null;
        }

        var ordered = points
            .OrderBy(item => item.Date)
            .ThenBy(item => item.Time)
            .ToList();

        var perBarVolumes = new List<decimal>(ordered.Count);
        decimal? previousCumulative = null;
        foreach (var point in ordered)
        {
            var perBarVolume = previousCumulative.HasValue
                ? Math.Max(0m, point.Volume - previousCumulative.Value)
                : point.Volume;
            perBarVolumes.Add(perBarVolume);
            previousCumulative = point.Volume;
        }

        var segmentSize = Math.Max(1, ordered.Count / 3);
        var firstAverageVolume = perBarVolumes.Take(segmentSize).DefaultIfEmpty(0m).Average();
        var lastAverageVolume = perBarVolumes.Skip(Math.Max(0, perBarVolumes.Count - segmentSize)).DefaultIfEmpty(0m).Average();
        var firstPrice = ordered.First().Price;
        var lastPrice = ordered.Last().Price;
        var priceSlope = lastPrice - firstPrice;
        var volumeSlope = lastAverageVolume - firstAverageVolume;

        if (priceSlope > 0m && volumeSlope < 0m)
        {
            return new VolumeDivergenceSnapshot(
                CombinePointTimestamp(ordered[^1]),
                priceSlope,
                volumeSlope);
        }

        return null;
    }

    private static DateTime CombinePointTimestamp(MinuteLinePointEntity point)
    {
        return point.Date.ToDateTime(TimeOnly.FromTimeSpan(point.Time), DateTimeKind.Unspecified);
    }

    private static string BuildWarningWindowKey(DateTime occurredAt, int lookbackMinutes)
    {
        var minutes = Math.Max(5, lookbackMinutes);
        var utc = occurredAt.Kind == DateTimeKind.Utc ? occurredAt : occurredAt.ToUniversalTime();
        var bucketTicks = TimeSpan.FromMinutes(minutes).Ticks;
        var normalizedTicks = utc.Ticks - (utc.Ticks % bucketTicks);
        return new DateTime(normalizedTicks, DateTimeKind.Utc).ToString("O");
    }

    private static string? TryGetWarningWindowKey(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (document.RootElement.TryGetProperty("warningWindowKey", out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private sealed record TradingPlanTransition(
        TradingPlanStatus NextStatus,
        TradingPlanEventType EventType,
        TradingPlanEventSeverity Severity,
        string Message);

    private sealed record VolumeDivergenceSnapshot(
        DateTime LatestPointAt,
        decimal PriceSlope,
        decimal VolumeSlope);
}