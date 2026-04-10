using System.Net;
using System.Net.Http;
using System.Text.Json;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Infrastructure.Logging;
using Xunit;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class OllamaProviderTests
{
    [Fact]
    public async Task ChatAsync_UsesNativeChatEndpointAndIncludesRuntimeOptions()
    {
        var handler = new CaptureHandler();
        var httpClient = new HttpClient(handler);
        var provider = new OllamaProvider(httpClient, new FakeLogWriter());
        var settings = new LlmProviderSettings
        {
            Provider = "ollama",
            ProviderType = "ollama",
            BaseUrl = "http://localhost:11434/v1",
            Model = "gemma4:e2b",
            SystemPrompt = "你是交易助手",
            ForceChinese = true,
            OllamaNumCtx = 4096,
            OllamaKeepAlive = "10",
            OllamaNumPredict = -1,
            OllamaTemperature = 0.25,
            OllamaTopK = 32,
            OllamaTopP = 0.85,
            OllamaMinP = 0.05,
            OllamaStop = ["###", "Observation:"],
            OllamaThink = true
        };

        var result = await provider.ChatAsync(
            settings,
            new LlmChatRequest("hello", "gemma4:e2b", 0.15, ResponseFormat: LlmResponseFormats.Json),
            CancellationToken.None);

        Assert.Equal("ok", result.Content);
        Assert.Equal("http://localhost:11434/api/chat", handler.LastRequestUri);
        Assert.NotNull(handler.LastRequestBody);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;
        Assert.Equal("gemma4:e2b", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.True(root.GetProperty("think").GetBoolean());
        Assert.Equal("10m", root.GetProperty("keep_alive").GetString());
        Assert.Equal(LlmResponseFormats.Json, root.GetProperty("format").GetString());

        var options = root.GetProperty("options");
        Assert.Equal(4096, options.GetProperty("num_ctx").GetInt32());
        Assert.Equal(-1, options.GetProperty("num_predict").GetInt32());
        Assert.Equal(32, options.GetProperty("top_k").GetInt32());
        Assert.Equal(0.85, options.GetProperty("top_p").GetDouble());
        Assert.Equal(0.05, options.GetProperty("min_p").GetDouble());
        Assert.Equal(0.15, options.GetProperty("temperature").GetDouble());

        var stop = options.GetProperty("stop");
        Assert.Equal(JsonValueKind.Array, stop.ValueKind);
        Assert.Equal("###", stop[0].GetString());
        Assert.Equal("Observation:", stop[1].GetString());
    }

    [Fact]
    public async Task ChatAsync_UsesDefaultOllamaRuntimeSettingsWhenUnset()
    {
        var handler = new CaptureHandler();
        var httpClient = new HttpClient(handler);
        var provider = new OllamaProvider(httpClient, new FakeLogWriter());
        var settings = new LlmProviderSettings
        {
            Provider = "ollama",
            ProviderType = "ollama",
            BaseUrl = "http://localhost:11434",
            Model = "gemma4:e2b"
        };

        var result = await provider.ChatAsync(settings, new LlmChatRequest("hello", null, null), CancellationToken.None);

        Assert.Equal("ok", result.Content);
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("think").GetBoolean());
        Assert.Equal("5m", root.GetProperty("keep_alive").GetString());

        var options = root.GetProperty("options");
        Assert.Equal(131072, options.GetProperty("num_ctx").GetInt32());
        Assert.Equal(2048, options.GetProperty("num_predict").GetInt32());
        Assert.Equal(0.3, options.GetProperty("temperature").GetDouble());
        Assert.Equal(64, options.GetProperty("top_k").GetInt32());
        Assert.Equal(0.95, options.GetProperty("top_p").GetDouble());
        Assert.Equal(0, options.GetProperty("min_p").GetDouble());
        Assert.False(options.TryGetProperty("stop", out _));
    }

    [Theory]
    [InlineData(null, "5m")]
    [InlineData("", "5m")]
    [InlineData("-1", "5m")]
    [InlineData("0", "0")]
    [InlineData("10", "10m")]
    [InlineData("30s", "30s")]
    public void ResolveKeepAlive_NormalizesLegacyAndBareNumericValues(string? input, string expected)
    {
        Assert.Equal(expected, OllamaRuntimeDefaults.ResolveKeepAlive(input));
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }
        public string? LastRequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.AbsoluteUri;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{" + "\"message\":{\"content\":\"ok\"},\"done\":true}")
            };
        }
    }

    private sealed class FakeLogWriter : IFileLogWriter
    {
        public void Write(string category, string message)
        {
        }
    }
}