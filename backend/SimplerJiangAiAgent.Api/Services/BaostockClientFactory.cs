using System.Threading.Channels;
using Baostock.NET.Client;

namespace SimplerJiangAiAgent.Api.Services;

public sealed class BaostockClientFactory : IBaostockClientFactory, IAsyncDisposable
{
    private readonly Channel<BaostockClient> _pool;
    private readonly SemaphoreSlim _createSemaphore;
    private readonly int _maxSize;
    private int _currentCount;
    private readonly ILogger<BaostockClientFactory> _logger;

    public BaostockClientFactory(ILogger<BaostockClientFactory> logger, int maxSize = 3)
    {
        _maxSize = maxSize;
        _pool = Channel.CreateBounded<BaostockClient>(maxSize);
        _createSemaphore = new SemaphoreSlim(1, 1);
        _logger = logger;
    }

    public async Task<BaostockClientLease> GetClientAsync(CancellationToken ct = default)
    {
        // Try to get an existing client from pool
        if (_pool.Reader.TryRead(out var client))
        {
            return new BaostockClientLease(client, ReturnToPoolAsync);
        }

        // Create new if under limit
        await _createSemaphore.WaitAsync(ct);
        try
        {
            // Double-check pool after acquiring semaphore
            if (_pool.Reader.TryRead(out client))
            {
                return new BaostockClientLease(client, ReturnToPoolAsync);
            }

            if (_currentCount < _maxSize)
            {
                client = await BaostockClient.CreateAndLoginAsync(ct: ct);
                _currentCount++;
                _logger.LogInformation("Created new BaostockClient (pool size: {Count}/{Max})", _currentCount, _maxSize);
                return new BaostockClientLease(client, ReturnToPoolAsync);
            }
        }
        finally
        {
            _createSemaphore.Release();
        }

        // Pool exhausted, wait for one to be returned
        client = await _pool.Reader.ReadAsync(ct);
        return new BaostockClientLease(client, ReturnToPoolAsync);
    }

    private async ValueTask ReturnToPoolAsync(BaostockClient client)
    {
        if (!_pool.Writer.TryWrite(client))
        {
            // Pool full (shouldn't happen with bounded channel of same size)
            await client.DisposeAsync();
            Interlocked.Decrement(ref _currentCount);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _pool.Writer.Complete();
        await foreach (var client in _pool.Reader.ReadAllAsync())
        {
            await client.DisposeAsync();
        }
        _createSemaphore.Dispose();
    }
}
