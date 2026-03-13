using System.Net;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Infrastructure.Logging;
using Xunit;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class SourceGovernanceServiceTests
{
    [Fact]
    public async Task RunOnceAsync_DiscoveryAndVerification_PromotesCandidateToRegistry()
    {
        await using var dbContext = CreateDbContext();
        var options = Options.Create(new SourceGovernanceOptions
        {
            EnableLlmDiscovery = true,
            EnableCrawlerAutoFix = false,
            LlmProvider = "mock",
            CandidatePromotionScore = 0.7m,
            MinParseSuccessRate = 0.6m,
            MinTimestampCoverage = 0.2m,
            MaxFreshnessLagMinutes = 600
        });

        var llm = new FakeLlmService(
            """
            [
              {
                "domain": "sample-source.test",
                "homepageUrl": "https://sample-source.test",
                "proposedTier": "preferred",
                "fetchStrategy": "html",
                "reason": "high coverage"
              }
            ]
            """);

        var httpFactory = new FakeHttpClientFactory(new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("published 2026-03-12 market update")
            }));

        var service = new SourceGovernanceService(dbContext, options, new FakeFileLogWriter(), llm, httpFactory, new FakeCommandRunner());
        await service.RunOnceAsync();

        var registry = await dbContext.NewsSourceRegistries.SingleAsync();
        Assert.Equal("sample-source.test", registry.Domain);
        Assert.Equal(NewsSourceStatus.Active, registry.Status);

        var candidate = await dbContext.NewsSourceCandidates.SingleAsync();
        Assert.Equal(NewsSourceStatus.Active, candidate.Status);

        var run = await dbContext.NewsSourceVerificationRuns.SingleAsync();
        Assert.True(run.Success);
    }

    [Fact]
    public async Task RunOnceAsync_QuarantinedSource_GeneratesAndDeploysCrawlerChange()
    {
        var repoRoot = CreateSandboxRepository();
        try
        {
        await using var dbContext = CreateDbContext();
        dbContext.NewsSourceRegistries.Add(new NewsSourceRegistry
        {
            Domain = "broken-parser.test",
            BaseUrl = "https://broken-parser.test",
            Status = NewsSourceStatus.Quarantine,
            Tier = NewsSourceTier.Fallback,
            FetchStrategy = "html",
            ParserVersion = "v1",
            ConsecutiveFailures = 5,
            LastStatusReason = "low_parse_success"
        });
        await dbContext.SaveChangesAsync();

        var options = Options.Create(new SourceGovernanceOptions
        {
            EnableLlmDiscovery = false,
            EnableCrawlerAutoFix = true,
            LlmProvider = "mock",
            RepositoryRoot = repoRoot,
            MinParseSuccessRate = 0.9m,
            MinTimestampCoverage = 0.9m,
            RollbackGraceMinutes = 1440
        });

        var llm = new FakeLlmService(
            """
            {
              "files": [
                "backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/SinaCompanyNewsParser.cs",
                "backend/SimplerJiangAiAgent.Api.Tests/SinaCompanyNewsParserTests.cs"
              ],
                            "patches": [
                                {
                                    "path": "backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/SinaCompanyNewsParser.cs",
                                    "content": "namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services; public sealed class SinaCompanyNewsParser { }"
                                },
                                {
                                    "path": "backend/SimplerJiangAiAgent.Api.Tests/SinaCompanyNewsParserTests.cs",
                                    "content": "namespace SimplerJiangAiAgent.Api.Tests; public sealed class SinaCompanyNewsParserTests { }"
                                }
                            ],
              "summary": "adjust selector and timestamp parsing",
                            "testCommand": "dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter SinaCompanyNewsParserTests",
                            "replayCommand": "dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter SourceGovernanceServiceTests"
            }
            """);

        var httpFactory = new FakeHttpClientFactory(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("ok")
        }));

        var commandRunner = new FakeCommandRunner();
        commandRunner.SetExitCode("dotnet build backend/SimplerJiangAiAgent.Api/SimplerJiangAiAgent.Api.csproj -nologo", 0);
        commandRunner.SetExitCode("dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter SinaCompanyNewsParserTests", 0);
        commandRunner.SetExitCode("dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter SourceGovernanceServiceTests", 0);

        var service = new SourceGovernanceService(dbContext, options, new FakeFileLogWriter(), llm, httpFactory, commandRunner);
        await service.RunOnceAsync();

        var queue = await dbContext.CrawlerChangeQueues.SingleAsync();
        Assert.Equal(CrawlerChangeStatus.Deployed, queue.Status);
        Assert.Contains("Parser", queue.ProposedFilesJson ?? string.Empty);

        var run = await dbContext.CrawlerChangeRuns.SingleAsync();
        Assert.Equal("deployed", run.Result);
        }
        finally
        {
            Directory.Delete(repoRoot, true);
        }
    }

    [Fact]
    public async Task RunOnceAsync_RepeatedSameDayRun_UpdatesExistingHealthDailyInsteadOfAddingDuplicate()
    {
        await using var dbContext = CreateDbContext();
        dbContext.NewsSourceRegistries.Add(new NewsSourceRegistry
        {
            Domain = "repeat-health.test",
            BaseUrl = "https://repeat-health.test",
            Status = NewsSourceStatus.Active,
            Tier = NewsSourceTier.Preferred,
            FetchStrategy = "html",
            ParseSuccessRate = 0.70m,
            TimestampCoverage = 0.55m,
            FreshnessLagMinutes = 120,
            ConsecutiveFailures = 1,
            LastStatusReason = "seed"
        });
        await dbContext.SaveChangesAsync();

        var options = Options.Create(new SourceGovernanceOptions
        {
            EnableLlmDiscovery = false,
            EnableCrawlerAutoFix = false,
            MinParseSuccessRate = 0.6m,
            MinTimestampCoverage = 0.2m,
            MaxFreshnessLagMinutes = 600
        });

        var service = new SourceGovernanceService(
            dbContext,
            options,
            new FakeFileLogWriter(),
            new FakeLlmService("[]"),
            new FakeHttpClientFactory(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            new FakeCommandRunner());

        await service.RunOnceAsync();

        var source = await dbContext.NewsSourceRegistries.SingleAsync();
        source.ParseSuccessRate = 0.92m;
        source.TimestampCoverage = 0.88m;
        source.FreshnessLagMinutes = 15;
        source.ConsecutiveFailures = 0;
        await dbContext.SaveChangesAsync();

        await service.RunOnceAsync();

        var healthRows = await dbContext.NewsSourceHealthDailies.ToListAsync();
        var daily = Assert.Single(healthRows);
        Assert.Equal(source.Id, daily.SourceId);
        Assert.Equal(DateTime.UtcNow.Date, daily.HealthDate);
        Assert.Equal(0.92m, daily.ParseSuccessRate);
        Assert.Equal(0.88m, daily.TimestampCoverage);
        Assert.Equal(15, daily.FreshnessLagMinutes);
        Assert.Equal(0, daily.ErrorCount);
    }

    [Fact]
    public async Task RunOnceAsync_CommandValidationFails_RejectsCrawlerChange()
    {
        var repoRoot = CreateSandboxRepository();
        try
        {
        await using var dbContext = CreateDbContext();
        dbContext.NewsSourceRegistries.Add(new NewsSourceRegistry
        {
            Domain = "failing-parser.test",
            BaseUrl = "https://failing-parser.test",
            Status = NewsSourceStatus.Quarantine,
            Tier = NewsSourceTier.Fallback,
            FetchStrategy = "html",
            ParserVersion = "v1",
            ConsecutiveFailures = 4,
            LastStatusReason = "parse_error"
        });
        await dbContext.SaveChangesAsync();

        var options = Options.Create(new SourceGovernanceOptions
        {
            EnableLlmDiscovery = false,
            EnableCrawlerAutoFix = true,
            LlmProvider = "mock",
            RepositoryRoot = repoRoot
        });

        var llm = new FakeLlmService(
            """
            {
              "files": [
                "backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/SinaCompanyNewsParser.cs"
              ],
                            "patches": [
                                {
                                    "path": "backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/SinaCompanyNewsParser.cs",
                                    "content": "namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services; public sealed class SinaCompanyNewsParser { }"
                                }
                            ],
              "summary": "failing patch",
                            "testCommand": "dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter SinaCompanyNewsParserTests",
                            "replayCommand": "dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter SourceGovernanceServiceTests"
            }
            """);

        var httpFactory = new FakeHttpClientFactory(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("ok")
        }));

        var commandRunner = new FakeCommandRunner();
        commandRunner.SetExitCode("dotnet build backend/SimplerJiangAiAgent.Api/SimplerJiangAiAgent.Api.csproj -nologo", 1);

        var service = new SourceGovernanceService(dbContext, options, new FakeFileLogWriter(), llm, httpFactory, commandRunner);
        await service.RunOnceAsync();

        var queue = await dbContext.CrawlerChangeQueues.SingleAsync();
        Assert.Equal(CrawlerChangeStatus.Rejected, queue.Status);
        Assert.Equal("build_failed", queue.ValidationNote);
        }
        finally
        {
            Directory.Delete(repoRoot, true);
        }
    }

    [Fact]
    public async Task RunOnceAsync_DeployedChangeWithQuarantineSource_AutoRollsBack()
    {
        var repoRoot = CreateSandboxRepository();
        var rollbackTargetPath = Path.Combine(repoRoot, "backend", "SimplerJiangAiAgent.Api", "Modules", "Stocks", "Services", "RollbackTempParser.cs");
        await File.WriteAllTextAsync(rollbackTargetPath, "new-content");

        try
        {
        await using var dbContext = CreateDbContext();
        var source = new NewsSourceRegistry
        {
            Domain = "rollback-source.test",
            BaseUrl = "https://rollback-source.test",
            Status = NewsSourceStatus.Quarantine,
            Tier = NewsSourceTier.Fallback,
            FetchStrategy = "html",
            ParserVersion = "v1",
            ConsecutiveFailures = 6,
            LastStatusReason = "health_degraded"
        };
        dbContext.NewsSourceRegistries.Add(source);
        await dbContext.SaveChangesAsync();

        dbContext.CrawlerChangeQueues.Add(new CrawlerChangeQueue
        {
            SourceId = source.Id,
            Domain = source.Domain,
            Status = CrawlerChangeStatus.Deployed,
            TriggerReason = "manual_seed",
            DeploymentBackupJson = "[{\"Path\":\"backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/RollbackTempParser.cs\",\"Existed\":true,\"Content\":\"old-content\"}]",
            UpdatedAt = DateTime.UtcNow.AddDays(-2)
        });
        await dbContext.SaveChangesAsync();

        var options = Options.Create(new SourceGovernanceOptions
        {
            EnableLlmDiscovery = false,
            EnableCrawlerAutoFix = true,
            LlmProvider = "mock",
            RepositoryRoot = repoRoot
        });

        var service = new SourceGovernanceService(
            dbContext,
            options,
            new FakeFileLogWriter(),
            new FakeLlmService("[]"),
            new FakeHttpClientFactory(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            new FakeCommandRunner());

        await service.RunOnceAsync();

        var queue = await dbContext.CrawlerChangeQueues
            .Where(x => x.TriggerReason == "manual_seed")
            .SingleAsync();
        Assert.Equal(CrawlerChangeStatus.RolledBack, queue.Status);
        Assert.Contains(await dbContext.CrawlerChangeRuns.Select(x => x.Result).ToListAsync(), x => x == CrawlerChangeStatus.RolledBack);
        Assert.Equal("old-content", await File.ReadAllTextAsync(rollbackTargetPath));
        }
        finally
        {
            Directory.Delete(repoRoot, true);
        }
    }

    private static string CreateSandboxRepository()
    {
        var root = Path.Combine(Path.GetTempPath(), "source-governance-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "backend", "SimplerJiangAiAgent.Api", "Modules", "Stocks", "Services"));
        Directory.CreateDirectory(Path.Combine(root, "backend", "SimplerJiangAiAgent.Api.Tests"));

        File.WriteAllText(Path.Combine(root, "backend", "SimplerJiangAiAgent.Api", "Modules", "Stocks", "Services", "SinaCompanyNewsParser.cs"), "namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services; public sealed class SinaCompanyNewsParser { }");
        File.WriteAllText(Path.Combine(root, "backend", "SimplerJiangAiAgent.Api.Tests", "SinaCompanyNewsParserTests.cs"), "namespace SimplerJiangAiAgent.Api.Tests; public sealed class SinaCompanyNewsParserTests { }");
        return root;
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    private sealed class FakeLlmService : ILlmService
    {
        private readonly string _content;

        public FakeLlmService(string content)
        {
            _content = content;
        }

        public Task<LlmChatResult> ChatAsync(string provider, LlmChatRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LlmChatResult(_content));
        }
    }

    private sealed class FakeFileLogWriter : IFileLogWriter
    {
        public void Write(string category, string message)
        {
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, false);
        }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }

    private sealed class FakeCommandRunner : ICommandRunner
    {
        private readonly Dictionary<string, int> _exitCodes = new(StringComparer.OrdinalIgnoreCase);

        public void SetExitCode(string command, int exitCode)
        {
            _exitCodes[command] = exitCode;
        }

        public Task<int> RunAsync(string command, string? workingDirectory = null, int timeoutSeconds = 0, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_exitCodes.TryGetValue(command, out var exitCode) ? exitCode : 0);
        }
    }
}