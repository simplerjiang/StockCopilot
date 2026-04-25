using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Infrastructure.Logging;
using Xunit;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class OpenAiProviderTests
{
    [Theory]
    [InlineData("```json\n{\"key\":\"value\"}\n```", "{\"key\":\"value\"}")]
    [InlineData("```\nplain text\n```", "plain text")]
    [InlineData("normal response without fences", "normal response without fences")]
    [InlineData("```JSON\n{\"a\":1}\n```", "{\"a\":1}")]
    [InlineData("mixed content with ```json\n{}\n``` in middle", "mixed content with ```json\n{}\n``` in middle")]
    [InlineData("```json\n{\"a\":1}\n```\n", "{\"a\":1}")] // trailing newline after closing fence
    [InlineData("```json\n{\"a\":1}```", "{\"a\":1}")] // no newline before closing fence
    [InlineData("```json\n{\"a\":1}", "{\"a\":1}")] // no closing fence at all
    [InlineData("```\n{\"a\":1}", "{\"a\":1}")] // no lang tag, no closing fence
    public void StripMarkdownCodeFences_HandlesVariants(string input, string expected)
    {
        var result = OpenAiProvider.StripMarkdownCodeFences(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ChatAsync_IncludesSystemPromptWhenProvided()
    {
        var handler = new CaptureHandler();
        var httpClient = new HttpClient(handler);
        var provider = new OpenAiProvider(httpClient, new FakeLogWriter());
        var settings = new LlmProviderSettings
        {
            Provider = "openai",
            ApiKey = "key",
            BaseUrl = "https://api.example.com/v1",
            SystemPrompt = "你是股票助手",
            ForceChinese = true
        };

        var result = await provider.ChatAsync(settings, new LlmChatRequest("hello", "gemini-3-pro-preview", 0.1, true), CancellationToken.None);

        Assert.Equal("ok", result.Content);
        Assert.NotNull(handler.LastRequestBody);
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var contents = doc.RootElement.GetProperty("contents");
        var parts = contents[0].GetProperty("parts");
        var text = parts[0].GetProperty("text").GetString() ?? string.Empty;
        Assert.Contains("hello", text);
        var systemInstruction = doc.RootElement.GetProperty("system_instruction");
        var systemParts = systemInstruction.GetProperty("parts");
        var systemText = systemParts[0].GetProperty("text").GetString() ?? string.Empty;
        Assert.Contains("你是股票助手", systemText);
        Assert.Contains("请使用中文回答", systemText);
        var tools = doc.RootElement.GetProperty("tools");
        Assert.True(tools.GetArrayLength() > 0);
    }

    [Fact]
    public async Task StreamChatAsync_ParsesSseChunks()
    {
        var handler = new CaptureHandler();
        var httpClient = new HttpClient(handler);
        var provider = new OpenAiProvider(httpClient, new FakeLogWriter());
        var settings = new LlmProviderSettings
        {
            Provider = "openai",
            ApiKey = "key",
            BaseUrl = "https://www.dmxapi.cn/v1",
            SystemPrompt = "你是股票助手",
            ForceChinese = true
        };

        var chunks = new List<string>();
        await foreach (var chunk in provider.StreamChatAsync(settings, new LlmChatRequest("hello", "gemini-3-pro-preview", 0.1, true)))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(new[] { "你", "好" }, chunks);
    }

    [Fact]
    public async Task StreamChatAsync_IgnoresNullPartsPayload_WithoutThrowing()
    {
        var handler = new NullPartsStreamHandler();
        var httpClient = new HttpClient(handler);
        var provider = new OpenAiProvider(httpClient, new FakeLogWriter());
        var settings = new LlmProviderSettings
        {
            Provider = "openai",
            ApiKey = "key",
            BaseUrl = "https://www.dmxapi.cn/v1",
            SystemPrompt = "你是股票助手",
            ForceChinese = true
        };

        var chunks = new List<string>();
        await foreach (var chunk in provider.StreamChatAsync(settings, new LlmChatRequest("hello", "gemini-3-pro-preview", 0.1, true)))
        {
            chunks.Add(chunk);
        }

        Assert.Empty(chunks);
    }

    [Fact]
    public async Task ChatAsync_WhenGatewayReturnsHtml_ShouldThrowReadableError()
    {
        var handler = new HtmlResponseHandler();
        var httpClient = new HttpClient(handler);
        var provider = new OpenAiProvider(httpClient, new FakeLogWriter());
        var settings = new LlmProviderSettings
        {
            Provider = "openai",
            ApiKey = "key",
            BaseUrl = "https://api.example.com/v1"
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ChatAsync(settings, new LlmChatRequest("hello", "gpt-4o-mini", 0.1, false), CancellationToken.None));

        Assert.Contains("非 JSON 内容", ex.Message);
    }

    [Fact]
    public async Task ChatAsync_AppendsV1WhenBaseUrlOmitsIt()
    {
        var handler = new CaptureHandler();
        var httpClient = new HttpClient(handler);
        var provider = new OpenAiProvider(httpClient, new FakeLogWriter());
        var settings = new LlmProviderSettings
        {
            Provider = "openai",
            ApiKey = "key",
            BaseUrl = "https://api.example.com"
        };

        await provider.ChatAsync(settings, new LlmChatRequest("hello", "gpt-4o-mini", 0.1, false), CancellationToken.None);

        Assert.Equal("https://api.example.com/v1/chat/completions", handler.LastRequestUri);
    }

    [Fact]
    public async Task ChatAsync_WhenHttpRequestFails_ShouldThrowReadableNetworkError()
    {
        var handler = new ThrowingHandler();
        var httpClient = new HttpClient(handler);
        var logs = new CollectingLogWriter();
        var provider = new OpenAiProvider(httpClient, logs);
        var settings = new LlmProviderSettings
        {
            Provider = "openai",
            ApiKey = "key",
            BaseUrl = "https://api.example.com"
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ChatAsync(settings, new LlmChatRequest("hello", "gpt-4o-mini", 0.1, false), CancellationToken.None));

        Assert.Contains("请求发送失败", ex.Message);
        Assert.Contains("连接被拒绝", ex.Message);
        Assert.Contains(logs.Entries, item => item.Contains("uri=https://api.example.com/v1/chat/completions", StringComparison.Ordinal));
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }
        public string? LastRequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            LastRequestUri = request.RequestUri?.AbsoluteUri;

            if (request.RequestUri?.AbsolutePath.Contains("streamGenerateContent", StringComparison.OrdinalIgnoreCase) == true)
            {
                var sse = "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"你\"}]}}]}\n\n" +
                          "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"好\"}]}}]}\n\n" +
                          "data: [DONE]\n\n";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(sse)
                };
            }

            var responseJson = request.RequestUri?.AbsolutePath.Contains("generateContent", StringComparison.OrdinalIgnoreCase) == true
                ? "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"ok\"}]}}]}"
                : "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            };
        }
    }

    private sealed class FakeLogWriter : IFileLogWriter
    {
        public void Write(string category, string message)
        {
        }
    }

    private sealed class CollectingLogWriter : IFileLogWriter
    {
        public List<string> Entries { get; } = new();

        public void Write(string category, string message)
        {
            Entries.Add($"{category}:{message}");
        }
    }

    private sealed class NullPartsStreamHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var sse = "data: {\"candidates\":[{\"content\":{\"parts\":null}}]}\n\n" +
                      "data: [DONE]\n\n";

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse)
            };

            return Task.FromResult(response);
        }
    }

    private sealed class HtmlResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body>gateway</body></html>")
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("连接被拒绝", new InvalidOperationException("No such host is known"));
        }
    }
}
