namespace SimplerJiangAiAgent.Api.Modules.Llm.Models;

public sealed record LlmSettingsRequest(
    string? ApiKey,
    string? BaseUrl,
    string? Model,
    string? SystemPrompt,
    bool ForceChinese,
    string? Organization,
    string? Project,
    bool Enabled);

public sealed record LlmSettingsResponse(
    string Provider,
    string ProviderType,
    string BaseUrl,
    string Model,
    string SystemPrompt,
    bool ForceChinese,
    string Organization,
    string Project,
    bool Enabled,
    bool HasApiKey,
    string ApiKeyMasked,
    DateTimeOffset UpdatedAt);

public sealed record ActiveLlmProviderResponse(
    string ActiveProviderKey,
    string[] ProviderKeys);

public sealed record ActiveLlmProviderRequest(
    string ActiveProviderKey);
