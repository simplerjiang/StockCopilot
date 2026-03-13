using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class EastmoneySectorSearchParserTests
{
    [Fact]
    public void Parse_ShouldReadDataDataShape()
    {
        const string json = """
        {
          "Data": {
            "Data": [
              {
                "Title": "半导体板块午后走强",
                "PublishTime": "2026-03-13 13:45:00",
                "Url": "https://finance.eastmoney.com/a/20260313/1.html",
                "InfoCode": "INF001",
                "SourceName": "东方财富网"
              }
            ]
          }
        }
        """;

        var result = EastmoneySectorSearchParser.Parse("sh600000", "半导体", json, new DateTime(2026, 3, 13, 14, 0, 0, DateTimeKind.Utc));

        var item = Assert.Single(result);
        Assert.Equal("sector", item.Level);
        Assert.Equal("半导体", item.SectorName);
        Assert.Equal("东方财富网", item.Source);
        Assert.Equal("INF001", item.ExternalId);
    }

    [Fact]
    public void Parse_ShouldReadDataListShape()
    {
        const string json = """
        {
          "data": {
            "List": [
              {
                "title": "银行板块估值修复",
                "publishTime": "2026-03-13 09:30:00",
                "url": "https://finance.eastmoney.com/a/20260313/2.html",
                "code": "INF002"
              }
            ]
          }
        }
        """;

        var result = EastmoneySectorSearchParser.Parse("sh600000", "银行", json, new DateTime(2026, 3, 13, 10, 0, 0, DateTimeKind.Utc));

        var item = Assert.Single(result);
        Assert.Equal("银行板块估值修复", item.Title);
        Assert.Equal("eastmoney-sector-search", item.SourceTag);
    }

    [Fact]
    public void Parse_ShouldReturnEmptyForHtmlResponse()
    {
        const string html = "<html><body>redirect</body></html>";

        var result = EastmoneySectorSearchParser.Parse("sh600000", "银行", html, DateTime.UtcNow);

        Assert.Empty(result);
    }
}