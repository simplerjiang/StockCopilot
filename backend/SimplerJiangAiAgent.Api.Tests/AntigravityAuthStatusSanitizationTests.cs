using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Infrastructure.Logging;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class AntigravityAuthStatusSanitizationTests : IClassFixture<AntigravityAuthStatusSanitizationTests.Factory>
{
    private const string SensitiveErrorMessage = "OAuth failed uri=https://api.bltcy.ai/v1/chat/completions token=sk-test-secret";

    private readonly Factory _factory;

    public AntigravityAuthStatusSanitizationTests(Factory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AuthStatus_WhenCallbackTaskFaulted_DoesNotLeakGatewayUrlOrToken()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/admin/antigravity/auth-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("error", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api.bltcy.ai", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-test-secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[LLM-GATEWAY]", body, StringComparison.Ordinal);
        Assert.Contains("[SECRET]", body, StringComparison.Ordinal);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var dataRoot = Path.Combine(Path.GetTempPath(), "sjai-tests", nameof(AntigravityAuthStatusSanitizationTests), Guid.NewGuid().ToString("N"));

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
                services.RemoveAll<AntigravityOAuthService>();
                services.RemoveAll<IFileLogWriter>();
                services.AddSingleton<IFileLogWriter>(_ => new FakeLogWriter());
                services.AddSingleton(sp =>
                {
                    var service = new AntigravityOAuthService(
                        new HttpClient(),
                        sp.GetRequiredService<IFileLogWriter>(),
                        sp.GetRequiredService<IConfiguration>());
                    SetFaultedCallbackTask(service);
                    return service;
                });
            });
        }

        private static void SetFaultedCallbackTask(AntigravityOAuthService service)
        {
            var tcs = new TaskCompletionSource<(string code, string state)>();
            tcs.SetException(new InvalidOperationException(SensitiveErrorMessage));
            var field = typeof(AntigravityOAuthService).GetField(
                "_callbackTcs",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(service, tcs);
        }
    }

    private sealed class FakeLogWriter : IFileLogWriter
    {
        public void Write(string category, string message)
        {
        }
    }
}