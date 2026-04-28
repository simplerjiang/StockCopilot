using Baostock.NET.Client;

namespace SimplerJiangAiAgent.Api.Services;

public sealed class BaostockClientLease : IAsyncDisposable
{
    public BaostockClient Client { get; }
    private readonly Func<BaostockClient, ValueTask> _returnToPool;
    private bool _disposed;

    internal BaostockClientLease(BaostockClient client, Func<BaostockClient, ValueTask> returnToPool)
    {
        Client = client;
        _returnToPool = returnToPool;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _returnToPool(Client);
    }
}
