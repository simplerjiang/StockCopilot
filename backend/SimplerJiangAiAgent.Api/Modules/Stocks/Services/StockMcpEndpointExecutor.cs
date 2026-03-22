namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

internal static class StockMcpEndpointExecutor
{
    internal const int ClientClosedRequestStatusCode = 499;

    public static async Task<IResult> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            var payload = await action(cancellationToken);
            return Results.Ok(payload);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Results.Json(new { message = "请求已取消" }, statusCode: ClientClosedRequestStatusCode);
        }
    }
}