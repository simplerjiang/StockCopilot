using System.Diagnostics;
using SimplerJiangAiAgent.Api.Infrastructure.Logging;

namespace SimplerJiangAiAgent.Api.Infrastructure.Llm;

public interface ILlmService
{
    Task<LlmChatResult> ChatAsync(string provider, LlmChatRequest request, CancellationToken cancellationToken = default);
}

public sealed class LlmService : ILlmService
{
    private const int MaxLoggedContentLength = 4000;
    private readonly ILlmSettingsStore _settingsStore;
    private readonly IReadOnlyCollection<ILlmProvider> _providers;
    private readonly IFileLogWriter _fileLogWriter;

    public LlmService(ILlmSettingsStore settingsStore, IEnumerable<ILlmProvider> providers, IFileLogWriter fileLogWriter)
    {
        _settingsStore = settingsStore;
        _providers = providers.ToArray();
        _fileLogWriter = fileLogWriter;
    }

    public async Task<LlmChatResult> ChatAsync(string provider, LlmChatRequest request, CancellationToken cancellationToken = default)
    {
        var traceId = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();

        var resolvedProvider = await _settingsStore.ResolveProviderKeyAsync(provider, cancellationToken);
        var settings = await _settingsStore.GetProviderAsync(resolvedProvider, cancellationToken)
            ?? new LlmProviderSettings { Provider = resolvedProvider, ProviderType = "openai", Enabled = true };

        if (!settings.Enabled)
        {
            throw new InvalidOperationException($"Provider {resolvedProvider} 未启用");
        }

        var providerType = string.IsNullOrWhiteSpace(settings.ProviderType) ? settings.Provider : settings.ProviderType;
        var target = _providers.FirstOrDefault(item => string.Equals(item.Name, providerType, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            throw new InvalidOperationException($"未找到 provider: {providerType}");
        }

        var finalRequest = request;
        if (settings.ForceChinese)
        {
            var hint = "请使用中文回答。";
            if (!request.Prompt.Contains(hint, StringComparison.OrdinalIgnoreCase))
            {
                finalRequest = request with { Prompt = $"{request.Prompt}\n\n{hint}" };
            }
        }

        finalRequest = finalRequest with { TraceId = traceId };

        WriteAudit(
            $"traceId={traceId} stage=request provider={resolvedProvider} providerType={providerType} model={finalRequest.Model ?? settings.Model ?? string.Empty} " +
            $"temp={(finalRequest.Temperature?.ToString("0.###") ?? "default")} useInternet={finalRequest.UseInternet} prompt={EscapeForLog(finalRequest.Prompt)}");

        try
        {
            var result = await target.ChatAsync(settings, finalRequest, cancellationToken);
            stopwatch.Stop();

            WriteAudit(
                $"traceId={traceId} stage=response provider={resolvedProvider} providerType={providerType} elapsedMs={stopwatch.ElapsedMilliseconds} " +
                $"content={EscapeForLog(result.Content)}");

            return result with { TraceId = traceId };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            WriteAudit(
                $"traceId={traceId} stage=error provider={resolvedProvider} providerType={providerType} elapsedMs={stopwatch.ElapsedMilliseconds} " +
                $"type={ex.GetType().Name} message={EscapeForLog(ex.Message)}");
            throw;
        }
    }

    private void WriteAudit(string message)
    {
        _fileLogWriter.Write("LLM-AUDIT", message);
    }

    private static string EscapeForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        if (normalized.Length <= MaxLoggedContentLength)
        {
            return normalized;
        }

        return normalized[..MaxLoggedContentLength] + "...(truncated)";
    }
}
