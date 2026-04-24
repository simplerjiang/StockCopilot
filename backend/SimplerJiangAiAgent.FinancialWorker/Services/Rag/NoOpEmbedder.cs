namespace SimplerJiangAiAgent.FinancialWorker.Services.Rag;

public class NoOpEmbedder : IEmbedder
{
    public bool IsAvailable => false;

    public Task<float[]?> EmbedAsync(string text, CancellationToken ct = default)
    {
        return Task.FromResult<float[]?>(null);
    }
}
