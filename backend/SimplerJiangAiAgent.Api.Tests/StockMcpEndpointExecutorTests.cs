using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StockMcpEndpointExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsOk_WhenActionCompletes()
    {
        var result = await StockMcpEndpointExecutor.ExecuteAsync(
            _ => Task.FromResult(new { toolName = "StockKlineMcp" }),
            CancellationToken.None);

        var (statusCode, body) = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.Contains("StockKlineMcp", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_Returns499_WhenRequestIsCanceled()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        var result = await StockMcpEndpointExecutor.ExecuteAsync<object>(
            _ => throw new OperationCanceledException(cancellationTokenSource.Token),
            cancellationTokenSource.Token);

        var (statusCode, body) = await ExecuteResultAsync(result);

        Assert.Equal(StockMcpEndpointExecutor.ClientClosedRequestStatusCode, statusCode);
        Assert.Contains("请求已取消", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_RethrowsOperationCanceled_WhenRequestTokenWasNotCanceled()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            StockMcpEndpointExecutor.ExecuteAsync<object>(
                _ => throw new OperationCanceledException(),
                CancellationToken.None));
    }

    private static async Task<(int StatusCode, string Body)> ExecuteResultAsync(IResult result)
    {
        var httpContext = new DefaultHttpContext();
        var bodyStream = new MemoryStream();
        httpContext.Response.Body = bodyStream;
        httpContext.RequestServices = new ServiceCollection()
            .AddLogging()
            .AddOptions()
            .BuildServiceProvider();

        await result.ExecuteAsync(httpContext);

        bodyStream.Position = 0;
        using var reader = new StreamReader(bodyStream, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        return (httpContext.Response.StatusCode, body);
    }
}