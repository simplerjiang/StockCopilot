namespace SimplerJiangAiAgent.Api.Infrastructure.Llm;

public interface ILlmSettingsStore
{
    Task<IReadOnlyCollection<LlmProviderSettings>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<string> GetActiveProviderKeyAsync(CancellationToken cancellationToken = default);
    Task<string> SetActiveProviderKeyAsync(string provider, CancellationToken cancellationToken = default);
    Task<string> ResolveProviderKeyAsync(string? provider, CancellationToken cancellationToken = default);
    Task<LlmProviderSettings?> GetProviderAsync(string provider, CancellationToken cancellationToken = default);
    Task<LlmProviderSettings> UpsertAsync(LlmProviderSettings settings, CancellationToken cancellationToken = default);
}
