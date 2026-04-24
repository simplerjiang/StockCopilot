namespace SimplerJiangAiAgent.FinancialWorker.Services.Rag;

public interface IEmbedder
{
    /// <summary>
    /// Generate embedding vector for text. Returns null if embedder is not configured.
    /// </summary>
    Task<float[]?> EmbedAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Whether this embedder is actually functional (not a stub).
    /// </summary>
    bool IsAvailable { get; }
}
