using SimplerJiangAiAgent.Api.Infrastructure.Serialization;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;
using System.Text.Json;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class EastmoneyAnnouncementParserTests
{
    [Fact]
    public void Parse_ShouldExtractAnnouncements()
    {
        const string json = """
        {
          "data": {
            "list": [
              {
                "art_code": "AN202603120001",
                "display_time": "2026-03-12 09:30:00:000",
                "title": "浦发银行：董事会决议公告"
              }
            ]
          }
        }
        """;

        var items = EastmoneyAnnouncementParser.Parse("sh600000", "浦发银行", "银行", json, new DateTime(2026, 3, 12, 10, 0, 0, DateTimeKind.Utc));

        var item = Assert.Single(items);
        Assert.Equal("sh600000", item.Symbol);
        Assert.Equal("announcement", item.Category);
        Assert.Equal("东方财富公告", item.Source);
        Assert.Equal("AN202603120001", item.ExternalId);
        Assert.Contains("AN202603120001", item.Url);
    }

    [Fact]
    public void Parse_ShouldStoreChinaDisplayTimeAsUtcAndRenderBackToChinaTime()
    {
        const string json = """
        {
          "data": {
            "list": [
              {
                "art_code": "AN202604241821562475",
                "display_time": "2026-04-24 19:49:32",
                "title": "贵州茅台：董事会决议公告"
              }
            ]
          }
        }
        """;

        var items = EastmoneyAnnouncementParser.Parse("sh600519", "贵州茅台", "白酒", json, new DateTime(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc));

        var item = Assert.Single(items);
        Assert.Equal(new DateTime(2026, 4, 24, 11, 49, 32, DateTimeKind.Utc), item.PublishTime);

        var options = new JsonSerializerOptions();
        options.Converters.Add(new ChinaDateTimeJsonConverter());
        var rendered = JsonSerializer.Serialize(item.PublishTime, options);

        Assert.Contains("2026-04-24T19:49:32", rendered);
        Assert.Contains("\\u002B08:00", rendered);
        Assert.DoesNotContain("2026-04-25T03:49:32", rendered);
    }
}