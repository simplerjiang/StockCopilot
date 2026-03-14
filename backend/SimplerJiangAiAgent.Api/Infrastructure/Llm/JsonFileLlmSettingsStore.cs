using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace SimplerJiangAiAgent.Api.Infrastructure.Llm;

public sealed class JsonFileLlmSettingsStore : ILlmSettingsStore
{
    private const string DefaultProviderKey = "default";
    private const string ActiveProviderAlias = "active";
    private const string LegacyOpenAiProviderKey = "openai";
    private readonly string _defaultsFilePath;
    private readonly string _localSecretsFilePath;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public JsonFileLlmSettingsStore(IWebHostEnvironment environment)
    {
        var baseDir = Path.Combine(environment.ContentRootPath, "App_Data");
        _defaultsFilePath = Path.Combine(baseDir, "llm-settings.json");
        _localSecretsFilePath = Path.Combine(baseDir, "llm-settings.local.json");
    }

    public async Task<IReadOnlyCollection<LlmProviderSettings>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var document = await LoadMergedAsync(cancellationToken);
        return document.Providers.Values.ToArray();
    }

    public async Task<string> GetActiveProviderKeyAsync(CancellationToken cancellationToken = default)
    {
        var document = await LoadMergedAsync(cancellationToken);
        return ResolveProviderKey(document.ActiveProviderKey, document);
    }

    public async Task<string> SetActiveProviderKeyAsync(string provider, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("Provider 不能为空", nameof(provider));
        }

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var defaultsDocument = NormalizeDocument(await LoadDocumentAsync(_defaultsFilePath, cancellationToken, requireLock: false));
            var localSecretsDocument = NormalizeDocument(await LoadDocumentAsync(_localSecretsFilePath, cancellationToken, requireLock: false));
            var merged = MergeDocuments(defaultsDocument, localSecretsDocument);
            var resolved = ResolveProviderKey(provider, merged);
            if (!merged.Providers.ContainsKey(resolved))
            {
                throw new InvalidOperationException($"未找到 provider: {provider}");
            }

            defaultsDocument.ActiveProviderKey = resolved;
            await SaveDocumentAsync(_defaultsFilePath, defaultsDocument, cancellationToken);
            return resolved;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<string> ResolveProviderKeyAsync(string? provider, CancellationToken cancellationToken = default)
    {
        var document = await LoadMergedAsync(cancellationToken);
        return ResolveProviderKey(provider, document);
    }

    public async Task<LlmProviderSettings?> GetProviderAsync(string provider, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return null;
        }

        var document = await LoadMergedAsync(cancellationToken);
        var resolvedProvider = ResolveProviderKey(provider, document);
        document.Providers.TryGetValue(resolvedProvider, out var settings);
        return settings;
    }

    public async Task<LlmProviderSettings> UpsertAsync(LlmProviderSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.Provider))
        {
            throw new ArgumentException("Provider 不能为空", nameof(settings.Provider));
        }

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var defaultsDocument = NormalizeDocument(await LoadDocumentAsync(_defaultsFilePath, cancellationToken, requireLock: false));
            var localSecretsDocument = NormalizeDocument(await LoadDocumentAsync(_localSecretsFilePath, cancellationToken, requireLock: false));
            var providerKey = NormalizeProviderKey(settings.Provider);

            if (!defaultsDocument.Providers.TryGetValue(providerKey, out var existingDefaults))
            {
                existingDefaults = new LlmProviderSettings { Provider = providerKey, ProviderType = "openai" };
            }

            if (!localSecretsDocument.Providers.TryGetValue(providerKey, out var existingSecrets))
            {
                existingSecrets = new LlmProviderSettings { Provider = providerKey, ProviderType = existingDefaults.ProviderType };
            }

            if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                existingSecrets.ApiKey = settings.ApiKey.Trim();
            }

            existingDefaults.Provider = providerKey;
            existingDefaults.ProviderType = string.IsNullOrWhiteSpace(settings.ProviderType)
                ? NormalizeProviderType(existingDefaults.ProviderType)
                : NormalizeProviderType(settings.ProviderType);
            existingSecrets.Provider = providerKey;
            existingSecrets.ProviderType = existingDefaults.ProviderType;

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                existingDefaults.BaseUrl = settings.BaseUrl.Trim();
            }

            if (!string.IsNullOrWhiteSpace(settings.Model))
            {
                existingDefaults.Model = settings.Model.Trim();
            }

            if (!string.IsNullOrWhiteSpace(settings.SystemPrompt))
            {
                existingDefaults.SystemPrompt = settings.SystemPrompt.Trim();
            }

            existingDefaults.ForceChinese = settings.ForceChinese;

            if (!string.IsNullOrWhiteSpace(settings.Organization))
            {
                existingDefaults.Organization = settings.Organization.Trim();
            }

            if (!string.IsNullOrWhiteSpace(settings.Project))
            {
                existingDefaults.Project = settings.Project.Trim();
            }

            existingDefaults.Enabled = settings.Enabled;
            existingDefaults.UpdatedAt = DateTimeOffset.UtcNow;
            existingDefaults.ApiKey = string.Empty;

            defaultsDocument.Providers[providerKey] = existingDefaults;
            if (!string.IsNullOrWhiteSpace(existingSecrets.ApiKey))
            {
                localSecretsDocument.Providers[providerKey] = new LlmProviderSettings
                {
                    Provider = providerKey,
                    ProviderType = existingDefaults.ProviderType,
                    ApiKey = existingSecrets.ApiKey,
                    UpdatedAt = existingDefaults.UpdatedAt
                };
            }
            else
            {
                localSecretsDocument.Providers.Remove(providerKey);
            }

            defaultsDocument.ActiveProviderKey = ResolveProviderKey(defaultsDocument.ActiveProviderKey, MergeDocuments(defaultsDocument, localSecretsDocument));

            await SaveDocumentAsync(_defaultsFilePath, defaultsDocument, cancellationToken);
            await SaveDocumentAsync(_localSecretsFilePath, localSecretsDocument, cancellationToken, deleteWhenEmpty: true);

            return MergeSettings(existingDefaults, existingSecrets.ApiKey);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<LlmSettingsDocument> LoadMergedAsync(CancellationToken cancellationToken, bool requireLock = true)
    {
        if (requireLock)
        {
            await _mutex.WaitAsync(cancellationToken);
        }

        try
        {
            var defaultsDocument = await LoadDocumentAsync(_defaultsFilePath, cancellationToken, requireLock: false);
            var localSecretsDocument = await LoadDocumentAsync(_localSecretsFilePath, cancellationToken, requireLock: false);
            return MergeDocuments(defaultsDocument, localSecretsDocument);
        }
        finally
        {
            if (requireLock)
            {
                _mutex.Release();
            }
        }
    }

    private async Task<LlmSettingsDocument> LoadDocumentAsync(string filePath, CancellationToken cancellationToken, bool requireLock = true)
    {
        if (requireLock)
        {
            await _mutex.WaitAsync(cancellationToken);
        }

        try
        {
            if (!File.Exists(filePath))
            {
                return new LlmSettingsDocument();
            }

            await using var stream = File.OpenRead(filePath);
            var document = await JsonSerializer.DeserializeAsync<LlmSettingsDocument>(stream, _serializerOptions, cancellationToken);
            return document ?? new LlmSettingsDocument();
        }
        finally
        {
            if (requireLock)
            {
                _mutex.Release();
            }
        }
    }

    private async Task SaveDocumentAsync(string filePath, LlmSettingsDocument document, CancellationToken cancellationToken, bool deleteWhenEmpty = false)
    {
        if (deleteWhenEmpty && document.Providers.Count == 0)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            return;
        }

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, document, _serializerOptions, cancellationToken);
    }

    private static LlmSettingsDocument MergeDocuments(LlmSettingsDocument defaultsDocument, LlmSettingsDocument localSecretsDocument)
    {
        defaultsDocument = NormalizeDocument(defaultsDocument);
        localSecretsDocument = NormalizeDocument(localSecretsDocument);
        var merged = new LlmSettingsDocument
        {
            ActiveProviderKey = defaultsDocument.ActiveProviderKey
        };

        foreach (var (provider, settings) in defaultsDocument.Providers)
        {
            merged.Providers[provider] = CloneWithoutSecret(settings);
        }

        foreach (var (provider, settings) in localSecretsDocument.Providers)
        {
            if (!merged.Providers.TryGetValue(provider, out var mergedSettings))
            {
                mergedSettings = new LlmProviderSettings { Provider = provider, ProviderType = settings.ProviderType };
                merged.Providers[provider] = mergedSettings;
            }

            if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                mergedSettings.ApiKey = settings.ApiKey.Trim();
            }
        }

        foreach (var (provider, settings) in merged.Providers)
        {
            var envApiKey = ResolveApiKeyFromEnvironment(provider);
            if (!string.IsNullOrWhiteSpace(envApiKey))
            {
                settings.ApiKey = envApiKey;
            }
        }

        merged.ActiveProviderKey = ResolveProviderKey(merged.ActiveProviderKey, merged);

        return merged;
    }

    private static LlmProviderSettings MergeSettings(LlmProviderSettings defaultsSettings, string apiKey)
    {
        var merged = CloneWithoutSecret(defaultsSettings);
        merged.ApiKey = string.IsNullOrWhiteSpace(apiKey)
            ? ResolveApiKeyFromEnvironment(defaultsSettings.Provider)
            : apiKey;
        return merged;
    }

    private static LlmProviderSettings CloneWithoutSecret(LlmProviderSettings source)
    {
        return new LlmProviderSettings
        {
            Provider = source.Provider,
            ProviderType = NormalizeProviderType(source.ProviderType),
            ApiKey = string.Empty,
            BaseUrl = source.BaseUrl,
            Model = source.Model,
            SystemPrompt = source.SystemPrompt,
            ForceChinese = source.ForceChinese,
            Organization = source.Organization,
            Project = source.Project,
            Enabled = source.Enabled,
            UpdatedAt = source.UpdatedAt
        };
    }

    private static string ResolveApiKeyFromEnvironment(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return string.Empty;
        }

        var normalizedProvider = provider.Trim().ToUpperInvariant();
        var providerScopedName = $"LLM__{normalizedProvider}__APIKEY";
        var providerScopedValue = Environment.GetEnvironmentVariable(providerScopedName);
        if (!string.IsNullOrWhiteSpace(providerScopedValue))
        {
            return providerScopedValue.Trim();
        }

        if (string.Equals(provider, DefaultProviderKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, LegacyOpenAiProviderKey, StringComparison.OrdinalIgnoreCase))
        {
            var openAiValue = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (!string.IsNullOrWhiteSpace(openAiValue))
            {
                return openAiValue.Trim();
            }
        }

        if (string.Equals(provider, "gemini_official", StringComparison.OrdinalIgnoreCase))
        {
            var geminiValue = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (!string.IsNullOrWhiteSpace(geminiValue))
            {
                return geminiValue.Trim();
            }

            var googleValue = Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
            if (!string.IsNullOrWhiteSpace(googleValue))
            {
                return googleValue.Trim();
            }
        }

        return string.Empty;
    }

    private static LlmSettingsDocument NormalizeDocument(LlmSettingsDocument? document)
    {
        document ??= new LlmSettingsDocument();
        var normalized = new LlmSettingsDocument
        {
            ActiveProviderKey = string.IsNullOrWhiteSpace(document.ActiveProviderKey)
                ? DefaultProviderKey
                : document.ActiveProviderKey.Trim()
        };

        foreach (var (provider, settings) in document.Providers)
        {
            var key = NormalizeProviderKey(provider);
            var clone = CloneWithSecret(settings);
            clone.Provider = key;
            clone.ProviderType = NormalizeProviderType(clone.ProviderType);
            normalized.Providers[key] = clone;
        }

        if (!normalized.Providers.ContainsKey(DefaultProviderKey) && normalized.Providers.TryGetValue(LegacyOpenAiProviderKey, out var legacy))
        {
            var migrated = CloneWithSecret(legacy);
            migrated.Provider = DefaultProviderKey;
            migrated.ProviderType = NormalizeProviderType(migrated.ProviderType);
            normalized.Providers[DefaultProviderKey] = migrated;
            normalized.Providers.Remove(LegacyOpenAiProviderKey);
        }

        normalized.ActiveProviderKey = ResolveProviderKey(normalized.ActiveProviderKey, normalized);
        return normalized;
    }

    private static LlmProviderSettings CloneWithSecret(LlmProviderSettings source)
    {
        return new LlmProviderSettings
        {
            Provider = source.Provider,
            ProviderType = NormalizeProviderType(source.ProviderType),
            ApiKey = source.ApiKey,
            BaseUrl = source.BaseUrl,
            Model = source.Model,
            SystemPrompt = source.SystemPrompt,
            ForceChinese = source.ForceChinese,
            Organization = source.Organization,
            Project = source.Project,
            Enabled = source.Enabled,
            UpdatedAt = source.UpdatedAt
        };
    }

    private static string NormalizeProviderKey(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return DefaultProviderKey;
        }

        var trimmed = provider.Trim();
        if (string.Equals(trimmed, LegacyOpenAiProviderKey, StringComparison.OrdinalIgnoreCase))
        {
            return DefaultProviderKey;
        }

        return trimmed;
    }

    private static string NormalizeProviderType(string? providerType)
    {
        return string.IsNullOrWhiteSpace(providerType)
            ? "openai"
            : providerType.Trim().ToLowerInvariant();
    }

    private static string ResolveProviderKey(string? provider, LlmSettingsDocument document)
    {
        var requested = string.IsNullOrWhiteSpace(provider)
            ? ActiveProviderAlias
            : provider.Trim();

        if (string.Equals(requested, ActiveProviderAlias, StringComparison.OrdinalIgnoreCase))
        {
            requested = string.IsNullOrWhiteSpace(document.ActiveProviderKey)
                ? DefaultProviderKey
                : document.ActiveProviderKey.Trim();
        }

        requested = NormalizeProviderKey(requested);
        if (document.Providers.ContainsKey(requested))
        {
            return requested;
        }

        if (document.Providers.Count == 0)
        {
            return requested;
        }

        if (document.Providers.ContainsKey(DefaultProviderKey))
        {
            return DefaultProviderKey;
        }

        return document.Providers.Keys.First();
    }
}
