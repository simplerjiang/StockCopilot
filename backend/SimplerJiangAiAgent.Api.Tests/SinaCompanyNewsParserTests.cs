using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public class SinaCompanyNewsParserTests
{
    [Fact]
    public void ParseCompanyNews_ShouldExtractItems()
    {
        var html = "<div class='datelist'><ul>" +
                   "<li><span>10:00</span><a href='https://example.com/a'>测试新闻A</a></li>" +
                   "<li><span>11:30</span><a href='https://example.com/b'>测试新闻B</a></li>" +
                   "</ul></div>";

        var list = SinaCompanyNewsParser.ParseCompanyNews(html);

        Assert.Equal(2, list.Count);
        Assert.Equal("测试新闻A", list[0].Title);
        Assert.Equal("新浪", list[0].Source);
    }

    [Fact]
    public void ParseCompanyNews_TimeOnly_ShouldUseChinaCurrentDateAsBase()
    {
        var html = "<div class='datelist'><ul>" +
                   "<li><span>00:10</span><a href='https://example.com/a'>跨日边界新闻</a></li>" +
                   "</ul></div>";
        var nowUtc = new DateTime(2026, 4, 24, 16, 30, 0, DateTimeKind.Utc);

        var list = SinaCompanyNewsParser.ParseCompanyNews(html, nowUtc);

        var item = Assert.Single(list);
        Assert.Equal("跨日边界新闻", item.Title);
        Assert.Equal(new DateTime(2026, 4, 24, 16, 10, 0, DateTimeKind.Utc), item.PublishedAt);
    }
}
