using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StockAgentLocalFactProjectionTests
{
    [Fact]
    public void Create_ShouldStripAiLabelsButKeepTranslatedTitle()
    {
        var source = new LocalFactPackageDto(
            "sh600000",
            "浦发银行",
            "银行",
            new[]
            {
                new LocalNewsItemDto(
                    "Bank stocks rise after policy support",
                    "政策支持后银行股走强",
                    "WSJ US Business",
                    "wsj-us-business-rss",
                    "company_news",
                    "利好",
                    new DateTime(2026, 3, 13, 8, 0, 0),
                    new DateTime(2026, 3, 13, 8, 5, 0),
                    "https://example.com/a",
                    "板块:银行",
                    new[] { "政策红利", "资金面" })
            },
            Array.Empty<LocalNewsItemDto>(),
            Array.Empty<LocalNewsItemDto>());

        var projected = StockAgentLocalFactProjection.Create(source);

        Assert.Single(projected.StockNews);
        Assert.Equal("政策支持后银行股走强", projected.StockNews[0].TranslatedTitle);
        var json = System.Text.Json.JsonSerializer.Serialize(projected);
        Assert.DoesNotContain("AiTarget", json);
        Assert.DoesNotContain("AiTags", json);
        Assert.DoesNotContain("Sentiment", json);
    }
}