using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Infrastructure.Storage;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class SourceGovernanceReadServiceTests
{
    [Fact]
    public void ServiceCollection_ShouldResolveServiceFromRuntimePaths()
    {
        var services = new ServiceCollection();
        var environment = new FakeHostEnvironment();
        var runtimePaths = new AppRuntimePaths(environment, new ConfigurationBuilder().Build());

        services.AddSingleton(runtimePaths);
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddScoped<ISourceGovernanceReadService>(serviceProvider =>
            new SourceGovernanceReadService(
                serviceProvider.GetRequiredService<AppDbContext>(),
                serviceProvider.GetRequiredService<AppRuntimePaths>()));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<ISourceGovernanceReadService>();

        Assert.NotNull(service);
        Assert.IsType<SourceGovernanceReadService>(service);
    }

    [Fact]
    public async Task GetOverviewAsync_ReturnsExpectedCounters()
    {
        await using var db = CreateDb();
        db.NewsSourceRegistries.AddRange(
            new NewsSourceRegistry { Domain = "a.test", BaseUrl = "https://a.test", Status = NewsSourceStatus.Active, Tier = NewsSourceTier.Preferred, UpdatedAt = DateTime.UtcNow },
            new NewsSourceRegistry { Domain = "b.test", BaseUrl = "https://b.test", Status = NewsSourceStatus.Quarantine, Tier = NewsSourceTier.Fallback, UpdatedAt = DateTime.UtcNow });
        db.NewsSourceCandidates.Add(new NewsSourceCandidate { Domain = "c.test", HomepageUrl = "https://c.test", Status = NewsSourceStatus.Pending, DiscoveryReason = "seed" });
        db.CrawlerChangeQueues.AddRange(
            new CrawlerChangeQueue { SourceId = 1, Domain = "a.test", Status = CrawlerChangeStatus.Generated, TriggerReason = "x" },
            new CrawlerChangeQueue { SourceId = 1, Domain = "a.test", Status = CrawlerChangeStatus.Deployed, TriggerReason = "x" });
        db.NewsSourceVerificationRuns.Add(new NewsSourceVerificationRun { Domain = "a.test", Success = false, HttpStatusCode = 500, ParseSuccessRate = 0m, TimestampCoverage = 0m, DuplicateRate = 1m, ContentDepth = 0m, CrossSourceAgreement = 0m, FreshnessLagMinutes = 1200, VerificationScore = 0m, FailureReason = "http_failed", ExecutedAt = DateTime.UtcNow.AddMinutes(-10) });
        db.CrawlerChangeRuns.Add(new CrawlerChangeRun { QueueId = 2, Domain = "a.test", Result = CrawlerChangeStatus.RolledBack, ExecutedAt = DateTime.UtcNow.AddMinutes(-5) });
        await db.SaveChangesAsync();

        var service = new SourceGovernanceReadService(db, new FakeHostEnvironment());
        var result = await service.GetOverviewAsync();

        Assert.Equal(1, result.ActiveSources);
        Assert.Equal(1, result.QuarantinedSources);
        Assert.Equal(1, result.PendingCandidates);
        Assert.Equal(1, result.PendingChanges);
        Assert.Equal(1, result.DeployedChanges);
        Assert.True(result.RollbackCount7d >= 1);
        Assert.True(result.RecentErrorCount24h >= 1);
    }

    [Fact]
    public async Task GetChangesAsync_ReturnsLatestRunPerQueue()
    {
        await using var db = CreateDb();
        db.CrawlerChangeQueues.Add(new CrawlerChangeQueue
        {
            SourceId = 1,
            Domain = "a.test",
            Status = CrawlerChangeStatus.Rejected,
            TriggerReason = "broken",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-1)
        });
        await db.SaveChangesAsync();

        db.CrawlerChangeRuns.AddRange(
            new CrawlerChangeRun { QueueId = 1, Domain = "a.test", Result = "old", Note = "old-note", ExecutedAt = DateTime.UtcNow.AddMinutes(-4) },
            new CrawlerChangeRun { QueueId = 1, Domain = "a.test", Result = "new", Note = "new-note", ExecutedAt = DateTime.UtcNow.AddMinutes(-2) });
        await db.SaveChangesAsync();

        var service = new SourceGovernanceReadService(db, new FakeHostEnvironment());
        var page = await service.GetChangesAsync(CrawlerChangeStatus.Rejected, "a.test", 1, 10);

        Assert.Equal(1, page.Total);
        Assert.Single(page.Items);
        Assert.Equal("new", page.Items[0].LatestRunResult);
        Assert.Equal("new-note", page.Items[0].LatestRunNote);
    }

    [Fact]
    public async Task SearchTraceAsync_ReturnsMatchedLogLines()
    {
        await using var db = CreateDb();
        var env = new FakeHostEnvironment();
        var logDir = Path.Combine(env.ContentRootPath, "App_Data", "logs");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, "llm-requests.txt");
        await File.WriteAllLinesAsync(logPath, new[]
        {
            "2026-03-12 [LLM-AUDIT] traceId=abc12345 stage=request",
            "2026-03-12 [LLM-AUDIT] traceId=zzz999 stage=request",
            "2026-03-12 [LLM-AUDIT] traceId=abc12345 stage=response"
        });

        var service = new SourceGovernanceReadService(db, env);
        var result = await service.SearchTraceAsync("abc12345", 10);

        Assert.Equal(2, result.Lines.Count);
        Assert.All(result.Lines, x => Assert.Contains("abc12345", x));
    }

    [Fact]
    public async Task GetLlmConversationLogsAsync_GroupsRequestAndResponseByTraceId()
    {
        await using var db = CreateDb();
        var env = new FakeHostEnvironment();
        var logDir = Path.Combine(env.ContentRootPath, "App_Data", "logs");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, "llm-requests.txt");
        await File.WriteAllLinesAsync(logPath, new[]
        {
            "2026-03-12 10:00:00.000 [LLM-AUDIT] traceId=t1 stage=request provider=openai model=gpt prompt=hello",
            "2026-03-12 10:00:01.000 [LLM] traceId=t1 prompt provider=openai model=gpt userPrompt=test",
            "2026-03-12 10:00:02.000 [OTHER] not-llm",
            "2026-03-12 10:00:03.000 [LLM-AUDIT] traceId=t1 stage=response provider=openai model=gpt content=ok",
            "2026-03-12 10:00:04.000 [LLM-AUDIT] traceId=t2 stage=request provider=openai model=gpt prompt=bye",
            "2026-03-12 10:00:05.000 [LLM-AUDIT] traceId=t2 stage=error provider=openai model=gpt message=failed"
        });

        var service = new SourceGovernanceReadService(db, env);
        var logs = await service.GetLlmConversationLogsAsync(10, "openai");

        Assert.Equal(2, logs.Count);

        var paired = Assert.Single(logs, x => x.TraceId == "t1");
        Assert.Equal("response", paired.Status);
        Assert.Contains("已脱敏", paired.RequestText);
        Assert.Equal("ok", paired.ResponseText);
        Assert.Equal(3, paired.Lines.Count);

        var failed = Assert.Single(logs, x => x.TraceId == "t2");
        Assert.Equal("error", failed.Status);
        Assert.Contains("已脱敏", failed.RequestText);
        Assert.Equal("failed", failed.ErrorText);
    }

    [Fact]
    public async Task GetLlmConversationLogsAsync_ShouldStripReasoningSectionsFromResponse()
    {
        await using var db = CreateDb();
        var env = new FakeHostEnvironment();
        var logDir = Path.Combine(env.ContentRootPath, "App_Data", "logs");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, "llm-requests.txt");
        await File.WriteAllLinesAsync(logPath, new[]
        {
            "2026-03-12 10:00:00.000 [LLM-AUDIT] traceId=t3 stage=request provider=openai model=gpt prompt=hello",
            "2026-03-12 10:00:03.000 [LLM-AUDIT] traceId=t3 stage=response provider=openai model=gpt content=<think>hidden</think>最终建议\n## 思考过程\n不应展示"
        });

        var service = new SourceGovernanceReadService(db, env);
        var logs = await service.GetLlmConversationLogsAsync(10, "t3");

        var item = Assert.Single(logs);
        Assert.Equal("最终建议", item.ResponseText);
        Assert.DoesNotContain("hidden", item.ResponseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("思考过程", item.ResponseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLlmConversationLogsAsync_ShouldRedactEnglishReasoningScaffold()
    {
        await using var db = CreateDb();
        var env = new FakeHostEnvironment();
        var logDir = Path.Combine(env.ContentRootPath, "App_Data", "logs");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, "llm-requests.txt");
        await File.WriteAllLinesAsync(logPath, new[]
        {
            "2026-03-12 10:00:00.000 [LLM-AUDIT] traceId=t4 stage=request provider=openai model=gpt prompt=hello",
            "2026-03-12 10:00:03.000 [LLM-AUDIT] traceId=t4 stage=response provider=openai model=gpt content=**My Thought Process** Let's break this down before answering."
        });

        var service = new SourceGovernanceReadService(db, env);
        var logs = await service.GetLlmConversationLogsAsync(10, "t4");

        var item = Assert.Single(logs);
        Assert.Equal("返回内容包含中间推理，已脱敏。", item.ResponseText);
    }

    [Fact]
    public async Task GetLlmConversationLogsAsync_ShouldStripReasoningTitleScaffold()
    {
        await using var db = CreateDb();
        var env = new FakeHostEnvironment();
        var logDir = Path.Combine(env.ContentRootPath, "App_Data", "logs");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, "llm-requests.txt");
        await File.WriteAllLinesAsync(logPath, new[]
        {
            "2026-03-12 10:00:00.000 [LLM-AUDIT] traceId=t5 stage=request provider=openai model=gpt prompt=hello",
            "2026-03-12 10:00:03.000 [LLM-AUDIT] traceId=t5 stage=response provider=openai model=gpt content=**Considering the Request** 最终建议：关注银行板块估值修复。"
        });

        var service = new SourceGovernanceReadService(db, env);
        var logs = await service.GetLlmConversationLogsAsync(10, "t5");

        var item = Assert.Single(logs);
        Assert.Equal("最终建议：关注银行板块估值修复。", item.ResponseText);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "SimplerJiangAiAgent.Api.Tests";
        public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "source-governance-read-tests", Guid.NewGuid().ToString("N"));
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}