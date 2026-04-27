using System.Net;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StockCrawlerPlaceholderTests
{
    [Fact]
    public async Task BaiduGetQuoteAsync_ReturnsNull_WhenQuoteCrawlerIsNotImplemented()
    {
        var crawler = new BaiduStockCrawler();

        var result = await crawler.GetQuoteAsync("sh600519");

        Assert.Null(result);
    }

    [Theory]
    [InlineData("var hq_str_sh600519=\"\";")]
    [InlineData("var hq_str_sh600519=\",0,0,0,0,0\";")]
    public async Task SinaGetQuoteAsync_ReturnsNull_WhenResponseIsEmptyOrUnparseable(string response)
    {
        var crawler = new SinaStockCrawler(new HttpClient(new StaticHttpMessageHandler(response)));

        var result = await crawler.GetQuoteAsync("sh600519");

        Assert.Null(result);
    }

    private sealed class StaticHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _response;

        public StaticHttpMessageHandler(string response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response)
            });
        }
    }
}