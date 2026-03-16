using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class TradingPlanReviewServiceTests
{
    [Fact]
    public async Task EvaluateAsync_MarksPlanReviewRequiredWhenThreateningNewsArrives()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext);
        await SeedWatchlistAsync(dbContext, plan.Symbol, plan.Name);
        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Symbol = plan.Symbol,
            Name = plan.Name,
            Title = "公司核心客户订单突然流失",
            Category = "快讯",
            Source = "测试快讯",
            SourceTag = "test-news",
            PublishTime = new DateTime(2026, 3, 16, 2, 5, 0, DateTimeKind.Utc),
            CrawledAt = new DateTime(2026, 3, 16, 2, 6, 0, DateTimeKind.Utc),
            AiSentiment = "利空",
            AiTarget = "个股:深科技"
        });
        await dbContext.SaveChangesAsync();

        var service = new TradingPlanReviewService(
            dbContext,
            new StubLlmService("""
            {
              "isPlanThreatened": true,
              "reason": "突发订单流失直接削弱原计划对基本面改善的预期。",
              "confidence": 88
            }
            """),
            Options.Create(new TradingPlanReviewOptions()),
            NullLogger<TradingPlanReviewService>.Instance);

        var changes = await service.EvaluateAsync(new DateTimeOffset(2026, 3, 16, 2, 8, 0, TimeSpan.Zero));

        Assert.Equal(1, changes);
        Assert.Equal(TradingPlanStatus.ReviewRequired, plan.Status);
        var reviewEvent = await dbContext.TradingPlanEvents.SingleAsync();
        Assert.Equal(TradingPlanEventType.ReviewRequired, reviewEvent.EventType);
        Assert.Equal(TradingPlanEventSeverity.Critical, reviewEvent.Severity);
        Assert.Contains("订单突然流失", reviewEvent.Message);
    }

    [Fact]
    public async Task EvaluateAsync_RecordsSafeReviewWithoutChangingPlanStatus()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext);
        await SeedWatchlistAsync(dbContext, plan.Symbol, plan.Name);
        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Symbol = plan.Symbol,
            Name = plan.Name,
            Title = "公司参加行业交流会",
            Category = "快讯",
            Source = "测试快讯",
            SourceTag = "test-news",
            PublishTime = new DateTime(2026, 3, 16, 2, 5, 0, DateTimeKind.Utc),
            CrawledAt = new DateTime(2026, 3, 16, 2, 6, 0, DateTimeKind.Utc),
            AiSentiment = "中性"
        });
        await dbContext.SaveChangesAsync();

        var service = new TradingPlanReviewService(
            dbContext,
            new StubLlmService("""
            {
              "isPlanThreatened": false,
              "reason": "该新闻未直接触发原计划失效条件。",
              "confidence": 61
            }
            """),
            Options.Create(new TradingPlanReviewOptions()),
            NullLogger<TradingPlanReviewService>.Instance);

        var changes = await service.EvaluateAsync(new DateTimeOffset(2026, 3, 16, 2, 8, 0, TimeSpan.Zero));

        Assert.Equal(1, changes);
        Assert.Equal(TradingPlanStatus.Pending, plan.Status);
        var reviewEvent = await dbContext.TradingPlanEvents.SingleAsync();
        Assert.Equal(TradingPlanEventType.NewsReviewed, reviewEvent.EventType);
        Assert.Equal(TradingPlanEventSeverity.Info, reviewEvent.Severity);
    }

    [Fact]
    public async Task EvaluateAsync_DoesNotBlockMainFlowWhenLlmFails()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext);
        await SeedWatchlistAsync(dbContext, plan.Symbol, plan.Name);
        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Symbol = plan.Symbol,
            Name = plan.Name,
            Title = "测试新闻",
            Category = "快讯",
            Source = "测试快讯",
            SourceTag = "test-news",
            PublishTime = new DateTime(2026, 3, 16, 2, 5, 0, DateTimeKind.Utc),
            CrawledAt = new DateTime(2026, 3, 16, 2, 6, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var service = new TradingPlanReviewService(
            dbContext,
            new StubLlmService(exception: new InvalidOperationException("provider unavailable")),
            Options.Create(new TradingPlanReviewOptions()),
            NullLogger<TradingPlanReviewService>.Instance);

        var changes = await service.EvaluateAsync(new DateTimeOffset(2026, 3, 16, 2, 8, 0, TimeSpan.Zero));

        Assert.Equal(0, changes);
        Assert.Equal(TradingPlanStatus.Pending, plan.Status);
        Assert.Empty(await dbContext.TradingPlanEvents.ToListAsync());
    }

    [Fact]
    public async Task EvaluateAsync_DeduplicatesReviewedNewsPerPlan()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext);
        await SeedWatchlistAsync(dbContext, plan.Symbol, plan.Name);
        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Id = 18,
            Symbol = plan.Symbol,
            Name = plan.Name,
            Title = "测试新闻",
            Category = "快讯",
            Source = "测试快讯",
            SourceTag = "test-news",
            PublishTime = new DateTime(2026, 3, 16, 2, 5, 0, DateTimeKind.Utc),
            CrawledAt = new DateTime(2026, 3, 16, 2, 6, 0, DateTimeKind.Utc)
        });
        dbContext.TradingPlanEvents.Add(new TradingPlanEvent
        {
            PlanId = plan.Id,
            Symbol = plan.Symbol,
            EventType = TradingPlanEventType.NewsReviewed,
            Severity = TradingPlanEventSeverity.Info,
            Message = "already reviewed",
            MetadataJson = "{\"localNewsId\":18}",
            OccurredAt = new DateTime(2026, 3, 16, 2, 7, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var service = new TradingPlanReviewService(
            dbContext,
            new StubLlmService("{\"isPlanThreatened\":false,\"reason\":\"safe\",\"confidence\":50}"),
            Options.Create(new TradingPlanReviewOptions()),
            NullLogger<TradingPlanReviewService>.Instance);

        var changes = await service.EvaluateAsync(new DateTimeOffset(2026, 3, 16, 2, 8, 0, TimeSpan.Zero));

        Assert.Equal(0, changes);
        Assert.Single(await dbContext.TradingPlanEvents.ToListAsync());
    }

    [Fact]
    public async Task EvaluateAsync_AcceptsDecimalConfidenceValues()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext);
        await SeedWatchlistAsync(dbContext, plan.Symbol, plan.Name);
        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Symbol = plan.Symbol,
            Name = plan.Name,
            Title = "核心客户取消大单",
            Category = "快讯",
            Source = "测试快讯",
            SourceTag = "test-news",
            PublishTime = new DateTime(2026, 3, 16, 2, 5, 0, DateTimeKind.Utc),
            CrawledAt = new DateTime(2026, 3, 16, 2, 6, 0, DateTimeKind.Utc),
            AiSentiment = "利空"
        });
        await dbContext.SaveChangesAsync();

        var service = new TradingPlanReviewService(
            dbContext,
            new StubLlmService("{" + "\"isPlanThreatened\":true,\"reason\":\"客户取消大单\",\"confidence\":88.5}"),
            Options.Create(new TradingPlanReviewOptions()),
            NullLogger<TradingPlanReviewService>.Instance);

        var changes = await service.EvaluateAsync(new DateTimeOffset(2026, 3, 16, 2, 8, 0, TimeSpan.Zero));

        Assert.Equal(1, changes);
        Assert.Equal(TradingPlanStatus.ReviewRequired, plan.Status);
        var reviewEvent = await dbContext.TradingPlanEvents.SingleAsync();
        Assert.Equal(TradingPlanEventType.ReviewRequired, reviewEvent.EventType);
        Assert.Equal("客户取消大单", reviewEvent.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_AcceptsStringConfidenceValues()
    {
        await using var dbContext = CreateDbContext();
        var plan = await SeedPlanAsync(dbContext);
        await SeedWatchlistAsync(dbContext, plan.Symbol, plan.Name);
        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Symbol = plan.Symbol,
            Name = plan.Name,
            Title = "行业龙头澄清合作终止",
            Category = "快讯",
            Source = "测试快讯",
            SourceTag = "test-news",
            PublishTime = new DateTime(2026, 3, 16, 2, 5, 0, DateTimeKind.Utc),
            CrawledAt = new DateTime(2026, 3, 16, 2, 6, 0, DateTimeKind.Utc),
            AiSentiment = "利空"
        });
        await dbContext.SaveChangesAsync();

        var service = new TradingPlanReviewService(
            dbContext,
            new StubLlmService("{" + "\"isPlanThreatened\":true,\"reason\":\"合作终止破坏催化预期\",\"confidence\":\"90.2\"}"),
            Options.Create(new TradingPlanReviewOptions()),
            NullLogger<TradingPlanReviewService>.Instance);

        var changes = await service.EvaluateAsync(new DateTimeOffset(2026, 3, 16, 2, 8, 0, TimeSpan.Zero));

        Assert.Equal(1, changes);
        Assert.Equal(TradingPlanStatus.ReviewRequired, plan.Status);
        var reviewEvent = await dbContext.TradingPlanEvents.SingleAsync();
        Assert.Equal(TradingPlanEventType.ReviewRequired, reviewEvent.EventType);
        Assert.Equal("合作终止破坏催化预期", reviewEvent.Reason);
    }

    private static async Task<TradingPlan> SeedPlanAsync(AppDbContext dbContext)
    {
        var plan = new TradingPlan
        {
            Symbol = "sz000021",
            Name = "深科技",
            Direction = TradingPlanDirection.Long,
            Status = TradingPlanStatus.Pending,
            TriggerPrice = 12.6m,
            InvalidPrice = 11.9m,
            AnalysisHistoryId = 7,
            AnalysisSummary = "等待景气改善",
            InvalidConditions = "基本面恶化或关键支撑跌破",
            SourceAgent = "commander",
            CreatedAt = new DateTime(2026, 3, 16, 1, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 3, 16, 1, 0, 0, DateTimeKind.Utc)
        };
        dbContext.TradingPlans.Add(plan);
        await dbContext.SaveChangesAsync();
        return plan;
    }

    private static async Task SeedWatchlistAsync(AppDbContext dbContext, string symbol, string name)
    {
        dbContext.ActiveWatchlists.Add(new ActiveWatchlist
        {
            Symbol = symbol,
            Name = name,
            SourceTag = "trading-plan",
            Note = "plan:test",
            IsEnabled = true,
            CreatedAt = new DateTime(2026, 3, 16, 1, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 3, 16, 1, 0, 0, DateTimeKind.Utc)
        });
        dbContext.StockQuoteSnapshots.Add(new StockQuoteSnapshot
        {
            Symbol = symbol,
            Name = name,
            Price = 12.2m,
            Change = 0.1m,
            ChangePercent = 0.8m,
            PeRatio = 0m,
            FloatMarketCap = 0m,
            VolumeRatio = 0m,
            Timestamp = new DateTime(2026, 3, 16, 2, 7, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }

    private sealed class StubLlmService : ILlmService
    {
        private readonly string _content;
        private readonly Exception? _exception;

        public StubLlmService(string content = "{}", Exception? exception = null)
        {
            _content = content;
            _exception = exception;
        }

        public Task<LlmChatResult> ChatAsync(string provider, LlmChatRequest request, CancellationToken cancellationToken = default)
        {
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(new LlmChatResult(_content));
        }
    }
}