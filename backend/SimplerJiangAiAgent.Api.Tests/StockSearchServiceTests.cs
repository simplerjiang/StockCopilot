using System.Net;
using System.Text;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public class StockSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_ShouldNormalizeStNameSpacingAndMatchSpacedStQuery()
    {
        var raw = "v_hint=\"sh~600001~*ST \\u56db\\u73af~SSTSH\";";
        var service = CreateService(raw);

        var results = await service.SearchAsync("*ST 四环");

        var result = Assert.Single(results);
        Assert.Equal("sh600001", result.Symbol);
        Assert.Equal("*ST四环", result.Name);
    }

    [Fact]
    public async Task SearchAsync_ShouldRejectLongInitialsOnlyQuery()
    {
        var raw = "v_hint=\"sh~601377~\\u5174\\u4e1a\\u8bc1\\u5238~xyzq~GP^sz~000678~\\u8944\\u9633\\u8f74\\u627f~xyzc~GP^sz~002388~\\u65b0\\u4e9a\\u5236\\u7a0b~xyzc~GP\"";
        var service = CreateService(raw);

        var results = await service.SearchAsync("xyz", 10);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ShouldAllowShortPinyinInitialsQuery()
    {
        var raw = "v_hint=\"sz~000001~\\u5e73\\u5b89\\u94f6\\u884c~payh~GP^sh~601318~\\u4e2d\\u56fd\\u5e73\\u5b89~zgpa~GP\"";
        var service = CreateService(raw);

        var results = await service.SearchAsync("pa", 10);

        var first = Assert.Single(results);
        Assert.Equal("000001", first.Code);
        Assert.Equal("sz000001", first.Symbol);
        Assert.Equal("\u5e73\u5b89\u94f6\u884c", first.Name);
    }

    [Theory]
    [InlineData("600519")]
    [InlineData("\u8d35\u5dde")]
    [InlineData("\u8305\u53f0")]
    public async Task SearchAsync_ShouldKeepCodeAndChineseNameQueries(string query)
    {
        var raw = "v_hint=\"sh~600519~\\u8d35\\u5dde\\u8305\\u53f0~gzmt~GP^sh~600518~\\u5eb7\\u7f8e\\u836f\\u4e1a~kmyy~GP\"";
        var service = CreateService(raw);

        var results = await service.SearchAsync(query, 10);

        var first = Assert.Single(results);
        Assert.Equal("600519", first.Code);
        Assert.Equal("sh600519", first.Symbol);
        Assert.Equal("\u8d35\u5dde\u8305\u53f0", first.Name);
    }

    [Fact]
    public async Task SearchAsync_ShouldKeepNumericCodePrefixQuery()
    {
        var raw = "v_hint=\"sz~000001~\\u5e73\\u5b89\\u94f6\\u884c~payh~GP^sz~000002~\\u4e07\\u79d1A~wka~GP\"";
        var service = CreateService(raw);

        var results = await service.SearchAsync("000001", 10);

        var first = Assert.Single(results);
        Assert.Equal("000001", first.Code);
        Assert.Equal("sz000001", first.Symbol);
        Assert.Equal("\u5e73\u5b89\u94f6\u884c", first.Name);
    }

    [Fact]
    public async Task SearchAsync_ShouldParseTencentSmartboxPayload()
    {
        var raw = "v_hint=\"sh~000680~\\u79d1\\u521b\\u7efc\\u6307~kczz~ZS^sh~000688~\\u79d1\\u521b50~kc50~ZS\"";
        var service = CreateService(raw);

        var results = await service.SearchAsync("科创", 10);

        Assert.Equal(2, results.Count);
        var first = results[0];
        Assert.Equal("000680", first.Code);
        Assert.Equal("sh000680", first.Symbol);
        Assert.Equal("科创综指", first.Name);
    }

    private static StockSearchService CreateService(string raw)
    {
        var handler = new FakeHttpMessageHandler(raw);
        var httpClient = new HttpClient(handler);
        return new StockSearchService(httpClient);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _response;

        public FakeHttpMessageHandler(string response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StringContent(_response, Encoding.UTF8, "text/plain");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
