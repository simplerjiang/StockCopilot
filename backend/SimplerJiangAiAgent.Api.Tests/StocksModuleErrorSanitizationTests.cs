using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SimplerJiangAiAgent.Api.Infrastructure;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StocksModuleErrorSanitizationTests : IClassFixture<StocksModuleErrorSanitizationTests.Factory>
{
    private const string SensitiveErrorMessage = "校准失败 uri=https://api.bltcy.ai/v1/chat/completions token=stock-secret-token";
    private const string SensitiveSupervisorErrorMessage = "worker failed uri=https://api.bltcy.ai/v1/chat/completions token=sk-test-secret";

    private readonly Factory _factory;

    public StocksModuleErrorSanitizationTests(Factory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task StockPublicEndpoint_WhenServiceThrows_DoesNotLeakGatewayUrl()
    {
        _factory.ExceptionMessage = SensitiveErrorMessage;
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/stocks/agents/signal-track-record?symbol=sh600519");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("api.bltcy.ai", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stock-secret-token", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[LLM-GATEWAY]", body, StringComparison.Ordinal);
        Assert.Contains("[SECRET]", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SupervisorStatus_WhenLastErrorSensitive_DoesNotLeakGatewayUrlOrToken()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/stocks/financial/worker/supervisor-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        AssertSanitized(body);
    }

    [Theory]
    [InlineData("/api/stocks/financial/worker/start")]
    [InlineData("/api/stocks/financial/worker/stop")]
    [InlineData("/api/stocks/financial/worker/restart")]
    public async Task SupervisorCommand_WhenStatusHasSensitiveError_DoesNotLeakGatewayUrlOrToken(string path)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(path, content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        AssertSanitized(body);
    }

    private static void AssertSanitized(string body)
    {
        Assert.DoesNotContain("api.bltcy.ai", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-test-secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[LLM-GATEWAY]", body, StringComparison.Ordinal);
        Assert.Contains("[SECRET]", body, StringComparison.Ordinal);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public string ExceptionMessage { get; set; } = SensitiveErrorMessage;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var dataRoot = Path.Combine(Path.GetTempPath(), "sjai-tests", nameof(StocksModuleErrorSanitizationTests), Guid.NewGuid().ToString("N"));

            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:DataRootPath"] = dataRoot
                });
            });
            builder.ConfigureServices(services =>
            {
                ApiTestDatabaseIsolation.UseIsolatedSqlite(services, dataRoot);
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IStockAgentReplayCalibrationService>();
                services.RemoveAll<IFinancialWorkerSupervisor>();
                services.AddScoped<IStockAgentReplayCalibrationService>(_ => new ThrowingReplayCalibrationService(ExceptionMessage));
                services.AddSingleton<IFinancialWorkerSupervisor>(_ => new SensitiveFinancialWorkerSupervisor(SensitiveSupervisorErrorMessage));
            });
        }
    }

    private sealed class SensitiveFinancialWorkerSupervisor : IFinancialWorkerSupervisor
    {
        private readonly string _message;

        public SensitiveFinancialWorkerSupervisor(string message)
        {
            _message = message;
        }

        public FinancialWorkerStatus GetStatus()
            => new("error", false, null, null, _message, null, null);

        public Task StartWorkerAsync(CancellationToken ct) => Task.CompletedTask;

        public Task StopWorkerAsync(CancellationToken ct) => Task.CompletedTask;

        public Task RestartWorkerAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class ThrowingReplayCalibrationService : IStockAgentReplayCalibrationService
    {
        private readonly string _message;

        public ThrowingReplayCalibrationService(string message)
        {
            _message = message;
        }

        public Task<StockAgentReplayBaselineDto> BuildBaselineAsync(string? symbol, int take, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(_message);
        }
    }
}