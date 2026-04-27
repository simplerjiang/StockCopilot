using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using System.Text.Json;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class LocalFactAiEnrichmentServiceTests
{
    [Fact]
    public async Task ProcessSymbolPendingAsync_ShouldBatchApplyAiFieldsAndMarkProcessed()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Symbol = "sh600000",
            Name = "浦发银行",
            SectorName = "银行",
            Title = "Bank stocks rise after policy support",
            Category = "company_news",
            Source = "WSJ US Business",
            SourceTag = "wsj-us-business-rss",
            PublishTime = new DateTime(2026, 3, 13, 8, 0, 0),
            CrawledAt = new DateTime(2026, 3, 13, 8, 5, 0),
            Url = "https://example.com/a"
        });
        await dbContext.SaveChangesAsync();

        var service = new LocalFactAiEnrichmentService(
            dbContext,
            new StubLlmService("""
            [
              {
                "id": "stock:1",
                "translatedTitle": "政策支持后银行股走强",
                "aiSentiment": "利好",
                "aiTarget": "板块:银行",
                "aiTags": ["政策红利", "资金面"]
              }
            ]
            """),
            new StubSettingsStore(),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        await service.ProcessSymbolPendingAsync("sh600000");

        var stored = await dbContext.LocalStockNews.SingleAsync();
        Assert.True(stored.IsAiProcessed);
        Assert.Equal("政策支持后银行股走强", stored.TranslatedTitle);
        Assert.Equal("利好", stored.AiSentiment);
        Assert.Equal("板块:银行", stored.AiTarget);
        var tags = JsonSerializer.Deserialize<string[]>(stored.AiTags ?? "[]") ?? [];
        Assert.Contains("政策红利", tags);
    }

    [Fact]
    public async Task ProcessSymbolPendingAsync_ShouldBackfillAssociatedCompanyWhenModelReturnsNoClearTarget()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Symbol = "sh600519",
            Name = "贵州茅台",
            SectorName = "白酒",
            Title = "贵州茅台2025年年度报告",
            Category = "announcement",
            Source = "东方财富公告",
            SourceTag = "eastmoney-announcement",
            PublishTime = new DateTime(2026, 4, 24, 11, 49, 32, DateTimeKind.Utc),
            CrawledAt = new DateTime(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var itemId = await dbContext.LocalStockNews.Select(item => item.Id).SingleAsync();
        var payload = JsonSerializer.Serialize(new[]
        {
            new
            {
                id = $"stock:{itemId}",
                translatedTitle = (string?)null,
                aiSentiment = "中性",
                aiTarget = "无明确标的",
                aiTags = Array.Empty<string>()
            }
        });
        var service = CreateService(dbContext, payload);

        await service.ProcessSymbolPendingAsync("sh600519");

        var stored = await dbContext.LocalStockNews.SingleAsync();
        Assert.Equal("贵州茅台", stored.AiTarget);
        Assert.True(stored.IsAiProcessed);
    }

    [Fact]
    public async Task ProcessSymbolPendingAsync_ShouldRejectMismatchedTargetAndPreferAssociatedCompany()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Symbol = "sh09969",
            Name = "诺诚健华",
            SectorName = "创新药",
            Title = "诺诚健华发布新药临床进展公告",
            Category = "company_news",
            Source = "测试源",
            SourceTag = "stock-test",
            PublishTime = new DateTime(2026, 4, 24, 10, 0, 0, DateTimeKind.Utc),
            CrawledAt = new DateTime(2026, 4, 24, 10, 1, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var itemId = await dbContext.LocalStockNews.Select(item => item.Id).SingleAsync();
        var payload = JsonSerializer.Serialize(new[]
        {
            new
            {
                id = $"stock:{itemId}",
                translatedTitle = (string?)null,
                aiSentiment = "中性",
                aiTarget = "中国电力设备",
                aiTags = Array.Empty<string>()
            }
        });
        var service = CreateService(dbContext, payload);

        await service.ProcessSymbolPendingAsync("sh09969");

        var stored = await dbContext.LocalStockNews.SingleAsync();
        Assert.Equal("诺诚健华", stored.AiTarget);
        Assert.NotEqual("中国电力设备", stored.AiTarget);
    }

    [Fact]
    public async Task ProcessSymbolPendingAsync_ShouldDropMismatchedEntityTagsAndKeepGenericTags()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Symbol = "sh600519",
            Name = "贵州茅台",
            SectorName = "酿酒行业",
            Title = "贵州茅台：2026年第一季度主要经营数据公告",
            Category = "announcement",
            Source = "东方财富公告",
            SourceTag = "eastmoney-announcement",
            PublishTime = new DateTime(2026, 4, 24, 11, 49, 32, DateTimeKind.Utc),
            CrawledAt = new DateTime(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var itemId = await dbContext.LocalStockNews.Select(item => item.Id).SingleAsync();
        var payload = JsonSerializer.Serialize(new[]
        {
            new
            {
                id = $"stock:{itemId}",
                translatedTitle = (string?)null,
                aiSentiment = "中性",
                aiTarget = "中国石化",
                aiTags = new[] { "中国石化", "行业预期", "经营数据" }
            }
        });
        var service = CreateService(dbContext, payload);

        await service.ProcessSymbolPendingAsync("sh600519");

        var stored = await dbContext.LocalStockNews.SingleAsync();
        Assert.Equal("贵州茅台", stored.AiTarget);
        var tags = JsonSerializer.Deserialize<string[]>(stored.AiTags ?? "[]") ?? [];
        Assert.DoesNotContain("中国石化", tags);
        Assert.Contains("行业预期", tags);
        Assert.Contains("经营数据", tags);
    }

    [Fact]
    public async Task ProcessMarketPendingAsync_ShouldDropIndustryTargetWhenNoEvidenceMentionsIt()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalSectorReports.Add(new LocalSectorReport
        {
            Level = "market",
            SectorName = "大盘环境",
            Title = "A股震荡收跌，市场情绪回落",
            ArticleSummary = "指数午后走弱，成交额小幅放大。",
            Source = "测试源",
            SourceTag = "market-test",
            PublishTime = new DateTime(2026, 4, 24, 9, 0, 0, DateTimeKind.Utc),
            CrawledAt = new DateTime(2026, 4, 24, 9, 1, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var itemId = await dbContext.LocalSectorReports.Select(item => item.Id).SingleAsync();
        var payload = JsonSerializer.Serialize(new[]
        {
            new
            {
                id = $"market:{itemId}",
                translatedTitle = (string?)null,
                aiSentiment = "中性",
                aiTarget = "半导体",
                aiTags = Array.Empty<string>()
            }
        });
        var service = CreateService(dbContext, payload);

        await service.ProcessMarketPendingAsync();

        var stored = await dbContext.LocalSectorReports.SingleAsync();
        Assert.Null(stored.AiTarget);
    }

    [Fact]
    public async Task ProcessMarketPendingAsync_ShouldKeepTargetWhenEvidenceMentionsIt()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalSectorReports.Add(new LocalSectorReport
        {
            Level = "market",
            SectorName = "大盘环境",
            Title = "低空经济概念活跃，产业链公司集体走强",
            ArticleSummary = "低空经济政策预期升温，相关标的成交放量。",
            Source = "测试源",
            SourceTag = "market-test",
            PublishTime = new DateTime(2026, 4, 24, 9, 30, 0, DateTimeKind.Utc),
            CrawledAt = new DateTime(2026, 4, 24, 9, 31, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        var itemId = await dbContext.LocalSectorReports.Select(item => item.Id).SingleAsync();
        var payload = JsonSerializer.Serialize(new[]
        {
            new
            {
                id = $"market:{itemId}",
                translatedTitle = (string?)null,
                aiSentiment = "利好",
                aiTarget = "低空经济",
                aiTags = Array.Empty<string>()
            }
        });
        var service = CreateService(dbContext, payload);

        await service.ProcessMarketPendingAsync();

        var stored = await dbContext.LocalSectorReports.SingleAsync();
        Assert.Equal("低空经济", stored.AiTarget);
    }

    [Fact]
    public async Task ArchiveJobCoordinator_ShouldPauseImmediatelyDuringRetryDelay()
    {
        var aiService = new ControlledArchiveJobAiEnrichmentService(cancellationToken =>
            Task.FromResult(new LocalFactPendingProcessSummary(
                new LocalFactPendingCounts(0, 0, 0),
                new LocalFactPendingCounts(1, 0, 0),
                false,
                "本轮批量清洗未取得进展，剩余待处理项保持未完成状态。",
                new LocalFactPendingContinuation(false, "no_progress"))));
        var scopeFactory = new ControlledArchiveJobScopeFactory(() => aiService);
        var coordinator = new LocalFactArchiveJobCoordinator(
            scopeFactory,
            NullLogger<LocalFactArchiveJobCoordinator>.Instance);

        await coordinator.StartOrResumeAsync();

        await WaitUntilAsync(() =>
            coordinator.GetStatus().ConsecutiveRecoverableFailures == 1
            && coordinator.GetStatus().Message?.Contains("自动重试", StringComparison.Ordinal) == true);

        var pausingStatus = await coordinator.PauseAsync();

        Assert.Equal("running", pausingStatus.State);
        Assert.True(pausingStatus.IsRunning);

        await WaitUntilAsync(
            () => string.Equals(coordinator.GetStatus().State, "paused", StringComparison.OrdinalIgnoreCase),
            timeoutMs: 1000);

        var pausedStatus = coordinator.GetStatus();
        Assert.Equal("paused", pausedStatus.State);
        Assert.False(pausedStatus.IsRunning);
        Assert.True(pausedStatus.RequiresManualResume);
        Assert.Equal("后台清洗已暂停。", pausedStatus.Message);
        Assert.Equal("已在下一轮开始前暂停后台清洗任务。", pausedStatus.AttentionMessage);
        Assert.Equal(1, pausedStatus.Rounds);
        Assert.Equal(1, pausedStatus.Remaining.Market);
        Assert.Equal(1, aiService.ProcessPendingBatchCalls);
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_ShouldCoverAllScopesAndExposeRemainingUntilDrained()
    {
        await using var dbContext = CreateDbContext();
        var publishBase = new DateTime(2026, 3, 13, 7, 0, 0);

        for (var index = 0; index < 13; index++)
        {
            dbContext.LocalSectorReports.Add(new LocalSectorReport
            {
                Level = "market",
                SectorName = "大盘环境",
                Title = $"Market item {index}",
                Source = "Reuters",
                SourceTag = "gnews-reuters",
                PublishTime = publishBase.AddMinutes(index),
                CrawledAt = publishBase.AddMinutes(index).AddSeconds(30),
                Url = $"https://example.com/market/{index}"
            });
        }

        dbContext.LocalSectorReports.Add(new LocalSectorReport
        {
            Symbol = "sh600000",
            SectorName = "银行",
            Level = "sector",
            Title = "Bank sector momentum improves",
            Source = "Reuters",
            SourceTag = "gnews-reuters",
            PublishTime = publishBase.AddHours(4),
            CrawledAt = publishBase.AddHours(4).AddMinutes(2),
            Url = "https://example.com/sector"
        });

        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Symbol = "sh600000",
            Name = "浦发银行",
            SectorName = "银行",
            Title = "Stock-specific catalyst",
            Category = "company_news",
            Source = "Reuters",
            SourceTag = "gnews-reuters",
            PublishTime = publishBase.AddHours(5),
            CrawledAt = publishBase.AddHours(5).AddMinutes(2),
            Url = "https://example.com/stock"
        });
        await dbContext.SaveChangesAsync();

        var service = new LocalFactAiEnrichmentService(
            dbContext,
            new EchoPromptLlmService(),
            new StubSettingsStore(),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        var firstRun = await service.ProcessPendingBatchAsync();

        Assert.False(firstRun.Completed);
        Assert.Equal(12, firstRun.Processed.Market);
        Assert.Equal(1, firstRun.Processed.Sector);
        Assert.Equal(1, firstRun.Processed.Stock);
        Assert.Equal(1, firstRun.Remaining.Market);
        Assert.Equal(0, firstRun.Remaining.Sector);
        Assert.Equal(0, firstRun.Remaining.Stock);
        Assert.Equal("本轮已达到单次清洗上限（每个层级最多 12 条），已保存部分结果。", firstRun.StopReason);
        Assert.NotNull(firstRun.Continuation);
        Assert.False(firstRun.Continuation!.MayContinueAutomatically);
        Assert.Equal("round_budget_reached", firstRun.Continuation.ReasonCode);

        var secondRun = await service.ProcessPendingBatchAsync();

        Assert.True(secondRun.Completed);
        Assert.Equal(1, secondRun.Processed.Market);
        Assert.Equal(0, secondRun.Processed.Sector);
        Assert.Equal(0, secondRun.Processed.Stock);
        Assert.Equal(0, secondRun.Remaining.Total);
        Assert.Null(secondRun.StopReason);
        Assert.NotNull(secondRun.Continuation);
        Assert.False(secondRun.Continuation!.MayContinueAutomatically);
        Assert.Equal("completed", secondRun.Continuation.ReasonCode);
        Assert.Equal(0, await dbContext.LocalSectorReports.CountAsync(item => !item.IsAiProcessed));
        Assert.Equal(0, await dbContext.LocalStockNews.CountAsync(item => !item.IsAiProcessed));
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_ShouldBoundArchiveSweepToOneConfiguredBatchPerScope()
    {
        await using var dbContext = CreateDbContext();
        var publishBase = new DateTime(2026, 3, 13, 6, 0, 0);

        for (var index = 0; index < 13; index++)
        {
            dbContext.LocalSectorReports.Add(new LocalSectorReport
            {
                Level = "market",
                SectorName = "大盘环境",
                Title = $"Market item {index}",
                Source = "Reuters",
                SourceTag = "gnews-reuters",
                PublishTime = publishBase.AddMinutes(index),
                CrawledAt = publishBase.AddMinutes(index).AddSeconds(30),
                Url = $"https://example.com/market/{index}"
            });

            dbContext.LocalStockNews.Add(new LocalStockNews
            {
                Symbol = "sh600000",
                Name = "浦发银行",
                SectorName = "银行",
                Title = $"Stock item {index}",
                Category = "company_news",
                Source = "Reuters",
                SourceTag = "gnews-reuters",
                PublishTime = publishBase.AddHours(1).AddMinutes(index),
                CrawledAt = publishBase.AddHours(1).AddMinutes(index).AddSeconds(30),
                Url = $"https://example.com/stock/{index}"
            });
        }

        await dbContext.SaveChangesAsync();

        var llmService = new CapturingEchoPromptLlmService();
        var service = new LocalFactAiEnrichmentService(
            dbContext,
            llmService,
            new StubSettingsStore(newsCleansingSettings: ("active", "", 5)),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        var summary = await service.ProcessPendingBatchAsync();

        Assert.False(summary.Completed);
        Assert.Equal(5, summary.Processed.Market);
        Assert.Equal(0, summary.Processed.Sector);
        Assert.Equal(5, summary.Processed.Stock);
        Assert.Equal(8, summary.Remaining.Market);
        Assert.Equal(8, summary.Remaining.Stock);
        Assert.Equal("本轮已达到单次清洗上限（每个层级最多 5 条），已保存部分结果。", summary.StopReason);
        Assert.NotNull(summary.Continuation);
        Assert.False(summary.Continuation!.MayContinueAutomatically);
        Assert.Equal("round_budget_reached", summary.Continuation.ReasonCode);
        Assert.Equal(2, llmService.CallCount);
        Assert.All(llmService.BatchSizes, size => Assert.InRange(size, 1, 5));
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_ShouldRespectConfiguredOllamaBatchSizeForOllama()
    {
        await using var dbContext = CreateDbContext();
        var publishBase = new DateTime(2026, 3, 13, 6, 30, 0);

        for (var index = 0; index < 10; index++)
        {
            dbContext.LocalSectorReports.Add(new LocalSectorReport
            {
                Level = "market",
                SectorName = "大盘环境",
                Title = $"Market item {index}",
                Source = "Reuters",
                SourceTag = "gnews-reuters",
                PublishTime = publishBase.AddMinutes(index),
                CrawledAt = publishBase.AddMinutes(index).AddSeconds(30),
                Url = $"https://example.com/market/{index}"
            });

            dbContext.LocalSectorReports.Add(new LocalSectorReport
            {
                Symbol = "sh600000",
                SectorName = "银行",
                Level = "sector",
                Title = $"Sector item {index}",
                Source = "Reuters",
                SourceTag = "gnews-reuters",
                PublishTime = publishBase.AddHours(1).AddMinutes(index),
                CrawledAt = publishBase.AddHours(1).AddMinutes(index).AddSeconds(30),
                Url = $"https://example.com/sector/{index}"
            });

            dbContext.LocalStockNews.Add(new LocalStockNews
            {
                Symbol = "sh600000",
                Name = "浦发银行",
                SectorName = "银行",
                Title = $"Stock item {index}",
                Category = "company_news",
                Source = "Reuters",
                SourceTag = "gnews-reuters",
                PublishTime = publishBase.AddHours(2).AddMinutes(index),
                CrawledAt = publishBase.AddHours(2).AddMinutes(index).AddSeconds(30),
                Url = $"https://example.com/stock/{index}"
            });
        }

        await dbContext.SaveChangesAsync();

        var llmService = new CapturingEchoPromptLlmService();
        var service = new LocalFactAiEnrichmentService(
            dbContext,
            llmService,
            new StubSettingsStore(
                newsCleansingSettings: ("ollama", "", 20),
                providers:
                [
                    new LlmProviderSettings
                    {
                        Provider = "ollama",
                        ProviderType = "ollama",
                        Model = "gemma4:e4b",
                        Enabled = true
                    }
                ]),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        var summary = await service.ProcessPendingBatchAsync();

        Assert.False(summary.Completed);
        Assert.Equal(20, summary.Processed.Total);
        Assert.Equal(7, summary.Processed.Market);
        Assert.Equal(7, summary.Processed.Sector);
        Assert.Equal(6, summary.Processed.Stock);
        Assert.Equal(3, summary.Remaining.Market);
        Assert.Equal(3, summary.Remaining.Sector);
        Assert.Equal(4, summary.Remaining.Stock);
        Assert.Equal("本轮已达到单次清洗上限（最多 20 条），已保存部分结果。", summary.StopReason);
        Assert.NotNull(summary.Continuation);
        Assert.False(summary.Continuation!.MayContinueAutomatically);
        Assert.Equal("round_budget_reached", summary.Continuation.ReasonCode);
        Assert.Equal(1, llmService.CallCount);
        Assert.Equal([20], llmService.BatchSizes);
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_ShouldLetSingleBusyScopeFillConfiguredOllamaBatch()
    {
        await using var dbContext = CreateDbContext();
        var publishBase = new DateTime(2026, 3, 13, 9, 0, 0);

        for (var index = 0; index < 25; index++)
        {
            dbContext.LocalStockNews.Add(new LocalStockNews
            {
                Symbol = "sh600000",
                Name = "浦发银行",
                SectorName = "银行",
                Title = $"Stock-only item {index}",
                Category = "company_news",
                Source = "Reuters",
                SourceTag = "gnews-reuters",
                PublishTime = publishBase.AddMinutes(index),
                CrawledAt = publishBase.AddMinutes(index).AddSeconds(30),
                Url = $"https://example.com/stock-only/{index}"
            });
        }

        await dbContext.SaveChangesAsync();

        var llmService = new CapturingEchoPromptLlmService();
        var service = new LocalFactAiEnrichmentService(
            dbContext,
            llmService,
            new StubSettingsStore(
                newsCleansingSettings: ("ollama", "", 20),
                providers:
                [
                    new LlmProviderSettings
                    {
                        Provider = "ollama",
                        ProviderType = "ollama",
                        Model = "gemma4:e4b",
                        Enabled = true
                    }
                ]),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        var summary = await service.ProcessPendingBatchAsync();

        Assert.False(summary.Completed);
        Assert.Equal(0, summary.Processed.Market);
        Assert.Equal(0, summary.Processed.Sector);
        Assert.Equal(20, summary.Processed.Stock);
        Assert.Equal(5, summary.Remaining.Stock);
        Assert.Equal("本轮已达到单次清洗上限（最多 20 条），已保存部分结果。", summary.StopReason);
        Assert.NotNull(summary.Continuation);
        Assert.False(summary.Continuation!.MayContinueAutomatically);
        Assert.Equal("round_budget_reached", summary.Continuation.ReasonCode);
        Assert.Equal(1, llmService.CallCount);
        Assert.Equal([20], llmService.BatchSizes);
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_ShouldExposeNoProgressStopReason_WhenArchiveSweepAppliesNothing()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalSectorReports.Add(new LocalSectorReport
        {
            Level = "market",
            SectorName = "大盘环境",
            Title = "No progress item",
            Source = "Reuters",
            SourceTag = "gnews-reuters",
            PublishTime = new DateTime(2026, 3, 13, 8, 30, 0),
            CrawledAt = new DateTime(2026, 3, 13, 8, 31, 0),
            Url = "https://example.com/no-progress"
        });
        await dbContext.SaveChangesAsync();

        var service = new LocalFactAiEnrichmentService(
            dbContext,
            new StubLlmService("[]"),
            new StubSettingsStore(),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        var summary = await service.ProcessPendingBatchAsync();

        Assert.False(summary.Completed);
        Assert.Equal(0, summary.Processed.Total);
        Assert.Equal(1, summary.Remaining.Market);
        Assert.Equal(0, summary.Remaining.Sector);
        Assert.Equal(0, summary.Remaining.Stock);
        Assert.Equal("本轮批量清洗未取得进展，剩余待处理项保持未完成状态。", summary.StopReason);
        Assert.NotNull(summary.Continuation);
        Assert.False(summary.Continuation!.MayContinueAutomatically);
        Assert.Equal("no_progress", summary.Continuation.ReasonCode);
        Assert.False((await dbContext.LocalSectorReports.SingleAsync()).IsAiProcessed);
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_ShouldExplainEmptyModelResponse_WhenBatchResponseIsBlank()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalSectorReports.Add(new LocalSectorReport
        {
            Level = "market",
            SectorName = "大盘环境",
            Title = "Blank response item",
            Source = "Reuters",
            SourceTag = "gnews-reuters",
            PublishTime = new DateTime(2026, 3, 13, 8, 35, 0),
            CrawledAt = new DateTime(2026, 3, 13, 8, 36, 0),
            Url = "https://example.com/blank-response"
        });
        await dbContext.SaveChangesAsync();

        var service = new LocalFactAiEnrichmentService(
            dbContext,
            new StubLlmService("   "),
            new StubSettingsStore(),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        var summary = await service.ProcessPendingBatchAsync();

        Assert.False(summary.Completed);
        Assert.Equal(0, summary.Processed.Total);
        Assert.Equal(1, summary.Remaining.Market);
        Assert.Equal("本轮批量清洗未取得进展：模型返回 null 或空内容，待处理资讯已保留。", summary.StopReason);
        Assert.Contains(summary.Events, item =>
            item.Level == "warning"
            && item.Type == "parse"
            && item.Message == "模型返回 null 或空内容，已跳过该批次。"
            && item.Details == "原始返回为空。");
        Assert.False((await dbContext.LocalSectorReports.SingleAsync()).IsAiProcessed);
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_ShouldParseMarkdownFencedBatchResponse()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalSectorReports.Add(new LocalSectorReport
        {
            Level = "market",
            SectorName = "大盘环境",
            Title = "Fenced item",
            Source = "Reuters",
            SourceTag = "gnews-reuters",
            PublishTime = new DateTime(2026, 3, 13, 8, 40, 0),
            CrawledAt = new DateTime(2026, 3, 13, 8, 41, 0),
            Url = "https://example.com/fenced"
        });
        await dbContext.SaveChangesAsync();

        var itemId = await dbContext.LocalSectorReports.Select(item => item.Id).SingleAsync();
        var payload = JsonSerializer.Serialize(new[]
        {
            new
            {
                id = $"market:{itemId}",
                translatedTitle = "已清洗:fenced",
                aiSentiment = "利好",
                aiTarget = "大盘",
                aiTags = new[] { "宏观货币" }
            }
        });

        var service = new LocalFactAiEnrichmentService(
            dbContext,
            new StubLlmService("模型整理如下：\n```json\n" + payload + "\n```"),
            new StubSettingsStore(),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        var summary = await service.ProcessPendingBatchAsync();
        var stored = await dbContext.LocalSectorReports.SingleAsync();

        Assert.True(summary.Completed);
        Assert.Equal(1, summary.Processed.Market);
        Assert.Equal(0, summary.Remaining.Total);
        Assert.True(stored.IsAiProcessed);
        Assert.Equal("已清洗:fenced", stored.TranslatedTitle);
        Assert.Equal("利好", stored.AiSentiment);
        Assert.Equal("大盘", stored.AiTarget);
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_ShouldParseBatchResponseWithTrailingExplanation()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalSectorReports.Add(new LocalSectorReport
        {
            Level = "market",
            SectorName = "大盘环境",
            Title = "Trailing explanation item",
            Source = "Reuters",
            SourceTag = "gnews-reuters",
            PublishTime = new DateTime(2026, 3, 13, 8, 50, 0),
            CrawledAt = new DateTime(2026, 3, 13, 8, 51, 0),
            Url = "https://example.com/trailing"
        });
        await dbContext.SaveChangesAsync();

        var itemId = await dbContext.LocalSectorReports.Select(item => item.Id).SingleAsync();
        var payload = JsonSerializer.Serialize(new[]
        {
            new
            {
                id = $"market:{itemId}",
                translatedTitle = "已清洗:trailing",
                aiSentiment = "中性",
                aiTarget = "无明确靶点",
                aiTags = Array.Empty<string>()
            }
        });

        var service = new LocalFactAiEnrichmentService(
            dbContext,
            new StubLlmService(payload + "\n以上为本轮整理结果，仅供校验。"),
            new StubSettingsStore(),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        var summary = await service.ProcessPendingBatchAsync();
        var stored = await dbContext.LocalSectorReports.SingleAsync();

        Assert.True(summary.Completed);
        Assert.Equal(1, summary.Processed.Market);
        Assert.Equal(0, summary.Remaining.Total);
        Assert.True(stored.IsAiProcessed);
        Assert.Equal("已清洗:trailing", stored.TranslatedTitle);
        Assert.Equal("中性", stored.AiSentiment);
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_ShouldSalvageFirstValidItemFromTruncatedBatchResponse()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalSectorReports.AddRange(
            new LocalSectorReport
            {
                Level = "market",
                SectorName = "大盘环境",
                Title = "Salvage item 1",
                Source = "Reuters",
                SourceTag = "gnews-reuters",
                PublishTime = new DateTime(2026, 3, 13, 9, 0, 0),
                CrawledAt = new DateTime(2026, 3, 13, 9, 1, 0),
                Url = "https://example.com/salvage/1"
            },
            new LocalSectorReport
            {
                Level = "market",
                SectorName = "大盘环境",
                Title = "Salvage item 2",
                Source = "Reuters",
                SourceTag = "gnews-reuters",
                PublishTime = new DateTime(2026, 3, 13, 9, 2, 0),
                CrawledAt = new DateTime(2026, 3, 13, 9, 3, 0),
                Url = "https://example.com/salvage/2"
            });
        await dbContext.SaveChangesAsync();

        var itemIds = await dbContext.LocalSectorReports
            .OrderBy(item => item.PublishTime)
            .Select(item => item.Id)
            .ToArrayAsync();
        var firstObject = JsonSerializer.Serialize(new
        {
            id = $"market:{itemIds[0]}",
            translatedTitle = "已清洗:salvaged-first",
            aiSentiment = "中性",
            aiTarget = "大盘",
            aiTags = new[] { "宏观货币" }
        });
        var malformedResponse = "[\n" + firstObject + ",\n{" +
            "\"id\":\"market:" + itemIds[1] + "\",\"translatedTitle\":\"坏掉的第二条\"";

        var service = new LocalFactAiEnrichmentService(
            dbContext,
            new StubLlmService(malformedResponse),
            new StubSettingsStore(),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        var summary = await service.ProcessPendingBatchAsync();
        var stored = await dbContext.LocalSectorReports.OrderBy(item => item.PublishTime).ToArrayAsync();

        Assert.False(summary.Completed);
        Assert.Equal(1, summary.Processed.Market);
        Assert.Equal(1, summary.Remaining.Market);
        Assert.Equal("本轮已保存部分结果，仍有待处理资讯。", summary.StopReason);
        Assert.NotNull(summary.Continuation);
        Assert.True(summary.Continuation!.MayContinueAutomatically);
        Assert.Equal("remaining_pending", summary.Continuation.ReasonCode);
        Assert.True(stored[0].IsAiProcessed);
        Assert.Equal("已清洗:salvaged-first", stored[0].TranslatedTitle);
        Assert.False(stored[1].IsAiProcessed);
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_ShouldExplainNonArrayBatchResponse_WhenBatchResponseIsObject()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalSectorReports.Add(new LocalSectorReport
        {
            Level = "market",
            SectorName = "大盘环境",
            Title = "Non array response item",
            Source = "Reuters",
            SourceTag = "gnews-reuters",
            PublishTime = new DateTime(2026, 3, 13, 9, 5, 0),
            CrawledAt = new DateTime(2026, 3, 13, 9, 6, 0),
            Url = "https://example.com/non-array"
        });
        await dbContext.SaveChangesAsync();

        var itemId = await dbContext.LocalSectorReports.Select(item => item.Id).SingleAsync();
        var invalidResponse = "{\"id\":\"market:" + itemId + "\",\"translatedTitle\":\"不是数组\"}";

        var service = new LocalFactAiEnrichmentService(
            dbContext,
            new StubLlmService(invalidResponse),
            new StubSettingsStore(),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        var summary = await service.ProcessPendingBatchAsync();

        Assert.False(summary.Completed);
        Assert.Equal(0, summary.Processed.Total);
        Assert.Equal(1, summary.Remaining.Market);
        Assert.Equal("本轮批量清洗未取得进展：模型返回内容不是可解析 JSON 数组，待处理资讯已保留。", summary.StopReason);
        Assert.Contains(summary.Events, item =>
            item.Level == "warning"
            && item.Type == "parse"
            && item.Message == "模型返回内容不是可解析 JSON 数组，已跳过该批次。");
        Assert.False((await dbContext.LocalSectorReports.SingleAsync()).IsAiProcessed);
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_ShouldEmitParseWarningAndKeepPending_WhenBatchResponseIsInvalid()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalSectorReports.Add(new LocalSectorReport
        {
            Level = "market",
            SectorName = "大盘环境",
            Title = "Invalid parse item",
            Source = "Reuters",
            SourceTag = "gnews-reuters",
            PublishTime = new DateTime(2026, 3, 13, 9, 10, 0),
            CrawledAt = new DateTime(2026, 3, 13, 9, 11, 0),
            Url = "https://example.com/invalid"
        });
        await dbContext.SaveChangesAsync();

        var itemId = await dbContext.LocalSectorReports.Select(item => item.Id).SingleAsync();
        var invalidResponse = "```json\n[{\"id\":\"market:" + itemId + "\",\"translatedTitle\":\"坏掉";

        var service = new LocalFactAiEnrichmentService(
            dbContext,
            new StubLlmService(invalidResponse),
            new StubSettingsStore(),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        var summary = await service.ProcessPendingBatchAsync();

        Assert.False(summary.Completed);
        Assert.Equal(0, summary.Processed.Total);
        Assert.Equal(1, summary.Remaining.Market);
        Assert.Equal("本轮批量清洗未取得进展：模型返回 JSON 解析失败，待处理资讯已保留。", summary.StopReason);
        Assert.NotNull(summary.Continuation);
        Assert.False(summary.Continuation!.MayContinueAutomatically);
        Assert.Equal("no_progress", summary.Continuation.ReasonCode);
        Assert.Contains(summary.Events, item =>
            item.Level == "warning"
            && item.Type == "parse"
            && item.Message == "模型返回 JSON 解析失败，已跳过该批次。");
        Assert.False((await dbContext.LocalSectorReports.SingleAsync()).IsAiProcessed);
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_ShouldKeepPerRequestProcessedCounts_WhenNewPendingArrivesDuringSweep()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var root = new InMemoryDatabaseRoot();
        var publishBase = new DateTime(2026, 3, 13, 10, 0, 0);

        await using (var seedContext = CreateDbContext(databaseName, root))
        {
            seedContext.LocalSectorReports.Add(new LocalSectorReport
            {
                Level = "market",
                SectorName = "大盘环境",
                Title = "Initial market item",
                Source = "Reuters",
                SourceTag = "gnews-reuters",
                PublishTime = publishBase,
                CrawledAt = publishBase.AddMinutes(1),
                Url = "https://example.com/market/initial"
            });
            await seedContext.SaveChangesAsync();
        }

        await using var serviceContext = CreateDbContext(databaseName, root);
        var initialId = await serviceContext.LocalSectorReports.Select(item => item.Id).SingleAsync();
        var service = new LocalFactAiEnrichmentService(
            serviceContext,
            new AppendPendingDuringSweepLlmService(databaseName, root, initialId, publishBase.AddHours(1)),
            new StubSettingsStore(),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        var result = await service.ProcessPendingBatchAsync();

        Assert.False(result.Completed);
        Assert.Equal(1, result.Processed.Market);
        Assert.Equal(0, result.Processed.Sector);
        Assert.Equal(0, result.Processed.Stock);
        Assert.Equal(1, result.Remaining.Market);
        Assert.Equal("本轮已保存部分结果，仍有待处理资讯。", result.StopReason);
        Assert.NotNull(result.Continuation);
        Assert.True(result.Continuation!.MayContinueAutomatically);
        Assert.Equal("remaining_pending", result.Continuation.ReasonCode);
        Assert.Equal(1, await serviceContext.LocalSectorReports.CountAsync(item => !item.IsAiProcessed));
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_ShouldSerializeOverlappingArchiveSweeps()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var root = new InMemoryDatabaseRoot();
        var publishBase = new DateTime(2026, 3, 13, 11, 0, 0);

        await using (var seedContext = CreateDbContext(databaseName, root))
        {
            seedContext.LocalSectorReports.Add(new LocalSectorReport
            {
                Level = "market",
                SectorName = "大盘环境",
                Title = "Serialized market item",
                Source = "Reuters",
                SourceTag = "gnews-reuters",
                PublishTime = publishBase,
                CrawledAt = publishBase.AddMinutes(1),
                Url = "https://example.com/market/serialized"
            });
            await seedContext.SaveChangesAsync();
        }

        var initialId = 0L;
        await using (var readContext = CreateDbContext(databaseName, root))
        {
            initialId = await readContext.LocalSectorReports.Select(item => item.Id).SingleAsync();
        }

        await using var firstContext = CreateDbContext(databaseName, root);
        await using var secondContext = CreateDbContext(databaseName, root);
        var blockingLlm = new BlockingLlmService($"[{{\"id\":\"market:{initialId}\",\"translatedTitle\":\"已清洗:market:{initialId}\",\"aiSentiment\":\"中性\",\"aiTarget\":\"大盘\",\"aiTags\":[]}}]");

        var firstService = new LocalFactAiEnrichmentService(
            firstContext,
            blockingLlm,
            new StubSettingsStore(),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);
        var secondService = new LocalFactAiEnrichmentService(
            secondContext,
            new StubLlmService(exception: new InvalidOperationException("Overlapping archive sweep should not reach a second LLM call.")),
            new StubSettingsStore(),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        var firstTask = firstService.ProcessPendingBatchAsync();
        await blockingLlm.WaitUntilCalledAsync();

        var secondTask = secondService.ProcessPendingBatchAsync();
        await Task.Delay(50);
        Assert.False(secondTask.IsCompleted);

        blockingLlm.Release();

        var firstResult = await firstTask;
        var secondResult = await secondTask;

        Assert.True(firstResult.Completed);
        Assert.Equal(1, firstResult.Processed.Market);
        Assert.NotNull(firstResult.Continuation);
        Assert.False(firstResult.Continuation!.MayContinueAutomatically);
        Assert.Equal("completed", firstResult.Continuation.ReasonCode);
        Assert.True(secondResult.Completed);
        Assert.Equal(0, secondResult.Processed.Total);
        Assert.Equal(0, secondResult.Remaining.Total);
        Assert.NotNull(secondResult.Continuation);
        Assert.False(secondResult.Continuation!.MayContinueAutomatically);
        Assert.Equal("completed", secondResult.Continuation.ReasonCode);

        await using var verifyContext = CreateDbContext(databaseName, root);
        Assert.Equal(0, await verifyContext.LocalSectorReports.CountAsync(item => !item.IsAiProcessed));
    }

    [Fact]
    public async Task ArchiveJobCoordinator_ShouldStartQuicklyAndExposeStatusUntilCompletion()
    {
        var firstBatch = new TaskCompletionSource<LocalFactPendingProcessSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var aiService = new ControlledArchiveJobAiEnrichmentService(
            async cancellationToken => await firstBatch.Task.WaitAsync(cancellationToken));
        var scopeFactory = new ControlledArchiveJobScopeFactory(() => aiService);
        var coordinator = new LocalFactArchiveJobCoordinator(
            scopeFactory,
            NullLogger<LocalFactArchiveJobCoordinator>.Instance);

        var startTask = coordinator.StartOrResumeAsync();
        var completed = await Task.WhenAny(startTask, Task.Delay(250));

        Assert.Same(startTask, completed);

        var startStatus = await startTask;
        Assert.Equal("running", startStatus.State);
        Assert.True(startStatus.IsRunning);
        Assert.Equal(1, startStatus.RunId);
        Assert.Equal(0, startStatus.Rounds);

        await aiService.WaitUntilCalledAsync();

        var runningStatus = coordinator.GetStatus();
        Assert.True(runningStatus.IsRunning);
        Assert.Equal(1, runningStatus.RunId);
        Assert.Equal("running", runningStatus.State);

        firstBatch.SetResult(new LocalFactPendingProcessSummary(
            new LocalFactPendingCounts(1, 0, 0),
            new LocalFactPendingCounts(0, 0, 0),
            true,
            null,
            new LocalFactPendingContinuation(false, "completed")));

        await WaitUntilAsync(() => !coordinator.GetStatus().IsRunning);

        var finalStatus = coordinator.GetStatus();
        Assert.Equal("completed", finalStatus.State);
        Assert.True(finalStatus.Completed);
        Assert.False(finalStatus.IsRunning);
        Assert.Equal(1, finalStatus.Rounds);
        Assert.Equal(1, finalStatus.Processed.Market);
        Assert.Equal(0, finalStatus.Remaining.Total);
        Assert.Equal("completed", finalStatus.Continuation?.ReasonCode);
        Assert.Equal(1, aiService.ProcessPendingBatchCalls);
        Assert.Equal(1, scopeFactory.ScopeCount);
    }

    [Fact]
    public async Task ArchiveJobCoordinator_ShouldNotStartDuplicateRunWhenJobIsAlreadyRunning()
    {
        var firstBatch = new TaskCompletionSource<LocalFactPendingProcessSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var aiService = new ControlledArchiveJobAiEnrichmentService(
            async cancellationToken => await firstBatch.Task.WaitAsync(cancellationToken));
        var scopeFactory = new ControlledArchiveJobScopeFactory(() => aiService);
        var coordinator = new LocalFactArchiveJobCoordinator(
            scopeFactory,
            NullLogger<LocalFactArchiveJobCoordinator>.Instance);

        var firstStatus = await coordinator.StartOrResumeAsync();
        await aiService.WaitUntilCalledAsync();

        var secondStatus = await coordinator.StartOrResumeAsync();

        Assert.Equal(firstStatus.RunId, secondStatus.RunId);
        Assert.True(secondStatus.IsRunning);
        Assert.Equal(1, aiService.ProcessPendingBatchCalls);
        Assert.Equal(1, scopeFactory.ScopeCount);

        firstBatch.SetResult(new LocalFactPendingProcessSummary(
            new LocalFactPendingCounts(0, 1, 0),
            new LocalFactPendingCounts(0, 0, 0),
            true,
            null,
            new LocalFactPendingContinuation(false, "completed")));

        await WaitUntilAsync(() => !coordinator.GetStatus().IsRunning);

        var finalStatus = coordinator.GetStatus();
        Assert.Equal("completed", finalStatus.State);
        Assert.Equal(1, finalStatus.RunId);
        Assert.Equal(1, finalStatus.Processed.Sector);
        Assert.Equal(1, aiService.ProcessPendingBatchCalls);
    }

    [Fact]
    public async Task ArchiveJobCoordinator_ShouldAutoRetryFirstRecoverableNoProgressRound()
    {
        var secondBatch = new TaskCompletionSource<LocalFactPendingProcessSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callIndex = 0;
        var aiService = new ControlledArchiveJobAiEnrichmentService(cancellationToken =>
        {
            callIndex += 1;
            if (callIndex == 1)
            {
                return Task.FromResult(new LocalFactPendingProcessSummary(
                    new LocalFactPendingCounts(0, 0, 0),
                    new LocalFactPendingCounts(1, 0, 0),
                    false,
                    "本轮批量清洗未取得进展，剩余待处理项保持未完成状态。",
                    new LocalFactPendingContinuation(false, "no_progress")));
            }

            return secondBatch.Task.WaitAsync(cancellationToken);
        });
        var scopeFactory = new ControlledArchiveJobScopeFactory(() => aiService);
        var coordinator = new LocalFactArchiveJobCoordinator(
            scopeFactory,
            NullLogger<LocalFactArchiveJobCoordinator>.Instance);

        await coordinator.StartOrResumeAsync();

        await WaitUntilAsync(() => aiService.ProcessPendingBatchCalls >= 2, timeoutMs: 5000);

        var retryingStatus = coordinator.GetStatus();
        Assert.Equal("running", retryingStatus.State);
        Assert.True(retryingStatus.IsRunning);
        Assert.False(retryingStatus.RequiresManualResume);
        Assert.Equal(1, retryingStatus.Rounds);
        Assert.Equal(1, retryingStatus.ConsecutiveRecoverableFailures);
        Assert.Equal("本轮批量清洗未取得进展，剩余待处理项保持未完成状态。", retryingStatus.AttentionMessage);
        Assert.Equal("后台清洗遇到可恢复问题，待处理资讯已保留，将在 500 毫秒后进行第 1 次自动重试。", retryingStatus.Message);
        Assert.Contains(retryingStatus.RecentEvents, item => item.Type == "retry");

        secondBatch.SetResult(new LocalFactPendingProcessSummary(
            new LocalFactPendingCounts(1, 0, 0),
            new LocalFactPendingCounts(0, 0, 0),
            true,
            null,
            new LocalFactPendingContinuation(false, "completed")));

        await WaitUntilAsync(() => !coordinator.GetStatus().IsRunning);

        var finalStatus = coordinator.GetStatus();
        Assert.Equal("completed", finalStatus.State);
        Assert.True(finalStatus.Completed);
        Assert.Equal(2, finalStatus.Rounds);
        Assert.Equal(1, finalStatus.Processed.Market);
        Assert.Equal(0, finalStatus.Remaining.Total);
        Assert.Equal(2, aiService.ProcessPendingBatchCalls);
        Assert.Equal(2, scopeFactory.ScopeCount);
    }

    [Fact]
    public async Task ArchiveJobCoordinator_ShouldStopAfterMaxRecoverableNoProgressRoundsAndKeepPending()
    {
        var noProgressReason = "本轮批量清洗未取得进展：模型返回 JSON 解析失败，待处理资讯已保留。";
        var aiService = new ControlledArchiveJobAiEnrichmentService(_ =>
            Task.FromResult(new LocalFactPendingProcessSummary(
                new LocalFactPendingCounts(0, 0, 0),
                new LocalFactPendingCounts(1, 1, 0),
                false,
                noProgressReason,
                new LocalFactPendingContinuation(false, "no_progress"))));
        var scopeFactory = new ControlledArchiveJobScopeFactory(() => aiService);
        var coordinator = new LocalFactArchiveJobCoordinator(
            scopeFactory,
            NullLogger<LocalFactArchiveJobCoordinator>.Instance);

        await coordinator.StartOrResumeAsync();

        await WaitUntilAsync(() => string.Equals(coordinator.GetStatus().State, "failed", StringComparison.OrdinalIgnoreCase), timeoutMs: 5000);

        var failedStatus = coordinator.GetStatus();
        Assert.Equal("failed", failedStatus.State);
        Assert.False(failedStatus.IsRunning);
        Assert.True(failedStatus.RequiresManualResume);
        Assert.Equal(3, failedStatus.Rounds);
        Assert.Equal(3, failedStatus.ConsecutiveRecoverableFailures);
        Assert.Equal("后台清洗已连续 3 轮未取得进展，待处理资讯已保留，已停止自动重试。", failedStatus.Message);
        Assert.Equal("后台清洗已连续 3 轮未取得进展，待处理资讯已保留，已停止自动重试。", failedStatus.StopReason);
        Assert.Equal(noProgressReason, failedStatus.AttentionMessage);
        Assert.Equal("retry_exhausted", failedStatus.Continuation?.ReasonCode);
        Assert.Contains(failedStatus.RecentEvents, item =>
            item.Type == "retry"
            && item.Level == "error"
            && item.Message == "后台清洗已连续 3 轮未取得进展，待处理资讯已保留，已停止自动重试。");
        Assert.Equal(3, aiService.ProcessPendingBatchCalls);
        Assert.Equal(3, scopeFactory.ScopeCount);
    }

    [Fact]
    public async Task ArchiveJobCoordinator_ShouldPauseAndResumePausedRun()
    {
        var firstBatch = new TaskCompletionSource<LocalFactPendingProcessSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumedBatch = new TaskCompletionSource<LocalFactPendingProcessSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callIndex = 0;
        var firstBatchCancelled = 0;
        var aiService = new ControlledArchiveJobAiEnrichmentService(async cancellationToken =>
        {
            callIndex += 1;
            if (callIndex == 1)
            {
                using var registration = cancellationToken.Register(() => Interlocked.Exchange(ref firstBatchCancelled, 1));
                return await firstBatch.Task.WaitAsync(cancellationToken);
            }

            if (callIndex == 2)
            {
                return await resumedBatch.Task.WaitAsync(cancellationToken);
            }

            throw new InvalidOperationException("Pause/resume scenario should only execute two archive rounds.");
        });
        var scopeFactory = new ControlledArchiveJobScopeFactory(() => aiService);
        var coordinator = new LocalFactArchiveJobCoordinator(
            scopeFactory,
            NullLogger<LocalFactArchiveJobCoordinator>.Instance);

        await coordinator.StartOrResumeAsync();
        await aiService.WaitUntilCalledAsync();

        var pausingStatus = await coordinator.PauseAsync();

        Assert.Equal("running", pausingStatus.State);
        Assert.True(pausingStatus.IsRunning);
        Assert.Equal("正在暂停后台清洗，等待当前批次完成。", pausingStatus.Message);
        Assert.Equal("已收到暂停请求，当前批次结束后会进入暂停状态。", pausingStatus.AttentionMessage);
        Assert.Equal(0, Interlocked.CompareExchange(ref firstBatchCancelled, 0, 0));

        firstBatch.SetResult(new LocalFactPendingProcessSummary(
            new LocalFactPendingCounts(1, 0, 0),
            new LocalFactPendingCounts(0, 1, 0),
            false,
            "本轮已达到单次清洗上限（每个层级最多 1 条），已保存部分结果。",
            new LocalFactPendingContinuation(true, "round_budget_reached")));

        await WaitUntilAsync(() => string.Equals(coordinator.GetStatus().State, "paused", StringComparison.OrdinalIgnoreCase));

        var pausedStatus = coordinator.GetStatus();
        Assert.Equal("paused", pausedStatus.State);
        Assert.False(pausedStatus.IsRunning);
        Assert.True(pausedStatus.RequiresManualResume);
        Assert.Equal("后台清洗已暂停。", pausedStatus.Message);
        Assert.Equal("当前批次处理完成后已进入暂停状态。", pausedStatus.AttentionMessage);
        Assert.Equal(1, pausedStatus.Rounds);
        Assert.Equal(1, pausedStatus.Processed.Market);
        Assert.Equal(1, pausedStatus.Remaining.Sector);
        Assert.Contains(pausedStatus.RecentEvents, item => item.Type == "pause");

        var resumedStatus = await coordinator.StartOrResumeAsync();

        Assert.Equal(pausedStatus.RunId, resumedStatus.RunId);
        Assert.Equal("running", resumedStatus.State);
        Assert.True(resumedStatus.IsRunning);
        Assert.Equal(0, resumedStatus.ConsecutiveRecoverableFailures);

        await WaitUntilAsync(() => aiService.ProcessPendingBatchCalls >= 2, timeoutMs: 5000);

        resumedBatch.SetResult(new LocalFactPendingProcessSummary(
            new LocalFactPendingCounts(0, 1, 0),
            new LocalFactPendingCounts(0, 0, 0),
            true,
            null,
            new LocalFactPendingContinuation(false, "completed")));

        await WaitUntilAsync(() => !coordinator.GetStatus().IsRunning);

        var finalStatus = coordinator.GetStatus();
        Assert.Equal("completed", finalStatus.State);
        Assert.Equal(pausedStatus.RunId, finalStatus.RunId);
        Assert.Equal(1, finalStatus.Processed.Sector);
        Assert.Equal(2, aiService.ProcessPendingBatchCalls);
    }

    [Fact]
    public async Task ArchiveJobCoordinator_ShouldRestartWithFreshRunState()
    {
        var refreshedPendingCounts = new LocalFactPendingCounts(1, 0, 1);
        var firstBatch = new TaskCompletionSource<LocalFactPendingProcessSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var restartedBatch = new TaskCompletionSource<LocalFactPendingProcessSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callIndex = 0;
        var aiService = new ControlledArchiveJobAiEnrichmentService(async cancellationToken =>
        {
            callIndex += 1;
            if (callIndex == 1)
            {
                return await firstBatch.Task.WaitAsync(cancellationToken);
            }

            if (callIndex == 2)
            {
                return await restartedBatch.Task.WaitAsync(cancellationToken);
            }

            throw new InvalidOperationException("Restart scenario should only execute two archive rounds.");
        }, cancellationToken => Task.FromResult(refreshedPendingCounts));
        var scopeFactory = new ControlledArchiveJobScopeFactory(() => aiService);
        var coordinator = new LocalFactArchiveJobCoordinator(
            scopeFactory,
            NullLogger<LocalFactArchiveJobCoordinator>.Instance);

        await coordinator.StartOrResumeAsync();
        await aiService.WaitUntilCalledAsync();

        var pausingStatus = await coordinator.PauseAsync();

        Assert.Equal("running", pausingStatus.State);
        Assert.True(pausingStatus.IsRunning);

        firstBatch.SetResult(new LocalFactPendingProcessSummary(
            new LocalFactPendingCounts(0, 1, 0),
            new LocalFactPendingCounts(0, 0, 1),
            false,
            "本轮已达到单次清洗上限（每个层级最多 1 条），已保存部分结果。",
            new LocalFactPendingContinuation(true, "round_budget_reached")));

        await WaitUntilAsync(() => string.Equals(coordinator.GetStatus().State, "paused", StringComparison.OrdinalIgnoreCase));

        var pausedStatus = coordinator.GetStatus();
        Assert.Equal(1, pausedStatus.Remaining.Stock);
        Assert.Equal(1, pausedStatus.Remaining.Total);

        var restartedStatus = await coordinator.RestartAsync();

        Assert.Equal("running", restartedStatus.State);
        Assert.True(restartedStatus.IsRunning);
        Assert.NotEqual(pausedStatus.RunId, restartedStatus.RunId);
        Assert.Equal(0, restartedStatus.Rounds);
        Assert.Equal(0, restartedStatus.Processed.Total);
        Assert.Equal(0, restartedStatus.Processed.Market);
        Assert.Equal(0, restartedStatus.Processed.Sector);
        Assert.Equal(0, restartedStatus.Processed.Stock);
        Assert.Equal(refreshedPendingCounts.Market, restartedStatus.Remaining.Market);
        Assert.Equal(refreshedPendingCounts.Sector, restartedStatus.Remaining.Sector);
        Assert.Equal(refreshedPendingCounts.Stock, restartedStatus.Remaining.Stock);
        Assert.Equal(refreshedPendingCounts.Total, restartedStatus.Remaining.Total);
        Assert.NotEqual(pausedStatus.Remaining.Total, restartedStatus.Remaining.Total);
        Assert.Contains(restartedStatus.RecentEvents, item => item.Type == "restart");

        await WaitUntilAsync(() => aiService.ProcessPendingBatchCalls >= 2, timeoutMs: 5000);

        restartedBatch.SetResult(new LocalFactPendingProcessSummary(
            refreshedPendingCounts,
            new LocalFactPendingCounts(0, 0, 0),
            true,
            null,
            new LocalFactPendingContinuation(false, "completed")));

        await WaitUntilAsync(() => !coordinator.GetStatus().IsRunning);

        var finalStatus = coordinator.GetStatus();
        Assert.Equal("completed", finalStatus.State);
        Assert.Equal(restartedStatus.RunId, finalStatus.RunId);
        Assert.Equal(1, finalStatus.Processed.Market);
        Assert.Equal(1, finalStatus.Processed.Stock);
        Assert.Equal(2, finalStatus.Processed.Total);
        Assert.Equal(2, aiService.ProcessPendingBatchCalls);
    }

    [Fact]
    public async Task ProcessMarketPendingAsync_WhenLlmFails_ShouldKeepPendingForRetry()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalSectorReports.Add(new LocalSectorReport
        {
            Level = "market",
            SectorName = "大盘环境",
            Title = "Fed holds rates steady",
            Source = "NYT Business",
            SourceTag = "nyt-business-rss",
            PublishTime = new DateTime(2026, 3, 13, 9, 0, 0),
            CrawledAt = new DateTime(2026, 3, 13, 9, 5, 0),
            Url = "https://example.com/m"
        });
        await dbContext.SaveChangesAsync();

        var service = new LocalFactAiEnrichmentService(
            dbContext,
            new StubLlmService(exception: new InvalidOperationException("429 Too Many Requests")),
            new StubSettingsStore(),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        await service.ProcessMarketPendingAsync();

        var stored = await dbContext.LocalSectorReports.SingleAsync();
        Assert.False(stored.IsAiProcessed);
        Assert.Equal("中性", stored.AiSentiment);
        Assert.Null(stored.TranslatedTitle);
    }

    [Fact]
    public async Task ProcessMarketPendingAsync_RequestPath_ShouldPrioritizeCurrentCrawlRowsOverOlderBacklog()
    {
        await using var dbContext = CreateDbContext();
        var backlogPublishBase = new DateTime(2026, 3, 13, 12, 0, 0);
        var backlogCrawledAt = new DateTime(2026, 3, 13, 8, 30, 0, DateTimeKind.Utc);
        for (var index = 0; index < 15; index++)
        {
            dbContext.LocalSectorReports.Add(new LocalSectorReport
            {
                Level = "market",
                SectorName = "大盘环境",
                Title = $"Backlog {index}",
                Source = "Reuters",
                SourceTag = "gnews-reuters",
                PublishTime = backlogPublishBase.AddMinutes(index),
                CrawledAt = backlogCrawledAt,
                Url = $"https://example.com/market/backlog-{index}"
            });
        }

        var currentPublishBase = new DateTime(2026, 3, 13, 9, 0, 0);
        var currentCrawledAt = new DateTime(2026, 3, 13, 10, 0, 0);
        for (var index = 0; index < 30; index++)
        {
            dbContext.LocalSectorReports.Add(new LocalSectorReport
            {
                Level = "market",
                SectorName = "大盘环境",
                Title = $"Current {index}",
                Source = "Reuters",
                SourceTag = "gnews-reuters",
                PublishTime = currentPublishBase.AddMinutes(index),
                CrawledAt = currentCrawledAt,
                Url = $"https://example.com/market/current-{index}"
            });
        }
        await dbContext.SaveChangesAsync();

        var llmService = new CapturingEchoPromptLlmService();
        var service = new LocalFactAiEnrichmentService(
            dbContext,
            llmService,
            new StubSettingsStore(),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        await service.ProcessMarketPendingAsync(mode: LocalFactMarketPendingMode.RequestPath);

        var stored = await dbContext.LocalSectorReports.ToListAsync();

        Assert.Equal(30, stored.Count(item => item.IsAiProcessed));
        Assert.All(
            stored.Where(item => item.Title.StartsWith("Current ", StringComparison.Ordinal)),
            item => Assert.True(item.IsAiProcessed));
        Assert.All(
            stored.Where(item => item.Title.StartsWith("Backlog ", StringComparison.Ordinal)),
            item => Assert.False(item.IsAiProcessed));
        Assert.Equal(30, llmService.ProcessedIds.Count);
    }

    [Fact]
    public async Task ProcessMarketPendingAsync_RequestPath_ShouldBoundSelectionToLiveRequestBudget()
    {
        await using var dbContext = CreateDbContext();
        var publishBase = new DateTime(2026, 3, 13, 11, 0, 0, DateTimeKind.Utc);
        var crawledAt = new DateTime(2026, 3, 13, 11, 30, 0, DateTimeKind.Utc);
        for (var index = 0; index < 80; index++)
        {
            dbContext.LocalSectorReports.Add(new LocalSectorReport
            {
                Level = "market",
                SectorName = "大盘环境",
                Title = $"Market item {index}",
                Source = "Reuters",
                SourceTag = "gnews-reuters",
                PublishTime = publishBase.AddMinutes(index),
                CrawledAt = crawledAt,
                Url = $"https://example.com/market/{index}"
            });
        }
        await dbContext.SaveChangesAsync();

        var llmService = new CapturingEchoPromptLlmService();
        var service = new LocalFactAiEnrichmentService(
            dbContext,
            llmService,
            new StubSettingsStore(newsCleansingSettings: ("active", "", 20)),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        await service.ProcessMarketPendingAsync(mode: LocalFactMarketPendingMode.RequestPath);

        var stored = await dbContext.LocalSectorReports.ToListAsync();

        Assert.Equal([20, 10], llmService.BatchSizes);
        Assert.Equal(2, llmService.CallCount);
        Assert.Equal(30, llmService.ProcessedIds.Count);
        Assert.Equal(30, stored.Count(item => item.IsAiProcessed));
        Assert.Equal(50, stored.Count(item => !item.IsAiProcessed));
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_ShouldUseProviderDefaultModel_WhenNewsCleansingModelIsBlank()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalSectorReports.Add(new LocalSectorReport
        {
            Level = "market",
            SectorName = "大盘环境",
            Title = "Model fallback item",
            Source = "Reuters",
            SourceTag = "gnews-reuters",
            PublishTime = new DateTime(2026, 3, 13, 12, 0, 0),
            CrawledAt = new DateTime(2026, 3, 13, 12, 1, 0),
            Url = "https://example.com/model-fallback"
        });
        await dbContext.SaveChangesAsync();

        var llmService = new CapturingEchoPromptLlmService();
        var service = new LocalFactAiEnrichmentService(
            dbContext,
            llmService,
            new StubSettingsStore(
                newsCleansingSettings: ("ollama", "", 12),
                providers:
                [
                    new LlmProviderSettings
                    {
                        Provider = "ollama",
                        ProviderType = "ollama",
                        Model = "gemma4:e4b",
                        Enabled = true
                    }
                ]),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        var summary = await service.ProcessPendingBatchAsync();

        Assert.Equal("ollama", llmService.LastProvider);
        Assert.Equal("gemma4:e4b", llmService.LastRequest?.Model);
        Assert.Equal(LlmResponseFormats.Json, llmService.LastRequest?.ResponseFormat);
        Assert.Equal(1, summary.Processed.Market);
        Assert.Equal(0, summary.Remaining.Total);
        Assert.NotNull(summary.Continuation);
        Assert.False(summary.Continuation!.MayContinueAutomatically);
        Assert.Equal("completed", summary.Continuation.ReasonCode);
        Assert.True((await dbContext.LocalSectorReports.SingleAsync()).IsAiProcessed);
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_ShouldRepairSingleNonArrayResponseIntoValidBatchJson()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalSectorReports.Add(new LocalSectorReport
        {
            Level = "market",
            SectorName = "大盘环境",
            Title = "Repairable prose item",
            Source = "Reuters",
            SourceTag = "gnews-reuters",
            PublishTime = new DateTime(2026, 3, 13, 12, 5, 0),
            CrawledAt = new DateTime(2026, 3, 13, 12, 6, 0),
            Url = "https://example.com/repair-success"
        });
        await dbContext.SaveChangesAsync();

        var itemId = await dbContext.LocalSectorReports.Select(item => item.Id).SingleAsync();
        var repairedPayload = JsonSerializer.Serialize(new[]
        {
            new
            {
                id = $"market:{itemId}",
                translatedTitle = "已清洗:repair-success",
                aiSentiment = "利好",
                aiTarget = "大盘",
                aiTags = new[] { "宏观货币" }
            }
        });

        var llmService = new SequenceLlmService(
            "好的，请提供您需要我处理的请求。",
            repairedPayload);
        var service = new LocalFactAiEnrichmentService(
            dbContext,
            llmService,
            new StubSettingsStore(),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        var summary = await service.ProcessPendingBatchAsync();
        var stored = await dbContext.LocalSectorReports.SingleAsync();

        Assert.True(summary.Completed);
        Assert.Equal(1, summary.Processed.Market);
        Assert.Equal(0, summary.Remaining.Total);
        Assert.Equal(2, llmService.CallCount);
        Assert.All(llmService.Requests, request => Assert.Equal(LlmResponseFormats.Json, request.ResponseFormat));
        Assert.Contains("唯一一次 JSON repair", llmService.Requests[1].Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(summary.Events, item =>
            item.Type == "parse"
            && item.Message == "模型返回内容不是可解析 JSON 数组，已跳过该批次。");
        Assert.True(stored.IsAiProcessed);
        Assert.Equal("已清洗:repair-success", stored.TranslatedTitle);
        Assert.Equal("利好", stored.AiSentiment);
        Assert.Equal("大盘", stored.AiTarget);
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_ShouldKeepFailureExplainability_WhenRepairStillFails()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalSectorReports.Add(new LocalSectorReport
        {
            Level = "market",
            SectorName = "大盘环境",
            Title = "Repair still fails item",
            Source = "Reuters",
            SourceTag = "gnews-reuters",
            PublishTime = new DateTime(2026, 3, 13, 12, 10, 0),
            CrawledAt = new DateTime(2026, 3, 13, 12, 11, 0),
            Url = "https://example.com/repair-failure"
        });
        await dbContext.SaveChangesAsync();

        var itemId = await dbContext.LocalSectorReports.Select(item => item.Id).SingleAsync();
        var llmService = new SequenceLlmService(
            "```json\n[{\"id\":\"market:" + itemId + "\",\"translatedTitle\":\"坏掉",
            "[{\"id\":\"market:" + itemId + "\",\"translatedTitle\":\"还是坏掉\"");
        var service = new LocalFactAiEnrichmentService(
            dbContext,
            llmService,
            new StubSettingsStore(),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        var summary = await service.ProcessPendingBatchAsync();

        Assert.False(summary.Completed);
        Assert.Equal(0, summary.Processed.Total);
        Assert.Equal(1, summary.Remaining.Market);
        Assert.Equal(2, llmService.CallCount);
        Assert.All(llmService.Requests, request => Assert.Equal(LlmResponseFormats.Json, request.ResponseFormat));
        Assert.Contains("唯一一次 JSON repair", llmService.Requests[1].Prompt, StringComparison.Ordinal);
        Assert.Equal("本轮批量清洗未取得进展：模型返回 JSON 解析失败，待处理资讯已保留。", summary.StopReason);
        Assert.NotNull(summary.Continuation);
        Assert.False(summary.Continuation!.MayContinueAutomatically);
        Assert.Equal("no_progress", summary.Continuation.ReasonCode);
        Assert.Contains(summary.Events, item =>
            item.Level == "warning"
            && item.Type == "parse"
            && item.Message == "模型返回 JSON 解析失败，已跳过该批次。");
        Assert.False((await dbContext.LocalSectorReports.SingleAsync()).IsAiProcessed);
    }

    private static AppDbContext CreateDbContext()
    {
        return CreateDbContext(Guid.NewGuid().ToString("N"), new InMemoryDatabaseRoot());
    }

    private static AppDbContext CreateDbContext(string databaseName, InMemoryDatabaseRoot root)
    {
        // V040-DEBT-1: 全量回归 20+ DbContextOptions 后 EF 触发 ManyServiceProvidersCreatedWarning 抛错；
        // 这是测试基础设施警告（每个测试 new options 是 InMemory 测试的常规模式），不是生产问题，按 EF 文档忽略。
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName, root)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        return new AppDbContext(options);
    }

    private static LocalFactAiEnrichmentService CreateService(AppDbContext dbContext, string llmPayload)
    {
        return new LocalFactAiEnrichmentService(
            dbContext,
            new StubLlmService(llmPayload),
            new StubSettingsStore(),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);
    }

    private sealed class StubLlmService : ILlmService
    {
        private readonly string _content;
        private readonly Exception? _exception;

        public StubLlmService(string content = "[]", Exception? exception = null)
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

    private sealed class EchoPromptLlmService : ILlmService
    {
        public Task<LlmChatResult> ChatAsync(string provider, LlmChatRequest request, CancellationToken cancellationToken = default)
        {
            var prompt = request.Prompt;
            var arrayStart = prompt.IndexOf('[');
            var arrayEnd = prompt.LastIndexOf(']');
            Assert.True(arrayStart >= 0 && arrayEnd >= arrayStart);

            using var payload = JsonDocument.Parse(prompt.Substring(arrayStart, arrayEnd - arrayStart + 1));
            var result = new List<object>();
            foreach (var item in payload.RootElement.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString();
                result.Add(new
                {
                    id,
                    translatedTitle = $"已清洗:{id}",
                    aiSentiment = "中性",
                    aiTarget = "无明确靶点",
                    aiTags = Array.Empty<string>()
                });
            }

            return Task.FromResult(new LlmChatResult(JsonSerializer.Serialize(result)));
        }
    }

    private sealed class CapturingEchoPromptLlmService : ILlmService
    {
        public string? LastProvider { get; private set; }
        public LlmChatRequest? LastRequest { get; private set; }
        public int CallCount { get; private set; }
        public List<int> BatchSizes { get; } = [];
        public List<string> ProcessedIds { get; } = [];

        public Task<LlmChatResult> ChatAsync(string provider, LlmChatRequest request, CancellationToken cancellationToken = default)
        {
            LastProvider = provider;
            LastRequest = request;
            CallCount += 1;

            var prompt = request.Prompt;
            var arrayStart = prompt.IndexOf('[');
            var arrayEnd = prompt.LastIndexOf(']');
            Assert.True(arrayStart >= 0 && arrayEnd >= arrayStart);

            using var payload = JsonDocument.Parse(prompt.Substring(arrayStart, arrayEnd - arrayStart + 1));
            BatchSizes.Add(payload.RootElement.GetArrayLength());
            var result = new List<object>();
            foreach (var item in payload.RootElement.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString();
                Assert.NotNull(id);
                ProcessedIds.Add(id!);
                result.Add(new
                {
                    id,
                    translatedTitle = $"已清洗:{id}",
                    aiSentiment = "中性",
                    aiTarget = "无明确靶点",
                    aiTags = Array.Empty<string>()
                });
            }

            return Task.FromResult(new LlmChatResult(JsonSerializer.Serialize(result)));
        }
    }

    private sealed class SequenceLlmService : ILlmService
    {
        private readonly Queue<string> _responses;

        public SequenceLlmService(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public List<LlmChatRequest> Requests { get; } = [];
        public int CallCount => Requests.Count;

        public Task<LlmChatResult> ChatAsync(string provider, LlmChatRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No queued LLM response remains.");
            }

            return Task.FromResult(new LlmChatResult(_responses.Dequeue()));
        }
    }

    private sealed class AppendPendingDuringSweepLlmService : ILlmService
    {
        private readonly string _databaseName;
        private readonly InMemoryDatabaseRoot _root;
        private readonly long _processedId;
        private readonly DateTime _newPublishTime;
        private bool _appended;

        public AppendPendingDuringSweepLlmService(string databaseName, InMemoryDatabaseRoot root, long processedId, DateTime newPublishTime)
        {
            _databaseName = databaseName;
            _root = root;
            _processedId = processedId;
            _newPublishTime = newPublishTime;
        }

        public async Task<LlmChatResult> ChatAsync(string provider, LlmChatRequest request, CancellationToken cancellationToken = default)
        {
            if (!_appended)
            {
                _appended = true;
                await using var appendContext = CreateDbContext(_databaseName, _root);
                appendContext.LocalSectorReports.Add(new LocalSectorReport
                {
                    Level = "market",
                    SectorName = "大盘环境",
                    Title = "New pending item",
                    Source = "Reuters",
                    SourceTag = "gnews-reuters",
                    PublishTime = _newPublishTime,
                    CrawledAt = _newPublishTime.AddMinutes(1),
                    Url = "https://example.com/market/new"
                });
                await appendContext.SaveChangesAsync(cancellationToken);
            }

            var content = "[{\"id\":\"market:" + _processedId + "\",\"translatedTitle\":\"已清洗:market:" + _processedId + "\",\"aiSentiment\":\"中性\",\"aiTarget\":\"大盘\",\"aiTags\":[]}]";
            return new LlmChatResult(content);
        }
    }

    private sealed class BlockingLlmService : ILlmService
    {
        private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string _content;

        public BlockingLlmService(string content)
        {
            _content = content;
        }

        public async Task<LlmChatResult> ChatAsync(string provider, LlmChatRequest request, CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult(true);
            await _release.Task.WaitAsync(cancellationToken);
            return new LlmChatResult(_content);
        }

        public Task WaitUntilCalledAsync() => _entered.Task;

        public void Release() => _release.TrySetResult(true);
    }

    private sealed class StubSettingsStore : ILlmSettingsStore
    {
        private readonly string _activeProviderKey;
        private readonly (string Provider, string Model, int BatchSize) _newsCleansingSettings;
        private readonly IReadOnlyDictionary<string, LlmProviderSettings> _providers;

        public StubSettingsStore(
            string activeProviderKey = "default",
            (string Provider, string Model, int BatchSize)? newsCleansingSettings = null,
            params LlmProviderSettings[] providers)
        {
            _activeProviderKey = activeProviderKey;
            _newsCleansingSettings = newsCleansingSettings ?? ("active", "", 12);
            _providers = providers.ToDictionary(item => item.Provider, StringComparer.OrdinalIgnoreCase);
        }

        public Task<IReadOnlyCollection<LlmProviderSettings>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<LlmProviderSettings>>(_providers.Values.ToArray());
        public Task<string> GetActiveProviderKeyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_activeProviderKey);
        public Task<string> SetActiveProviderKeyAsync(string provider, CancellationToken cancellationToken = default)
            => Task.FromResult(provider);
        public Task<string> ResolveProviderKeyAsync(string? provider, CancellationToken cancellationToken = default)
            => Task.FromResult(string.IsNullOrWhiteSpace(provider) || string.Equals(provider, "active", StringComparison.OrdinalIgnoreCase)
                ? _activeProviderKey
                : provider);
        public Task<LlmProviderSettings?> GetProviderAsync(string provider, CancellationToken cancellationToken = default)
            => Task.FromResult(_providers.TryGetValue(provider, out var settings) ? settings : null);
        public Task<LlmProviderSettings> UpsertAsync(LlmProviderSettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult(settings);
        public Task<string> GetGlobalTavilyKeyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);
        public Task<(string Provider, string Model, int BatchSize)> GetNewsCleansingSettingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_newsCleansingSettings);
        public Task SetNewsCleansingSettingsAsync(string provider, string model, int batchSize, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class ControlledArchiveJobAiEnrichmentService : ILocalFactAiEnrichmentService
    {
        private readonly Func<CancellationToken, Task<LocalFactPendingProcessSummary>> _processPendingBatchAsync;
        private readonly Func<CancellationToken, Task<LocalFactPendingCounts>> _getPendingCountsAsync;
        private readonly TaskCompletionSource<bool> _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ControlledArchiveJobAiEnrichmentService(
            Func<CancellationToken, Task<LocalFactPendingProcessSummary>> processPendingBatchAsync,
            Func<CancellationToken, Task<LocalFactPendingCounts>>? getPendingCountsAsync = null)
        {
            _processPendingBatchAsync = processPendingBatchAsync;
            _getPendingCountsAsync = getPendingCountsAsync ?? (_ => Task.FromResult(new LocalFactPendingCounts(0, 0, 0)));
        }

        public int ProcessPendingBatchCalls { get; private set; }

        public Task ProcessMarketPendingAsync(
            CancellationToken cancellationToken = default,
            LocalFactMarketPendingMode mode = LocalFactMarketPendingMode.Default)
        {
            throw new NotSupportedException();
        }

        public Task ProcessSymbolPendingAsync(string symbol, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<LocalFactPendingProcessSummary> ProcessPendingBatchAsync(CancellationToken cancellationToken = default)
        {
            ProcessPendingBatchCalls += 1;
            _called.TrySetResult(true);
            return _processPendingBatchAsync(cancellationToken);
        }

        public Task<LocalFactPendingCounts> GetPendingCountsAsync(CancellationToken cancellationToken = default)
        {
            return _getPendingCountsAsync(cancellationToken);
        }

        public Task WaitUntilCalledAsync() => _called.Task;
    }

    private sealed class ControlledArchiveJobScopeFactory : IServiceScopeFactory
    {
        private readonly Func<ILocalFactAiEnrichmentService> _serviceFactory;

        public ControlledArchiveJobScopeFactory(Func<ILocalFactAiEnrichmentService> serviceFactory)
        {
            _serviceFactory = serviceFactory;
        }

        public int ScopeCount { get; private set; }

        public IServiceScope CreateScope()
        {
            ScopeCount += 1;
            return new ControlledArchiveJobScope(_serviceFactory());
        }
    }

    private sealed class ControlledArchiveJobScope : IServiceScope, IAsyncDisposable
    {
        public ControlledArchiveJobScope(ILocalFactAiEnrichmentService aiService)
        {
            ServiceProvider = new ControlledArchiveJobServiceProvider(aiService);
        }

        public IServiceProvider ServiceProvider { get; }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ControlledArchiveJobServiceProvider : IServiceProvider
    {
        private readonly ILocalFactAiEnrichmentService _aiService;

        public ControlledArchiveJobServiceProvider(ILocalFactAiEnrichmentService aiService)
        {
            _aiService = aiService;
        }

        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(ILocalFactAiEnrichmentService)
                ? _aiService
                : null;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 3000)
    {
        var startedAt = DateTime.UtcNow;
        while (!predicate())
        {
            if ((DateTime.UtcNow - startedAt).TotalMilliseconds > timeoutMs)
            {
                throw new TimeoutException("Condition was not met within the expected time.");
            }

            await Task.Delay(20);
        }
    }

    [Theory]
    [InlineData("无荒隔靶点")]
    [InlineData("无明确靶点")]
    [InlineData("无明确标的")]
    [InlineData("N/A")]
    [InlineData("null")]
    [InlineData("undefined")]
    [InlineData("未知标签")]
    [InlineData("a")]
    [InlineData("这是一个非常非常长的标签文本超过了允许的最大字符数限制")]
    public void IsGarbageTag_ShouldReturnTrue_ForKnownGarbageOrInvalidTags(string tag)
    {
        Assert.True(Modules.Stocks.Services.LocalFactAiTargetPolicy.IsGarbageTag(tag));
    }

    [Theory]
    [InlineData("政策红利")]
    [InlineData("板块轮动")]
    [InlineData("贵州茅台")]
    [InlineData("银行")]
    public void IsGarbageTag_ShouldReturnFalse_ForValidTags(string tag)
    {
        Assert.False(Modules.Stocks.Services.LocalFactAiTargetPolicy.IsGarbageTag(tag));
    }

    [Fact]
    public async Task ProcessSymbolPendingAsync_ShouldFilterGarbageTags()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LocalStockNews.Add(new LocalStockNews
        {
            Symbol = "sh600000",
            Name = "浦发银行",
            SectorName = "银行",
            Title = "Bank stocks rise after policy support",
            Category = "company_news",
            Source = "WSJ",
            SourceTag = "wsj-rss",
            PublishTime = new DateTime(2026, 3, 13, 8, 0, 0),
            CrawledAt = new DateTime(2026, 3, 13, 8, 5, 0),
            Url = "https://example.com/garbage-tag"
        });
        await dbContext.SaveChangesAsync();

        var service = new LocalFactAiEnrichmentService(
            dbContext,
            new StubLlmService("""
            [
              {
                "id": "stock:1",
                "translatedTitle": "银行股政策利好上涨",
                "aiSentiment": "利好",
                "aiTarget": "浦发银行",
                "aiTags": ["政策红利", "无荒隔靶点", "银行", "N/A", "undefined"]
              }
            ]
            """),
            new StubSettingsStore(),
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        await service.ProcessSymbolPendingAsync("sh600000");

        var processed = await dbContext.LocalStockNews.FirstAsync();
        Assert.True(processed.IsAiProcessed);
        // Garbage tags "无荒隔靶点", "N/A", "undefined" should be filtered
        var tags = JsonSerializer.Deserialize<string[]>(processed.AiTags!);
        Assert.NotNull(tags);
        Assert.DoesNotContain("无荒隔靶点", tags);
        Assert.DoesNotContain("N/A", tags);
        Assert.DoesNotContain("undefined", tags);
        // Valid tags should be present
        Assert.Contains("政策红利", tags);
    }
}