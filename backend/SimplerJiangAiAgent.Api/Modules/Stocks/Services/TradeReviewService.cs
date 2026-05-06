using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface ITradeReviewService
{
    Task<TradeReviewItemDto> GenerateReviewAsync(TradeReviewGenerateDto dto, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TradeReviewItemDto>> GetReviewsAsync(string? type, DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
    Task<TradeReviewItemDto?> GetReviewByIdAsync(long id, CancellationToken cancellationToken = default);
}

public sealed class TradeReviewService : ITradeReviewService
{
    private readonly AppDbContext _db;
    private readonly ILlmService _llmService;
    private readonly IGpuTaskQueue _gpuQueue;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public TradeReviewService(AppDbContext db, ILlmService llmService, IGpuTaskQueue gpuQueue)
    {
        _db = db;
        _llmService = llmService;
        _gpuQueue = gpuQueue;
    }

    public async Task<TradeReviewItemDto> GenerateReviewAsync(TradeReviewGenerateDto dto, CancellationToken cancellationToken = default)
    {
        var reviewType = ParseReviewType(dto.Type);
        var (periodStart, periodEnd) = ResolvePeriod(reviewType, dto.From, dto.To);

        // 1. Collect trades
        var trades = await _db.TradeExecutions
            .AsNoTracking()
            .Where(t => t.ExecutedAt >= periodStart && t.ExecutedAt <= periodEnd)
            .OrderBy(t => t.ExecutedAt)
            .ToListAsync(cancellationToken);

        // 2. Compute stats
        var sells = trades.Where(t => t.Direction == TradeDirection.Sell).ToList();
        var tradeCount = sells.Count;
        var totalPnL = sells.Sum(t => t.RealizedPnL ?? 0);
        var winCount = sells.Count(t => t.RealizedPnL > 0);
        var winRate = tradeCount > 0 ? (decimal)winCount / tradeCount : 0;
        var followedCount = trades.Count(t => t.ComplianceTag == ComplianceTag.FollowedPlan);
        var complianceRate = trades.Count > 0 ? (decimal)followedCount / trades.Count : 0;

        // 3. Collect market context
        var marketSnapshots = await _db.MarketSentimentSnapshots
            .AsNoTracking()
            .Where(s => s.TradingDate >= periodStart.Date && s.TradingDate <= periodEnd.Date)
            .OrderByDescending(s => s.SnapshotTime)
            .Take(10)
            .ToListAsync(cancellationToken);

        // 4. Collect news
        var tradeSymbols = trades.Select(t => t.Symbol).Distinct().ToList();
        var news = await _db.LocalStockNews
            .AsNoTracking()
            .Where(n => tradeSymbols.Contains(n.Symbol) && n.PublishTime >= periodStart && n.PublishTime <= periodEnd)
            .OrderByDescending(n => n.PublishTime)
            .Take(20)
            .ToListAsync(cancellationToken);

        // 5. Collect sector rotation
        var sectorSnapshots = await _db.SectorRotationSnapshots
            .AsNoTracking()
            .Where(s => s.TradingDate >= periodStart.Date && s.TradingDate <= periodEnd.Date && s.IsMainline)
            .OrderByDescending(s => s.TradingDate)
            .Take(10)
            .ToListAsync(cancellationToken);

        // 6. Build structured context JSON
        var contextObj = new
        {
            period = new { start = periodStart, end = periodEnd },
            trades = trades.Select(t => new
            {
                t.Symbol,
                t.Name,
                direction = t.Direction.ToString(),
                t.ExecutedPrice,
                t.Quantity,
                t.ExecutedAt,
                realizedPnL = t.RealizedPnL,
                returnRate = t.ReturnRate,
                compliance = t.ComplianceTag.ToString(),
                t.AgentDirection,
                t.AgentConfidence,
                t.MarketStageAtTrade
            }),
            stats = new
            {
                totalTrades = trades.Count,
                sellCount = tradeCount,
                winCount,
                winRate,
                totalPnL,
                complianceRate,
                followedCount,
                deviatedCount = trades.Count(t => t.ComplianceTag == ComplianceTag.DeviatedFromPlan),
                unplannedCount = trades.Count(t => t.ComplianceTag == ComplianceTag.Unplanned)
            },
            marketStages = marketSnapshots.Select(s => new
            {
                s.TradingDate,
                s.StageLabel,
                s.StageConfidence,
                s.LimitUpCount,
                s.Advancers,
                s.Decliners
            }),
            majorNews = news.Select(n => new
            {
                n.Symbol,
                n.Title,
                n.AiSentiment,
                n.PublishTime
            }),
            mainlineSectors = sectorSnapshots.Select(s => new
            {
                s.SectorName,
                s.TradingDate,
                s.ChangePercent,
                s.MainlineScore
            })
        };

        var contextJson = JsonSerializer.Serialize(contextObj, JsonOpts);

        // 7. Build LLM prompt
        var prompt = BuildPrompt(contextJson);

        // 8. Call LLM
        await using var gpuLease = await _gpuQueue.AcquireAsync(
            "交易复盘", GpuTaskPriority.High, cancellationToken);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, gpuLease.CancellationToken);

        LlmChatResult llmResult;
        try
        {
            llmResult = await _llmService.ChatAsync(
                "active",
                new LlmChatRequest(prompt, null, 0.3),
                linkedCts.Token);
        }
        catch
        {
            gpuLease.MarkFailed();
            throw;
        }

        var reviewContent = llmResult.Content?.Trim() ?? "复盘生成失败，LLM 无响应。";

        // 9. Save to DB
        var review = new TradeReview
        {
            ReviewType = reviewType,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            TradeCount = tradeCount,
            TotalPnL = totalPnL,
            WinRate = winRate,
            ComplianceRate = complianceRate,
            ReviewContent = reviewContent,
            ContextSummaryJson = contextJson,
            LlmTraceId = llmResult.TraceId,
            CreatedAt = DateTime.UtcNow
        };

        _db.TradeReviews.Add(review);
        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(review);
    }

    public async Task<IReadOnlyList<TradeReviewItemDto>> GetReviewsAsync(string? type, DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
    {
        var query = _db.TradeReviews.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<ReviewType>(type, true, out var rt))
            query = query.Where(r => r.ReviewType == rt);
        if (from.HasValue)
            query = query.Where(r => r.PeriodStart >= from.Value);
        if (to.HasValue)
            query = query.Where(r => r.PeriodEnd <= to.Value);

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        return items.Select(MapToDto).ToList();
    }

    public async Task<TradeReviewItemDto?> GetReviewByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var review = await _db.TradeReviews
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        return review is null ? null : MapToDto(review);
    }

    private static ReviewType ParseReviewType(string? type) => type?.ToLowerInvariant() switch
    {
        "daily" => ReviewType.Daily,
        "weekly" => ReviewType.Weekly,
        "monthly" => ReviewType.Monthly,
        _ => ReviewType.Custom
    };

    private static readonly TimeZoneInfo CstZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

    private static (DateTime start, DateTime end) ResolvePeriod(ReviewType type, DateTime? from, DateTime? to)
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CstZone);
        return type switch
        {
            ReviewType.Daily => (now.Date, now),
            ReviewType.Weekly => (now.AddDays(-(int)now.DayOfWeek + (now.DayOfWeek == DayOfWeek.Sunday ? -6 : 1)).Date, now),
            ReviewType.Monthly => (new DateTime(now.Year, now.Month, 1), now),
            _ => (from ?? now.AddDays(-7), to ?? now)
        };
    }

    private static string BuildPrompt(string contextJson)
    {
        var sb = new StringBuilder();
        sb.AppendLine("你是一个冷静、客观、直言不讳的交易教练。你不会恭维用户，而是实事求是地指出问题。");
        sb.AppendLine();
        sb.AppendLine("基于以下交易记录和市场数据，生成一份结构化的复盘报告。");
        sb.AppendLine();
        sb.AppendLine("## 输出格式（Markdown）");
        sb.AppendLine();
        sb.AppendLine("### 一、市场环境回顾");
        sb.AppendLine("本期市场处于什么阶段？主线板块是什么？有什么重大事件？");
        sb.AppendLine();
        sb.AppendLine("### 二、操作回顾");
        sb.AppendLine("用户做了哪些交易？每笔交易的结果如何？");
        sb.AppendLine();
        sb.AppendLine("### 三、赢亏归因");
        sb.AppendLine("赚钱的交易为什么赚了（顺势/选股准/买点好）？亏钱的交易为什么亏了（逆势/追涨/止损迟/无计划）？");
        sb.AppendLine();
        sb.AppendLine("### 四、纪律评估");
        sb.AppendLine("用户有多少交易遵守了系统建议？偏离了多少次？偏离的结果如何？");
        sb.AppendLine();
        sb.AppendLine("### 五、行为模式观察");
        sb.AppendLine("用户是否有反复出现的行为模式（追涨、频繁交易、退潮期加仓、忽视止损）？");
        sb.AppendLine();
        sb.AppendLine("### 六、改进建议");
        sb.AppendLine("针对以上观察，给出 2-3 条具体的、可执行的改进建议。");
        sb.AppendLine();
        sb.AppendLine("要求：");
        sb.AppendLine("- 直接、诚实、有数据支撑");
        sb.AppendLine("- 像一个真正关心你但不客气的教练");
        sb.AppendLine("- 引用具体的交易记录和数字");
        sb.AppendLine("- 不要空话套话");
        sb.AppendLine("- 使用中文回答");
        sb.AppendLine();
        sb.AppendLine("## 交易与市场数据");
        sb.AppendLine();
        sb.AppendLine(contextJson);

        return sb.ToString();
    }

    private static TradeReviewItemDto MapToDto(TradeReview r) => new(
        r.Id,
        r.ReviewType.ToString(),
        r.PeriodStart,
        r.PeriodEnd,
        r.TradeCount,
        r.TotalPnL,
        r.WinRate,
        r.ComplianceRate,
        r.ReviewContent,
        r.CreatedAt);
}
