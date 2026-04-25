using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface ITradeAccountingService
{
    Task<TradeExecution> RecordTradeAsync(TradeExecutionCreateDto dto);
    Task<TradeExecution> UpdateTradeAsync(long id, TradeExecutionUpdateDto dto);
    Task DeleteTradeAsync(long id);
    Task<IReadOnlyList<TradeExecutionItemDto>> GetTradesAsync(string? symbol, DateTime? from, DateTime? to, string? type);
    Task<TradeSummaryDto> GetTradeSummaryAsync(string period, DateTime? from, DateTime? to);
    Task<TradeWinRateDto> GetWinRateAsync(DateTime? from, DateTime? to, string? symbol);
    Task RecalculatePositionAsync(string symbol);
    Task<(int deletedTrades, int deletedPositions, int deletedReviews)> ResetAllTradesAsync();
}

public sealed class TradeAccountingService : ITradeAccountingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _db;
    private readonly ITradeComplianceService _complianceService;
    private readonly ITradeExecutionInsightService _tradeExecutionInsightService;

    public TradeAccountingService(AppDbContext db, ITradeComplianceService complianceService, ITradeExecutionInsightService tradeExecutionInsightService)
    {
        _db = db;
        _complianceService = complianceService;
        _tradeExecutionInsightService = tradeExecutionInsightService;
    }

    public async Task<TradeExecution> RecordTradeAsync(TradeExecutionCreateDto dto)
    {
        if (!Enum.TryParse<TradeDirection>(dto.Direction, true, out var direction))
            throw new ArgumentException($"Invalid direction: {dto.Direction}");
        if (!Enum.TryParse<TradeType>(dto.TradeType, true, out var tradeType))
            throw new ArgumentException($"Invalid trade type: {dto.TradeType}");

        var normalizedSymbol = StockSymbolNormalizer.Normalize(dto.Symbol);
        var name = dto.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
        {
            var snapshot = await _db.StockQuoteSnapshots.FirstOrDefaultAsync(s => s.Symbol == normalizedSymbol);
            if (snapshot is not null && !string.IsNullOrWhiteSpace(snapshot.Name))
            {
                name = snapshot.Name;
            }
            else
            {
                var watchItem = await _db.ActiveWatchlists.FirstOrDefaultAsync(w => w.Symbol == normalizedSymbol);
                name = watchItem?.Name ?? "";
            }
        }

        var trade = new TradeExecution
        {
            PlanId = dto.PlanId,
            Symbol = normalizedSymbol,
            Name = name,
            Direction = direction,
            TradeType = tradeType,
            ExecutedPrice = dto.ExecutedPrice,
            Quantity = dto.Quantity,
            ExecutedAt = dto.ExecutedAt,
            Commission = dto.Commission,
            UserNote = dto.UserNote,
            CreatedAt = DateTime.UtcNow,
            ComplianceTag = ComplianceTag.Unplanned,
            PlanAction = NormalizeOptional(dto.PlanAction),
            ExecutionAction = NormalizeOptional(dto.ExecutionAction),
            DeviationTagsJson = SerializeTags(dto.DeviationTags),
            DeviationNote = NormalizeOptional(dto.DeviationNote),
            AbandonReason = NormalizeOptional(dto.AbandonReason)
        };

        // Auto-calculate on sell
        if (direction == TradeDirection.Sell)
        {
            var position = await _db.StockPositions
                .FirstOrDefaultAsync(p => p.Symbol == trade.Symbol);
            if (position is not null && position.AverageCostPrice > 0)
            {
                trade.CostBasis = position.AverageCostPrice;
                trade.RealizedPnL = (trade.ExecutedPrice - position.AverageCostPrice) * trade.Quantity;
                var costTotal = position.AverageCostPrice * trade.Quantity;
                trade.ReturnRate = costTotal != 0 ? trade.RealizedPnL.Value / costTotal : 0;
            }
        }

        // Day-trade detection: same day, same symbol, opposite direction exists
        var executedDate = trade.ExecutedAt.Date;
        var oppositeDirection = direction == TradeDirection.Buy ? TradeDirection.Sell : TradeDirection.Buy;
        var hasOpposite = await _db.TradeExecutions
            .AnyAsync(t => t.Symbol == trade.Symbol
                && t.ExecutedAt.Date == executedDate
                && t.Direction == oppositeDirection);
        if (hasOpposite)
        {
            trade.TradeType = TradeType.DayTrade;
            // Also mark the opposite trades as DayTrade
            var oppositeTrades = await _db.TradeExecutions
                .Where(t => t.Symbol == trade.Symbol
                    && t.ExecutedAt.Date == executedDate
                    && t.Direction == oppositeDirection
                    && t.TradeType != TradeType.DayTrade)
                .ToListAsync();
            foreach (var t in oppositeTrades)
            {
                t.TradeType = TradeType.DayTrade;
            }
        }

        _db.TradeExecutions.Add(trade);

        // Tag compliance
        await _complianceService.TagComplianceAsync(trade);
        await MarkPlanTriggeredAsync(trade);
        await _tradeExecutionInsightService.EnrichTradeExecutionAsync(trade);
        await _db.SaveChangesAsync();

        // Recalculate position
        await RecalculatePositionAsync(trade.Symbol);

        return trade;
    }

    public async Task<TradeExecution> UpdateTradeAsync(long id, TradeExecutionUpdateDto dto)
    {
        var trade = await _db.TradeExecutions.FindAsync(id)
            ?? throw new KeyNotFoundException($"Trade {id} not found");

        trade.ExecutedPrice = dto.ExecutedPrice;
        trade.Quantity = dto.Quantity;
        trade.ExecutedAt = dto.ExecutedAt;
        trade.Commission = dto.Commission;
        trade.UserNote = dto.UserNote;
        trade.PlanAction = NormalizeOptional(dto.PlanAction) ?? trade.PlanAction;
        trade.ExecutionAction = NormalizeOptional(dto.ExecutionAction) ?? trade.ExecutionAction;
        trade.DeviationTagsJson = SerializeTags(dto.DeviationTags) ?? trade.DeviationTagsJson;
        trade.DeviationNote = NormalizeOptional(dto.DeviationNote);
        trade.AbandonReason = NormalizeOptional(dto.AbandonReason);

        // Re-calculate PnL if sell
        if (trade.Direction == TradeDirection.Sell)
        {
            var position = await _db.StockPositions
                .FirstOrDefaultAsync(p => p.Symbol == trade.Symbol);
            if (position is not null && position.AverageCostPrice > 0)
            {
                trade.CostBasis = position.AverageCostPrice;
                trade.RealizedPnL = (trade.ExecutedPrice - position.AverageCostPrice) * trade.Quantity;
                var costTotal = position.AverageCostPrice * trade.Quantity;
                trade.ReturnRate = costTotal != 0 ? trade.RealizedPnL.Value / costTotal : 0;
            }
        }

        await _complianceService.TagComplianceAsync(trade);
        await _tradeExecutionInsightService.EnrichTradeExecutionAsync(trade, useLiveQuote: false);
        await _db.SaveChangesAsync();
        await RecalculatePositionAsync(trade.Symbol);
        return trade;
    }

    public async Task DeleteTradeAsync(long id)
    {
        var trade = await _db.TradeExecutions.FindAsync(id)
            ?? throw new KeyNotFoundException($"Trade {id} not found");
        var symbol = trade.Symbol;
        _db.TradeExecutions.Remove(trade);
        await _db.SaveChangesAsync();
        await RecalculatePositionAsync(symbol);
    }

    public async Task<IReadOnlyList<TradeExecutionItemDto>> GetTradesAsync(string? symbol, DateTime? from, DateTime? to, string? type)
    {
        var query = _db.TradeExecutions
            .Include(t => t.Plan)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(symbol))
            query = query.Where(t => t.Symbol == symbol.Trim());
        if (from.HasValue)
            query = query.Where(t => t.ExecutedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(t => t.ExecutedAt <= to.Value);
        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<TradeType>(type, true, out var tt))
            query = query.Where(t => t.TradeType == tt);

        var items = await query
            .OrderByDescending(t => t.ExecutedAt)
            .Take(200)
            .ToListAsync();

        return items.Select(MapToDto).ToList();
    }

    public async Task<TradeSummaryDto> GetTradeSummaryAsync(string period, DateTime? from, DateTime? to)
    {
        var cst = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var nowCst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cst);
        var endOfTodayCstUtc = TimeZoneInfo.ConvertTimeToUtc(nowCst.Date.AddDays(1), cst);
        DateTime periodStart, periodEnd;

        var p = period.ToLowerInvariant().Trim();
        // Support "7d", "30d", "365d" shorthand
        if (p.EndsWith("d") && int.TryParse(p.AsSpan(0, p.Length - 1), out var days))
        {
            periodStart = from ?? endOfTodayCstUtc.AddDays(-days);
            periodEnd = to ?? endOfTodayCstUtc;
        }
        else switch (p)
        {
            case "week":
                periodStart = from ?? endOfTodayCstUtc.AddDays(-7);
                periodEnd = to ?? endOfTodayCstUtc;
                break;
            case "month":
                periodStart = from ?? new DateTime(nowCst.Year, nowCst.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                periodEnd = to ?? endOfTodayCstUtc;
                break;
            case "year":
                periodStart = from ?? new DateTime(nowCst.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                periodEnd = to ?? endOfTodayCstUtc;
                break;
            default: // "all" or custom
                periodStart = from ?? DateTime.MinValue;
                periodEnd = to ?? endOfTodayCstUtc;
                break;
        }

        var sells = await _db.TradeExecutions
            .AsNoTracking()
            .Where(t => t.Direction == TradeDirection.Sell
                && t.ExecutedAt >= periodStart
                && t.ExecutedAt <= periodEnd)
            .ToListAsync();

        var totalTrades = sells.Count;
        var winCount = sells.Count(t => t.RealizedPnL > 0);
        var lossCount = sells.Count(t => t.RealizedPnL < 0);
        var winRate = totalTrades > 0 ? (decimal)winCount / totalTrades : 0;
        var totalPnL = sells.Sum(t => t.RealizedPnL ?? 0);
        var avgPnL = totalTrades > 0 ? totalPnL / totalTrades : 0;
        var avgWin = winCount > 0 ? sells.Where(t => t.RealizedPnL > 0).Average(t => t.RealizedPnL ?? 0) : 0;
        var avgLoss = lossCount > 0 ? Math.Abs(sells.Where(t => t.RealizedPnL < 0).Average(t => t.RealizedPnL ?? 0)) : 0;
        var plRatio = avgLoss != 0 ? avgWin / avgLoss : (winCount > 0 ? -1m : 0m);
        var dayTrades = sells.Where(t => t.TradeType == TradeType.DayTrade).ToList();
        var dayTradeCount = dayTrades.Count;
        var dayTradePnL = dayTrades.Sum(t => t.RealizedPnL ?? 0);
        var plannedCount = sells.Count(t => t.PlanId.HasValue);
        var complianceRate = totalTrades > 0 ? (decimal)sells.Count(t => t.ComplianceTag == ComplianceTag.FollowedPlan) / totalTrades : 0;
        var maxSingleLoss = sells.Count > 0 ? sells.Min(t => t.RealizedPnL ?? 0) : 0;
        if (maxSingleLoss > 0) maxSingleLoss = 0;

        return new TradeSummaryDto(
            period, periodStart, periodEnd,
            totalTrades, winCount, lossCount, winRate,
            totalPnL, avgPnL, plRatio,
            dayTradeCount, dayTradePnL,
            plannedCount, complianceRate,
            maxSingleLoss);
    }

    public async Task<TradeWinRateDto> GetWinRateAsync(DateTime? from, DateTime? to, string? symbol)
    {
        var query = _db.TradeExecutions
            .AsNoTracking()
            .Where(t => t.Direction == TradeDirection.Sell);

        if (!string.IsNullOrWhiteSpace(symbol))
            query = query.Where(t => t.Symbol == symbol.Trim());
        if (from.HasValue)
            query = query.Where(t => t.ExecutedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(t => t.ExecutedAt <= to.Value);

        var sells = await query
            .Include(t => t.Plan)
            .OrderByDescending(t => t.ExecutedAt)
            .ToListAsync();

        var total = sells.Count;
        var winCount = sells.Count(t => t.RealizedPnL > 0);
        var winRate = total > 0 ? (decimal)winCount / total : 0;
        var avgPnL = total > 0 ? sells.Average(t => t.RealizedPnL ?? 0) : 0;
        var avgReturn = total > 0 ? sells.Average(t => t.ReturnRate ?? 0) : 0;
        var recent = sells.Take(10).Select(MapToDto).ToList();

        return new TradeWinRateDto(total, winCount, winRate, avgPnL, avgReturn, recent);
    }

    public async Task RecalculatePositionAsync(string symbol)
    {
        var trades = await _db.TradeExecutions
            .Where(t => t.Symbol == symbol)
            .OrderBy(t => t.ExecutedAt)
            .ThenBy(t => t.Id)
            .ToListAsync();

        var quantity = 0;
        var totalCost = 0m;
        var name = string.Empty;

        foreach (var t in trades)
        {
            if (!string.IsNullOrWhiteSpace(t.Name))
                name = t.Name;

            if (t.Direction == TradeDirection.Buy)
            {
                totalCost += t.ExecutedPrice * t.Quantity;
                quantity += t.Quantity;
            }
            else
            {
                var sellQty = Math.Min(t.Quantity, quantity);
                if (quantity > 0)
                {
                    var avgCost = totalCost / quantity;
                    totalCost -= avgCost * sellQty;
                }
                quantity -= sellQty;
                if (quantity < 0) quantity = 0;
                if (quantity == 0) totalCost = 0;
            }
        }

        var avgCostPrice = quantity > 0 ? totalCost / quantity : 0;
        var position = await _db.StockPositions
            .FirstOrDefaultAsync(p => p.Symbol == symbol);

        if (position is null)
        {
            position = new StockPosition
            {
                Symbol = symbol,
                Name = name,
                QuantityLots = quantity,
                AverageCostPrice = avgCostPrice,
                TotalCost = totalCost,
                UpdatedAt = DateTime.UtcNow
            };
            _db.StockPositions.Add(position);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(name))
                position.Name = name;
            position.QuantityLots = quantity;
            position.AverageCostPrice = avgCostPrice;
            position.TotalCost = totalCost;
            position.UpdatedAt = DateTime.UtcNow;
        }

        // Bug #88: 同步计算 MarketValue / UnrealizedPnL / UnrealizedReturnRate
        if (quantity > 0)
        {
            // 优先用已有最新价，否则从行情快照取，最后用成本价兜底
            var latestPrice = position.LatestPrice;
            if (latestPrice is null or <= 0)
            {
                var snapshot = await _db.StockQuoteSnapshots
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Symbol == symbol);
                latestPrice = snapshot?.Price;
            }
            latestPrice ??= avgCostPrice;

            if (latestPrice > 0)
            {
                var mv = decimal.Round(latestPrice.Value * quantity, 2, MidpointRounding.AwayFromZero);
                var pnl = decimal.Round(mv - totalCost, 2, MidpointRounding.AwayFromZero);
                position.LatestPrice = latestPrice;
                position.MarketValue = mv;
                position.UnrealizedPnL = pnl;
                position.UnrealizedReturnRate = totalCost != 0
                    ? decimal.Round(pnl / totalCost, 6, MidpointRounding.AwayFromZero)
                    : 0;
            }
        }
        else
        {
            position.LatestPrice = null;
            position.MarketValue = null;
            position.UnrealizedPnL = null;
            position.UnrealizedReturnRate = null;
        }

        await _db.SaveChangesAsync();
    }

    private static TradeExecutionItemDto MapToDto(TradeExecution t) => new(
        t.Id, t.PlanId, t.Plan?.Title,
        t.Symbol, t.Name,
        t.Direction.ToString(), t.TradeType.ToString(),
        t.ExecutedPrice, t.Quantity, t.ExecutedAt,
        t.Commission, t.UserNote, t.CreatedAt,
        t.CostBasis, t.RealizedPnL, t.ReturnRate,
        t.ComplianceTag.ToString(),
        t.AgentDirection, t.AgentConfidence, t.MarketStageAtTrade,
        t.PlanSourceAgent, t.PlanAction, t.ExecutionAction,
        ParseDeviationTags(t.DeviationTagsJson), t.DeviationNote, t.AbandonReason,
        ParseScenarioSnapshot(t.ScenarioSnapshotJson), ParsePositionSnapshot(t.PositionSnapshotJson), t.CoachTip);

    public async Task<(int deletedTrades, int deletedPositions, int deletedReviews)> ResetAllTradesAsync()
    {
        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var deletedTrades = await _db.TradeExecutions.ExecuteDeleteAsync();
            var deletedPositions = await _db.StockPositions.ExecuteDeleteAsync();
            var deletedReviews = await _db.TradeReviews.ExecuteDeleteAsync();
            await tx.CommitAsync();
            return (deletedTrades, deletedPositions, deletedReviews);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private async Task MarkPlanTriggeredAsync(TradeExecution trade)
    {
        if (!trade.PlanId.HasValue)
        {
            return;
        }

        var plan = await _db.TradingPlans.FirstOrDefaultAsync(item => item.Id == trade.PlanId.Value);
        if (plan is null || plan.Status != TradingPlanStatus.Pending)
        {
            return;
        }

        plan.Status = TradingPlanStatus.Triggered;
        plan.TriggeredAt ??= trade.ExecutedAt;
        plan.UpdatedAt = DateTime.UtcNow;
    }

    private static string? NormalizeOptional(string? value)
    {
        var result = value?.Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static string? SerializeTags(IReadOnlyList<string>? value)
    {
        var normalized = value?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return normalized is { Length: > 0 } ? JsonSerializer.Serialize(normalized, JsonOptions) : null;
    }

    private static IReadOnlyList<string> ParseDeviationTags(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(value, JsonOptions)
                   ?.Where(item => !string.IsNullOrWhiteSpace(item))
                   .ToArray()
                   ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static TradingPlanScenarioStatusDto? ParseScenarioSnapshot(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TradingPlanScenarioStatusDto>(value, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static TradingPlanPositionContextDto? ParsePositionSnapshot(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TradingPlanPositionContextDto>(value, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
