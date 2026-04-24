using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SimplerJiangAiAgent.Api.Modules.Stocks.Contracts;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public class FinancialReportsEndpointTests : IClassFixture<FinancialReportsEndpointTests.Factory>
{
    private readonly Factory _factory;

    public FinancialReportsEndpointTests(Factory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetFinancialReports_WithFullQuery_BindsParametersAndReturns200()
    {
        var stub = new StubFinancialDataReadService();
        _factory.Stub = stub;
        var client = _factory.CreateClient();

        var response = await client.GetAsync(
            "/api/stocks/financial/reports?symbol=600519&reportType=annual&page=1&pageSize=10&sort=reportDate%20desc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("total", out _), "response missing 'total'");
        Assert.True(body.TryGetProperty("page", out _), "response missing 'page'");
        Assert.True(body.TryGetProperty("pageSize", out _), "response missing 'pageSize'");
        Assert.True(body.TryGetProperty("items", out _), "response missing 'items'");

        Assert.NotNull(stub.LastQuery);
        Assert.Equal("600519", stub.LastQuery!.Symbol);
        Assert.Equal("annual", stub.LastQuery.ReportType);
        Assert.Equal(1, stub.LastQuery.Page);
        Assert.Equal(10, stub.LastQuery.PageSize);
        Assert.Equal("reportDate desc", stub.LastQuery.Sort);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public StubFinancialDataReadService? Stub { get; set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                var existing = services.Where(d => d.ServiceType == typeof(IFinancialDataReadService)).ToList();
                foreach (var d in existing)
                {
                    services.Remove(d);
                }

                services.AddScoped<IFinancialDataReadService>(_ => Stub ?? new StubFinancialDataReadService());
            });
        }
    }

    public sealed class StubFinancialDataReadService : IFinancialDataReadService
    {
        public FinancialReportListQuery? LastQuery { get; private set; }

        public PagedResult<FinancialReportListItem> ListReports(FinancialReportListQuery query)
        {
            LastQuery = query;
            return new PagedResult<FinancialReportListItem>(Array.Empty<FinancialReportListItem>(), 0, query.Page, query.PageSize);
        }

        public List<Dictionary<string, object?>> GetReports(string symbol, int limit = 20) => new();
        public List<Dictionary<string, object?>> GetIndicators(string symbol, int limit = 20) => new();
        public List<Dictionary<string, object?>> GetDividends(string symbol) => new();
        public List<Dictionary<string, object?>> GetMarginTrading(string symbol, int limit = 100) => new();
        public List<FinancialCollectionLogEntry> GetCollectionLogs(string? symbol = null, int limit = 50) => new();
        public Dictionary<string, object?>? GetConfig() => null;
        public FinancialReportSummary? GetReportSummary(string symbol, int periods = 4) => null;
        public FinancialTrendSummary? GetTrendSummary(string symbol, int periods = 8) => null;
        public FinancialReportDetail? GetReportById(string id) => null;
        public void Dispose() { }
    }
}
