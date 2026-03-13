using Microsoft.EntityFrameworkCore;
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
            Options.Create(new StockSyncOptions()),
            NullLogger<LocalFactAiEnrichmentService>.Instance);

        await service.ProcessMarketPendingAsync();

        var stored = await dbContext.LocalSectorReports.SingleAsync();
        Assert.False(stored.IsAiProcessed);
        Assert.Equal("中性", stored.AiSentiment);
        Assert.Null(stored.TranslatedTitle);
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
}