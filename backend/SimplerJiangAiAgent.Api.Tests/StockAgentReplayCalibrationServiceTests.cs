using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Storage;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StockAgentReplayCalibrationServiceTests
{
    [Fact]
    public async Task BuildBaselineAsync_ShouldComputeReplayMetricsFromHistoryAndKlines()
    {
        await using var dbContext = CreateDbContext();
        dbContext.StockAgentAnalysisHistories.Add(new StockAgentAnalysisHistory
        {
            Symbol = "sh600000",
            Name = "浦发银行",
            Interval = "day",
            ResultJson = """
            {
              "agents": [
                {
                  "agentId": "commander",
                  "traceId": "trace-001",
                  "data": {
                    "agent": "commander",
                    "summary": "偏多",
                    "directional_bias": "看多",
                    "confidence_score": 72,
                    "probabilities": { "bull": 62, "base": 23, "bear": 15 },
                    "revision": { "required": true, "reason": "证据改善" },
                    "evidence": [
                      {
                        "point": "公告",
                        "title": "浦发银行公告",
                        "source": "上交所公告",
                        "publishedAt": "2026-03-10 09:00",
                        "url": "https://example.com/a",
                        "localFactId": 1
                      }
                    ]
                  }
                }
              ]
            }
            """,
            CreatedAt = new DateTime(2026, 3, 10, 10, 0, 0)
        });
        dbContext.KLinePoints.AddRange(
            new KLinePointEntity { Symbol = "sh600000", Interval = "day", Date = new DateTime(2026, 3, 10), Open = 10m, Close = 10m, High = 10.1m, Low = 9.9m, Volume = 1000 },
            new KLinePointEntity { Symbol = "sh600000", Interval = "day", Date = new DateTime(2026, 3, 11), Open = 10.1m, Close = 10.2m, High = 10.3m, Low = 10m, Volume = 1000 },
            new KLinePointEntity { Symbol = "sh600000", Interval = "day", Date = new DateTime(2026, 3, 13), Open = 10.2m, Close = 10.5m, High = 10.6m, Low = 10.1m, Volume = 1000 },
            new KLinePointEntity { Symbol = "sh600000", Interval = "day", Date = new DateTime(2026, 3, 17), Open = 10.4m, Close = 10.8m, High = 10.9m, Low = 10.3m, Volume = 1000 },
            new KLinePointEntity { Symbol = "sh600000", Interval = "day", Date = new DateTime(2026, 3, 24), Open = 10.7m, Close = 11m, High = 11.2m, Low = 10.6m, Volume = 1000 });
        await dbContext.SaveChangesAsync();

        var runtimePaths = CreateRuntimePaths();
        Directory.CreateDirectory(runtimePaths.LogsPath);
        await File.WriteAllLinesAsync(Path.Combine(runtimePaths.LogsPath, "llm-requests.txt"), new[]
        {
            "2026-03-10 10:00:00.000 [LLM-AUDIT] traceId=trace-001 stage=request",
            "2026-03-10 10:00:01.000 [LLM] parse_error agent=commander message=bad_json",
            "2026-03-10 10:00:02.000 [LLM-AUDIT] traceId=trace-001 stage=response"
        });

        var service = new StockAgentReplayCalibrationService(dbContext, runtimePaths);
        var baseline = await service.BuildBaselineAsync("sh600000", 20);

        Assert.Equal(1, baseline.SampleCount);
        Assert.True(baseline.TraceableEvidenceRate > 0m);
        Assert.True(baseline.ParseRepairRate > 0m);
        Assert.Contains(baseline.Horizons, item => item.HorizonDays == 1 && item.SampleCount == 1);
        Assert.Equal("trace-001", baseline.Samples[0].TraceId);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    private static AppRuntimePaths CreateRuntimePaths()
    {
        return new AppRuntimePaths(new FakeHostEnvironment(), new ConfigurationBuilder().Build());
    }

    private sealed class FakeHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "SimplerJiangAiAgent.Api.Tests";
        public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "replay-calibration-tests", Guid.NewGuid().ToString("N"));
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}