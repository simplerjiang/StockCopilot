namespace SimplerJiangAiAgent.Api.Services;

/// <summary>
/// Factory for obtaining pooled BaostockClient instances.
/// </summary>
public interface IBaostockClientFactory
{
    /// <summary>
    /// Borrows a connected BaostockClient from the pool.
    /// Caller must dispose the returned handle to return the client.
    /// </summary>
    Task<BaostockClientLease> GetClientAsync(CancellationToken ct = default);
}
