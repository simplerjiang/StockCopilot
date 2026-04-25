using SimplerJiangAiAgent.Api.Infrastructure;
using Xunit;

namespace SimplerJiangAiAgent.Api.Tests;

public class ErrorSanitizerTests
{
    [Fact]
    public void Null_ReturnsNull()
    {
        Assert.Null(ErrorSanitizer.SanitizeErrorMessage(null));
    }

    [Fact]
    public void Empty_ReturnsEmpty()
    {
        Assert.Equal("", ErrorSanitizer.SanitizeErrorMessage(""));
    }

    [Fact]
    public void NoUrl_ReturnsSameMessage()
    {
        const string msg = "角色 analyst 连续 2 次返回非 JSON / 非法响应";
        Assert.Equal(msg, ErrorSanitizer.SanitizeErrorMessage(msg));
    }

    [Fact]
    public void HttpsUrl_IsReplaced()
    {
        const string raw = "请求发送失败，请检查 BaseUrl、代理或网络连通性。uri=https://api.bltcy.ai/v1/chat/completions，原因：Connection refused";
        var result = ErrorSanitizer.SanitizeErrorMessage(raw);
        Assert.DoesNotContain("bltcy.ai", result);
        Assert.DoesNotContain("https://", result);
        Assert.Contains("[LLM-GATEWAY]", result);
        Assert.Contains("Connection refused", result);
    }

    [Fact]
    public void HttpUrl_IsReplaced()
    {
        const string raw = "Ollama 请求超时，请检查本地服务状态。uri=http://localhost:11434/api/chat";
        var result = ErrorSanitizer.SanitizeErrorMessage(raw);
        Assert.DoesNotContain("http://localhost", result);
        Assert.Contains("[LLM-GATEWAY]", result);
    }

    [Fact]
    public void MultipleUrls_AllReplaced()
    {
        const string raw = "Tried https://gw1.example.com/v1 and https://gw2.example.com/v1, both failed";
        var result = ErrorSanitizer.SanitizeErrorMessage(raw);
        Assert.DoesNotContain("example.com", result);
        Assert.Equal(2, result.Split("[LLM-GATEWAY]").Length - 1);
    }
}
