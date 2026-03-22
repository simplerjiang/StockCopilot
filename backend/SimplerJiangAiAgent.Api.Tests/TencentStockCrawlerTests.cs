using System.Net;
using System.Text;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class TencentStockCrawlerTests
{
    [Fact]
    public async Task GetQuoteAsync_ShouldMapSupportedGlobalAliasSymbols()
    {
        string? requestedUri = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            requestedUri = request.RequestUri?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("v_usINX=\"200~标普500~.INX~6506.48~6606.49~6594.66~7040255867~0~0~6479.48~0~0~0~0~0~0~0~0~0~6555.18~0~0~0~0~0~0~0~0~0~~2026-03-20 17:05:20~-100.01~-1.51~6594.66~6473.52~USD~7040255867~45807283993518~~~~~~1.83~~~S&P 500 Index~~7002.28~4835.04~0~~~~-4.95~-1.90~ZS~~~-3.46~-5.83~-5.41~~~2.32~~~6506.48~~~\";", Encoding.UTF8, "text/plain")
            };
        });
        var crawler = new TencentStockCrawler(new HttpClient(handler));

        var result = await crawler.GetQuoteAsync("spx");

        Assert.Contains("q=usINX", requestedUri, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("spx", result.Symbol);
        Assert.Equal("标普500", result.Name);
        Assert.Equal(6506.48m, result.Price);
        Assert.Equal(-100.01m, result.Change);
        Assert.Equal(-1.51m, result.ChangePercent);
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
}