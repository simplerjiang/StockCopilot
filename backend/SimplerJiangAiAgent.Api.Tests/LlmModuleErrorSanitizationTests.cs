using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Infrastructure.Logging;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class LlmModuleErrorSanitizationTests : IClassFixture<LlmModuleErrorSanitizationTests.Factory>
{
    private const string SensitiveErrorMessage = "OpenAI 请求失败: 401 uri=https://api.bltcy.ai/v1/chat/completions token=sk-test-secret";

    private readonly Factory _factory;

    public LlmModuleErrorSanitizationTests(Factory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ChatEndpoint_WhenProviderThrows_DoesNotLeakGatewayUrl()
    {
        _factory.LlmExceptionMessage = SensitiveErrorMessage;
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/llm/chat/openai", new { prompt = "hello" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        AssertSanitized(body);
    }

    [Fact]
    public async Task AdminTestEndpoint_WhenProviderThrows_DoesNotLeakGatewayUrl()
    {
        _factory.LlmExceptionMessage = SensitiveErrorMessage;
        var client = _factory.CreateClient();
        await AuthenticateAsync(client);

        var response = await client.PostAsJsonAsync("/api/admin/llm/test/openai", new { prompt = "hello" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        AssertSanitized(body);
    }

    [Fact]
    public async Task StreamEndpoint_WhenProviderThrows_DoesNotLeakGatewayUrlInSse()
    {
        _factory.StreamHttpExceptionMessage = SensitiveErrorMessage;
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/llm/chat/stream/openai", new { prompt = "hello" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data:", body, StringComparison.Ordinal);
        AssertSanitized(body);
    }

    private async Task AuthenticateAsync(HttpClient client)
    {
        var loginResponse = await client.PostAsJsonAsync("/api/admin/login", new
        {
            username = Factory.AdminUsername,
            password = Factory.AdminPassword
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = loginBody.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
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
        public const string AdminUsername = "test-admin";
        public const string AdminPassword = "test-password";

        public string LlmExceptionMessage { get; set; } = SensitiveErrorMessage;
        public string StreamHttpExceptionMessage { get; set; } = SensitiveErrorMessage;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var dataRoot = Path.Combine(Path.GetTempPath(), "sjai-tests", nameof(LlmModuleErrorSanitizationTests), Guid.NewGuid().ToString("N"));

            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:DataRootPath"] = dataRoot,
                    ["Admin:Username"] = AdminUsername,
                    ["Admin:Password"] = AdminPassword,
                    ["Admin:TokenExpiryMinutes"] = "5"
                });
            });
            builder.ConfigureServices(services =>
            {
                ApiTestDatabaseIsolation.UseIsolatedSqlite(services, dataRoot);
                services.RemoveAll<IHostedService>();
                services.RemoveAll<ILlmService>();
                services.RemoveAll<ILlmSettingsStore>();
                services.RemoveAll<IFileLogWriter>();
                services.RemoveAll<OpenAiProvider>();

                services.AddSingleton<ILlmService>(_ => new ThrowingLlmService(LlmExceptionMessage));
                services.AddSingleton<ILlmSettingsStore>(_ => new StubSettingsStore());
                services.AddSingleton<IFileLogWriter>(_ => new FakeLogWriter());
                services.AddSingleton(_ => new OpenAiProvider(
                    new HttpClient(new ThrowingHttpMessageHandler(() => StreamHttpExceptionMessage)),
                    new FakeLogWriter()));
            });
        }
    }

    private sealed class ThrowingLlmService : ILlmService
    {
        private readonly string _message;

        public ThrowingLlmService(string message)
        {
            _message = message;
        }

        public Task<LlmChatResult> ChatAsync(string provider, LlmChatRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(_message);
        }
    }

    private sealed class StubSettingsStore : ILlmSettingsStore
    {
        private readonly LlmProviderSettings _settings = new()
        {
            Provider = "openai",
            ProviderType = "openai",
            ApiKey = "sk-test-secret",
            BaseUrl = "https://api.bltcy.ai/v1",
            Model = "gpt-test",
            Enabled = true
        };

        public Task<IReadOnlyCollection<LlmProviderSettings>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<LlmProviderSettings>>(new[] { _settings });

        public Task<string> GetActiveProviderKeyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("openai");

        public Task<string> SetActiveProviderKeyAsync(string provider, CancellationToken cancellationToken = default)
            => Task.FromResult(provider);

        public Task<string> ResolveProviderKeyAsync(string? provider, CancellationToken cancellationToken = default)
            => Task.FromResult(string.IsNullOrWhiteSpace(provider) || string.Equals(provider, "active", StringComparison.OrdinalIgnoreCase) ? "openai" : provider);

        public Task<LlmProviderSettings?> GetProviderAsync(string provider, CancellationToken cancellationToken = default)
            => Task.FromResult<LlmProviderSettings?>(_settings);

        public Task<LlmProviderSettings> UpsertAsync(LlmProviderSettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult(settings);

        public Task<string> GetGlobalTavilyKeyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<(string Provider, string Model, int BatchSize)> GetNewsCleansingSettingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(("active", string.Empty, 12));

        public Task SetNewsCleansingSettingsAsync(string provider, string model, int batchSize, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<string> _messageFactory;

        public ThrowingHttpMessageHandler(Func<string> messageFactory)
        {
            _messageFactory = messageFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException(_messageFactory());
        }
    }

    private sealed class FakeLogWriter : IFileLogWriter
    {
        public void Write(string category, string message)
        {
        }
    }
}