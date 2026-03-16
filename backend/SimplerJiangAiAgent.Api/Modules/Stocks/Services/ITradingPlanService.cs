using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface ITradingPlanService
{
    Task<IReadOnlyList<TradingPlan>> GetListAsync(string? symbol, int take = 20, CancellationToken cancellationToken = default);
    Task<TradingPlan?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<TradingPlanSaveResult> CreateAsync(TradingPlanCreateDto request, CancellationToken cancellationToken = default);
    Task<TradingPlan?> UpdateAsync(long id, TradingPlanUpdateDto request, CancellationToken cancellationToken = default);
    Task<TradingPlan?> CancelAsync(long id, CancellationToken cancellationToken = default);
    Task<TradingPlan?> ResumeAsync(long id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}

public sealed record TradingPlanSaveResult(TradingPlan Plan, bool WatchlistEnsured);

public sealed class TradingPlanService : ITradingPlanService
{
    private readonly AppDbContext _dbContext;
    private readonly IActiveWatchlistService _watchlistService;
    private readonly IStockMarketContextService _marketContextService;

    public TradingPlanService(AppDbContext dbContext, IActiveWatchlistService watchlistService, IStockMarketContextService marketContextService)
    {
        _dbContext = dbContext;
        _watchlistService = watchlistService;
        _marketContextService = marketContextService;
    }

    public async Task<IReadOnlyList<TradingPlan>> GetListAsync(string? symbol, int take = 20, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.TradingPlans
            .AsNoTracking()
            .Where(IsRenderablePlan())
            .OrderByDescending(item => item.CreatedAt)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(symbol))
        {
            var normalized = StockSymbolNormalizer.Normalize(symbol);
            query = query.Where(item => item.Symbol == normalized);
        }

        return await query
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken);
    }

    public async Task<TradingPlan?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.TradingPlans
            .AsNoTracking()
            .Where(IsRenderablePlan())
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public async Task<TradingPlanSaveResult> CreateAsync(TradingPlanCreateDto request, CancellationToken cancellationToken = default)
    {
        var normalized = StockSymbolNormalizer.Normalize(request.Symbol);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("symbol 不能为空", nameof(request.Symbol));
        }

        var history = await _dbContext.StockAgentAnalysisHistories
            .FirstOrDefaultAsync(item => item.Id == request.AnalysisHistoryId, cancellationToken);
        if (history is null)
        {
            throw new InvalidOperationException("分析历史不存在");
        }

        if (!string.Equals(history.Symbol, normalized, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("分析历史与当前股票不匹配");
        }

        var now = DateTime.UtcNow;
        var name = NormalizeRequiredName(request.Name, history.Name);
        var marketContext = await _marketContextService.GetLatestAsync(normalized, cancellationToken);
        var plan = new TradingPlan
        {
            PlanKey = GeneratePlanKey(),
            Symbol = normalized,
            Name = name,
            Title = NormalizeLegacyTitle(name),
            Direction = ParseDirection(request.Direction),
            Status = TradingPlanStatus.Pending,
            TriggerPrice = request.TriggerPrice,
            InvalidPrice = request.InvalidPrice,
            StopLossPrice = request.StopLossPrice,
            TakeProfitPrice = request.TakeProfitPrice,
            TargetPrice = request.TargetPrice,
            ExpectedCatalyst = NormalizeOptional(request.ExpectedCatalyst),
            InvalidConditions = NormalizeOptional(request.InvalidConditions),
            RiskLimits = NormalizeOptional(request.RiskLimits),
            AnalysisSummary = NormalizeOptional(request.AnalysisSummary),
            AnalysisHistoryId = request.AnalysisHistoryId,
            SourceAgent = NormalizeOptional(request.SourceAgent) ?? "commander",
            UserNote = NormalizeOptional(request.UserNote),
            MarketStageLabelAtCreation = marketContext?.StageLabel,
            StageConfidenceAtCreation = marketContext?.StageConfidence,
            SuggestedPositionScale = marketContext?.SuggestedPositionScale,
            ExecutionFrequencyLabel = marketContext?.ExecutionFrequencyLabel,
            MainlineSectorName = marketContext?.MainlineSectorName,
            MainlineScoreAtCreation = marketContext?.MainlineScore,
            SectorNameAtCreation = marketContext?.StockSectorName,
            SectorCodeAtCreation = marketContext?.SectorCode,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.TradingPlans.Add(plan);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _watchlistService.UpsertAsync(plan.Symbol, plan.Name, "trading-plan", $"plan:{plan.Id}", true, cancellationToken);
        return new TradingPlanSaveResult(plan, true);
    }

    public async Task<TradingPlan?> UpdateAsync(long id, TradingPlanUpdateDto request, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.TradingPlans.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (plan is null)
        {
            return null;
        }

        if (!IsEditableStatus(plan.Status))
        {
            throw new InvalidOperationException("仅 Pending 计划允许编辑");
        }

        if (plan.Status == TradingPlanStatus.Draft)
        {
            plan.Status = TradingPlanStatus.Pending;
        }

        plan.Name = NormalizeRequiredName(request.Name, plan.Name);
        EnsureLegacyCompatibility(plan);
        plan.Direction = ParseDirection(request.Direction, plan.Direction);
        plan.TriggerPrice = request.TriggerPrice;
        plan.InvalidPrice = request.InvalidPrice;
        plan.StopLossPrice = request.StopLossPrice;
        plan.TakeProfitPrice = request.TakeProfitPrice;
        plan.TargetPrice = request.TargetPrice;
        plan.ExpectedCatalyst = NormalizeOptional(request.ExpectedCatalyst);
        plan.InvalidConditions = NormalizeOptional(request.InvalidConditions);
        plan.RiskLimits = NormalizeOptional(request.RiskLimits);
        plan.AnalysisSummary = NormalizeOptional(request.AnalysisSummary);
        plan.SourceAgent = NormalizeOptional(request.SourceAgent) ?? plan.SourceAgent;
        plan.UserNote = NormalizeOptional(request.UserNote);
        plan.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return plan;
    }

    public async Task<TradingPlan?> CancelAsync(long id, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.TradingPlans.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (plan is null)
        {
            return null;
        }

        if (plan.Status == TradingPlanStatus.Cancelled)
        {
            return plan;
        }

        EnsureLegacyCompatibility(plan);
        plan.Status = TradingPlanStatus.Cancelled;
        plan.CancelledAt = DateTime.UtcNow;
        plan.UpdatedAt = plan.CancelledAt.Value;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return plan;
    }

    public async Task<TradingPlan?> ResumeAsync(long id, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.TradingPlans.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (plan is null)
        {
            return null;
        }

        if (plan.Status != TradingPlanStatus.ReviewRequired)
        {
            throw new InvalidOperationException("仅 ReviewRequired 计划允许恢复观察");
        }

        EnsureLegacyCompatibility(plan);
        plan.Status = TradingPlanStatus.Pending;
        plan.UpdatedAt = DateTime.UtcNow;
        _dbContext.TradingPlanEvents.Add(new TradingPlanEvent
        {
            PlanId = plan.Id,
            Symbol = plan.Symbol,
            EventType = TradingPlanEventType.ReviewCleared,
            Strategy = "manual-review",
            Reason = "人工复核后恢复观察",
            CreatedAt = plan.UpdatedAt,
            Severity = TradingPlanEventSeverity.Info,
            Message = "人工复核后已恢复观察。",
            MetadataJson = JsonSerializer.Serialize(new { resumedAt = plan.UpdatedAt }),
            OccurredAt = plan.UpdatedAt
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return plan;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.TradingPlans.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (plan is null)
        {
            return false;
        }

        _dbContext.TradingPlans.Remove(plan);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string NormalizeRequiredName(string? preferred, string fallback)
    {
        var value = NormalizeOptional(preferred);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback.Trim();
    }

    private static void EnsureLegacyCompatibility(TradingPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.PlanKey))
        {
            plan.PlanKey = GeneratePlanKey();
        }

        plan.Title = NormalizeLegacyTitle(plan.Name);
    }

    private static string GeneratePlanKey()
    {
        return $"plan-{Guid.NewGuid():N}";
    }

    private static string NormalizeLegacyTitle(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        var result = value?.Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static TradingPlanDirection ParseDirection(string? value, TradingPlanDirection fallback = TradingPlanDirection.Long)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return Enum.TryParse<TradingPlanDirection>(value.Trim(), true, out var direction)
            ? direction
            : fallback;
    }

    private static bool IsEditableStatus(TradingPlanStatus status)
    {
        return status is TradingPlanStatus.Pending or TradingPlanStatus.Draft or TradingPlanStatus.ReviewRequired;
    }

    private static System.Linq.Expressions.Expression<Func<TradingPlan, bool>> IsRenderablePlan()
    {
        return item => item.AnalysisHistoryId > 0
            && !string.IsNullOrWhiteSpace(item.Symbol)
            && !string.IsNullOrWhiteSpace(item.Name);
    }
}