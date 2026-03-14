namespace SimplerJiangAiAgent.Api.Infrastructure.Llm;

public sealed class LlmProviderSettings
{
    public string Provider { get; set; } = string.Empty;
    public string ProviderType { get; set; } = "openai";
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public bool ForceChinese { get; set; }
    public string Organization { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LlmSettingsDocument
{
    public string ActiveProviderKey { get; set; } = "default";
    public Dictionary<string, LlmProviderSettings> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
