using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class RssMarketNewsParserTests
{
    [Fact]
    public void Parse_ShouldReadStandardRssItems()
    {
        const string xml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0">
          <channel>
            <item>
              <title>Fed outlook keeps markets cautious</title>
              <link>https://example.com/market-1</link>
              <guid>guid-1</guid>
              <pubDate>Thu, 12 Mar 2026 22:41:00 GMT</pubDate>
            </item>
          </channel>
        </rss>
        """;

        var result = RssMarketNewsParser.Parse(xml, "WSJ US Business", "wsj-us-business-rss", new DateTime(2026, 3, 13, 0, 0, 0, DateTimeKind.Utc));

        var item = Assert.Single(result);
        Assert.Equal("market", item.Level);
        Assert.Equal("WSJ US Business", item.Source);
        Assert.Equal("guid-1", item.ExternalId);
    }

    [Fact]
    public void Parse_ShouldReadAtomEntries()
    {
        const string xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <entry>
            <title>Global demand cools in March</title>
            <id>tag:example.com,2026:2</id>
            <updated>2026-03-13T08:00:00Z</updated>
            <link href="https://example.com/market-2" />
          </entry>
        </feed>
        """;

        var result = RssMarketNewsParser.Parse(xml, "NYT Business", "nyt-business-rss", new DateTime(2026, 3, 13, 9, 0, 0, DateTimeKind.Utc));

        var item = Assert.Single(result);
        Assert.Equal("https://example.com/market-2", item.Url);
        Assert.Equal("大盘环境", item.SectorName);
    }

    [Fact]
    public void Parse_ShouldReturnEmptyForInvalidXml()
    {
        var result = RssMarketNewsParser.Parse("not xml", "WSJ US Business", "wsj-us-business-rss", DateTime.UtcNow);

        Assert.Empty(result);
    }
}