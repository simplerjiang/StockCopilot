using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class SinaSectorNewsSearchParserTests
{
    [Fact]
    public void Parse_ShouldReadBoxResultMarkup()
    {
        const string html = """
        <html>
          <body>
            <div class="box-result clearfix">
              <h2><a href="https://finance.sina.com.cn/stock/2026-03-13/doc-demo.shtml" target="_blank">半导体板块午后回暖</a></h2>
              <h2><span class="fgray_time">第一财经 2小时前</span></h2>
            </div>
          </body>
        </html>
        """;

        var crawledAt = new DateTime(2026, 3, 13, 6, 0, 0, DateTimeKind.Utc);
        var result = SinaSectorNewsSearchParser.Parse("sh600000", "半导体", html, crawledAt);

        var item = Assert.Single(result);
        Assert.Equal("sector", item.Level);
        Assert.Equal("半导体", item.SectorName);
        Assert.Equal("第一财经", item.Source);
        Assert.Equal("sina-sector-search", item.SourceTag);
        Assert.Equal("https://finance.sina.com.cn/stock/2026-03-13/doc-demo.shtml", item.Url);
    }

    [Fact]
    public void Parse_ShouldSupportFallbackHeadingStructure()
    {
        const string html = """
        <html>
          <body>
            <h2><a href="https://finance.sina.com.cn/roll/2026-03-13/doc-bank.shtml" target="_blank">银行板块估值修复</a></h2>
            <h2><span class="fgray_time">界面新闻 03月13日 10:30</span></h2>
          </body>
        </html>
        """;

        var result = SinaSectorNewsSearchParser.Parse("sh600000", "银行", html, new DateTime(2026, 3, 13, 4, 0, 0, DateTimeKind.Utc));

        var item = Assert.Single(result);
        Assert.Equal("银行板块估值修复", item.Title);
        Assert.Equal("界面新闻", item.Source);
        Assert.Equal("https://finance.sina.com.cn/roll/2026-03-13/doc-bank.shtml", item.ExternalId);
    }

    [Fact]
    public void Parse_ShouldReturnEmptyForHtmlWithoutResults()
    {
        const string html = "<html><body><p>暂无结果</p></body></html>";

        var result = SinaSectorNewsSearchParser.Parse("sh600000", "银行", html, DateTime.UtcNow);

        Assert.Empty(result);
    }
}