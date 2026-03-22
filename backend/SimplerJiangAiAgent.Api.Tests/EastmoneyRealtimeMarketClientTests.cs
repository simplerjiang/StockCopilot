using System.Net;
using System.Text;
using SimplerJiangAiAgent.Api.Modules.Market.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class EastmoneyRealtimeMarketClientTests
{
    [Fact]
    public async Task GetBatchQuotesAsync_ShouldParseQuotesInRequestedOrder()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""
        {
          "data": {
            "diff": [
              { "f2": 32.43, "f3": -3.57, "f4": -1.20, "f6": 4664584692.4, "f8": 9.13, "f9": 50.58, "f10": 1.17, "f12": "000021", "f13": 0, "f14": "深科技", "f15": 33.70, "f16": 31.80, "f124": 1773904299 },
              { "f2": 10.32, "f3": -0.48, "f4": -0.05, "f6": 784461845.0, "f8": 0.23, "f9": 6.87, "f10": 0.96, "f12": "600000", "f13": 1, "f14": "浦发银行", "f15": 10.40, "f16": 10.25, "f124": 1773904303 }
            ]
          }
        }
        """));
        var client = new EastmoneyRealtimeMarketClient(new HttpClient(handler));

        var result = await client.GetBatchQuotesAsync(["sh600000", "sz000021"]);

        Assert.Equal(2, result.Count);
        Assert.Equal("sh600000", result[0].Symbol);
        Assert.Equal("浦发银行", result[0].Name);
        Assert.Equal(10.32m, result[0].Price);
        Assert.Equal("sz000021", result[1].Symbol);
        Assert.Equal(4664584692.4m, result[1].TurnoverAmount);
    }

    [Fact]
    public async Task GetBatchQuotesAsync_ShouldMapSupportedGlobalIndices()
    {
      string? requestedUri = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
        requestedUri = Uri.UnescapeDataString(request.RequestUri?.ToString() ?? string.Empty);

            return JsonResponse("""
            {
              "data": {
                "diff": [
                  { "f2": 25277.32, "f3": -0.88, "f4": -223.26, "f6": 0, "f8": 0, "f9": 0, "f10": 0, "f12": "HSI", "f13": 100, "f14": "恒生指数", "f15": 25388.16, "f16": 25174.15, "f124": 1773994150 },
                  { "f2": 4872.38, "f3": -2.48, "f4": -123.9, "f6": 0, "f8": 0, "f9": 0, "f10": 0, "f12": "HSTECH", "f13": 124, "f14": "恒生科技指数", "f15": 4933.2, "f16": 4820.12, "f124": 1773994150 },
                  { "f2": 6506.48, "f3": -1.51, "f4": -100.01, "f6": 0, "f8": 0, "f9": 0, "f10": 0, "f12": "SPX", "f13": 100, "f14": "标普500", "f15": 6528.9, "f16": 6482.1, "f124": 1774036786 }
                ]
              }
            }
            """);
        });
        var client = new EastmoneyRealtimeMarketClient(new HttpClient(handler));

        var result = await client.GetBatchQuotesAsync(["hsi", "hstech", "spx"]);

  Assert.Contains("100.HSI", requestedUri);
  Assert.Contains("124.HSTECH", requestedUri);
  Assert.Contains("100.SPX", requestedUri);
        Assert.Equal(3, result.Count);
        Assert.Equal("hsi", result[0].Symbol);
        Assert.Equal("恒生指数", result[0].Name);
        Assert.Equal("hstech", result[1].Symbol);
        Assert.Equal("标普500", result[2].Name);
    }

    [Fact]
    public async Task GetMainCapitalFlowAsync_ShouldParseLatestPoint()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""
        {
          "data": {
            "klines": [
              "2026-03-19 14:59,-107903802133.0,88049831563.0,19853970602.0,-46362068397.0,-61541733736.0",
              "2026-03-19 15:00,-107904203519.0,88056574114.0,19847629437.0,-46361324424.0,-61542879095.0"
            ]
          }
        }
        """));
        var client = new EastmoneyRealtimeMarketClient(new HttpClient(handler));

        var result = await client.GetMainCapitalFlowAsync();

        Assert.NotNull(result);
        Assert.Equal("亿元", result!.AmountUnit);
        Assert.Equal(2, result.Points.Count);
        Assert.Equal(-1079.04m, result.MainNetInflow);
        Assert.Equal(880.57m, result.SmallOrderNetInflow);
        Assert.Equal(-615.43m, result.SuperLargeOrderNetInflow);
    }

    [Fact]
    public async Task GetNorthboundFlowAsync_ShouldSkipPlaceholderRowsAndParseLatestPoint()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""
        {
          "data": {
            "s2nDate": "03-19",
            "s2n": [
              "14:59,-,-,-,-,-",
              "15:00,123400.00,5200000.00,567800.00,4200000.00,691200.00"
            ]
          }
        }
        """));
        var client = new EastmoneyRealtimeMarketClient(new HttpClient(handler));

        var result = await client.GetNorthboundFlowAsync();

        Assert.NotNull(result);
        Assert.Equal("03-19", result!.TradingDateLabel);
        Assert.Single(result.Points);
        Assert.Equal(12.34m, result.ShanghaiNetInflow);
        Assert.Equal(420m, result.ShenzhenBalance);
        Assert.Equal(69.12m, result.TotalNetInflow);
    }

    [Fact]
    public async Task GetBreadthDistributionAsync_ShouldParseJsonpDistribution()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("callbackdata7930743({\"data\":{\"qdate\":20260319,\"fenbu\":[{\"-11\":5},{\"-10\":15},{\"-1\":402},{\"0\":18},{\"1\":160},{\"10\":12},{\"11\":28}]}});", Encoding.UTF8, "application/javascript")
        });
        var client = new EastmoneyRealtimeMarketClient(new HttpClient(handler));

        var result = await client.GetBreadthDistributionAsync();

        Assert.NotNull(result);
        Assert.Equal(new DateOnly(2026, 3, 19), result!.TradingDate);
        Assert.Equal(200, result.Advancers);
        Assert.Equal(422, result.Decliners);
        Assert.Equal(18, result.FlatCount);
        Assert.Equal(40, result.LimitUpCount);
        Assert.Equal(20, result.LimitDownCount);
        Assert.Equal("跌停", result.Buckets[0].Label);
        Assert.Equal("涨停", result.Buckets[^1].Label);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
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