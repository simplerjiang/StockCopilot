using System.Net;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SimplerJiangAiAgent.Api.Infrastructure;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class FinancialWorkerSupervisorServiceTests
{
    private const string SensitiveErrorMessage = "heartbeat failed uri=https://api.bltcy.ai/v1/chat/completions token=sk-test-secret";

    [Fact]
    public void GetStatus_WhenLastErrorContainsSecrets_ReturnsSanitizedError()
    {
        var supervisor = CreateSupervisor(SensitiveErrorMessage);
        SetPrivateField(supervisor, "_lastError", SensitiveErrorMessage);

        var status = supervisor.GetStatus();

        AssertSanitized(status.LastError);
    }

    [Fact]
    public async Task HeartbeatFailure_WhenHttpRequestExceptionContainsSecrets_StoresSanitizedError()
    {
        var supervisor = CreateSupervisor(SensitiveErrorMessage);
        SetPrivateField(supervisor, "_state", "running");

        var method = typeof(FinancialWorkerSupervisorService).GetMethod(
            "PerformHeartbeatAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task)method.Invoke(supervisor, new object[] { CancellationToken.None })!;
        await task;

        var status = supervisor.GetStatus();
        AssertSanitized(status.LastError);
    }

    private static FinancialWorkerSupervisorService CreateSupervisor(string exceptionMessage)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FinancialWorker:BaseUrl"] = "http://127.0.0.1:5120"
            })
            .Build();

        return new FinancialWorkerSupervisorService(
            new ThrowingHttpClientFactory(exceptionMessage),
            configuration,
            NullLogger<FinancialWorkerSupervisorService>.Instance);
    }

    private static void SetPrivateField<TValue>(FinancialWorkerSupervisorService supervisor, string fieldName, TValue value)
    {
        var field = typeof(FinancialWorkerSupervisorService).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(supervisor, value);
    }

    private static void AssertSanitized(string? value)
    {
        Assert.False(string.IsNullOrWhiteSpace(value));
        Assert.DoesNotContain("api.bltcy.ai", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-test-secret", value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[LLM-GATEWAY]", value, StringComparison.Ordinal);
        Assert.Contains("[SECRET]", value, StringComparison.Ordinal);
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        private readonly string _message;

        public ThrowingHttpClientFactory(string message)
        {
            _message = message;
        }

        public HttpClient CreateClient(string name)
            => new(new ThrowingHandler(_message));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly string _message;

        public ThrowingHandler(string message)
        {
            _message = message;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException(_message, inner: null, statusCode: HttpStatusCode.BadGateway);
    }
}