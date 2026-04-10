namespace SimplerJiangAiAgent.Api.Infrastructure.Llm;

public static class OllamaRuntimeDefaults
{
    public const int NumCtx = 131072;
    public const int NumGpu = 99;
    public const string KeepAlive = "5m";
    public const int NumPredict = 2048;
    public const double Temperature = 0.3;
    public const int TopK = 64;
    public const double TopP = 0.95;
    public const double MinP = 0.0;
    public const bool Think = false;

    public static bool IsOllamaProvider(LlmProviderSettings? settings)
    {
        return settings is not null && IsOllamaProvider(settings.Provider, settings.ProviderType);
    }

    public static bool IsOllamaProvider(string? providerKey, string? providerType)
    {
        return string.Equals(providerType, "ollama", StringComparison.OrdinalIgnoreCase)
            || string.Equals(providerKey, "ollama", StringComparison.OrdinalIgnoreCase);
    }

    public static int ResolveNumCtx(int? value)
    {
        return value is > 0 ? value.Value : NumCtx;
    }

    public static int ResolveNumGpu(int? value)
    {
        return value.HasValue ? value.Value : NumGpu;
    }

    public static string ResolveKeepAlive(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return KeepAlive;
        }

        var normalized = value.Trim();
        if (string.Equals(normalized, "-1", StringComparison.Ordinal))
        {
            return KeepAlive;
        }

        if (string.Equals(normalized, "0", StringComparison.Ordinal))
        {
            return normalized;
        }

        if (int.TryParse(normalized, out var numericValue))
        {
            return numericValue > 0 ? $"{numericValue}m" : KeepAlive;
        }

        return normalized;
    }

    public static int ResolveNumPredict(int? value)
    {
        return value is >= -1 ? value.Value : NumPredict;
    }

    public static double ResolveTemperature(double? settingsValue, double? requestValue = null)
    {
        if (requestValue.HasValue && requestValue.Value >= 0)
        {
            return requestValue.Value;
        }

        if (settingsValue.HasValue && settingsValue.Value >= 0)
        {
            return settingsValue.Value;
        }

        return Temperature;
    }

    public static int ResolveTopK(int? value)
    {
        return value is > 0 ? value.Value : TopK;
    }

    public static double ResolveTopP(double? value)
    {
        return value.HasValue && value.Value is > 0 and <= 1
            ? value.Value
            : TopP;
    }

    public static double ResolveMinP(double? value)
    {
        return value.HasValue && value.Value is >= 0 and <= 1
            ? value.Value
            : MinP;
    }

    public static string[] ResolveStop(string[]? values)
    {
        if (values is null || values.Length == 0)
        {
            return Array.Empty<string>();
        }

        return values
            .Select(value => value?.Trim() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static bool ResolveThink(bool? value)
    {
        return value ?? Think;
    }
}