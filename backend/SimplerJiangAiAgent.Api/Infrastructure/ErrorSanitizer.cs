using System.Text.RegularExpressions;

namespace SimplerJiangAiAgent.Api.Infrastructure;

/// <summary>
/// Strips sensitive URLs (LLM gateway addresses, etc.) from error messages
/// before they reach the frontend or are persisted to the database.
/// Internal logs (ILogger) should keep the original message for debugging.
/// </summary>
internal static partial class ErrorSanitizer
{
    // Matches http(s) URLs — covers gateway endpoints like https://api.bltcy.ai/v1/chat/completions
    [GeneratedRegex(@"https?://[^\s,，;；\]）)""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"\b(Bearer\s+)[A-Za-z0-9._~+/=-]{8,}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BearerTokenPattern();

    [GeneratedRegex(@"\b(api[_-]?key|access[_-]?token|refresh[_-]?token|token|authorization)(\s*[:=]\s*)([""']?)(?:Bearer\s+)?[^,\r\n;}\]""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SecretAssignmentPattern();

    [GeneratedRegex(@"\bsk-[A-Za-z0-9][A-Za-z0-9._-]{6,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BareOpenAiKeyPattern();

    [GeneratedRegex(@"\b[A-Za-z]:\\[^\r\n,;""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex WindowsPathPattern();

    [GeneratedRegex(@"(?<!:)\/(?:Users|home|var|tmp|etc|opt)\/[^\r\n,;""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UnixPathPattern();

    private const string Replacement = "[LLM-GATEWAY]";
    private const string SecretReplacement = "[SECRET]";
    private const string PathReplacement = "[LOCAL-PATH]";

    /// <summary>
    /// Returns the message with all HTTP(S) URLs replaced by a safe placeholder.
    /// Returns null when the input is null.
    /// </summary>
    public static string? SanitizeErrorMessage(string? message)
    {
        if (string.IsNullOrEmpty(message)) return message;
        var sanitized = UrlPattern().Replace(message, Replacement);
        sanitized = BearerTokenPattern().Replace(sanitized, $"$1{SecretReplacement}");
        sanitized = SecretAssignmentPattern().Replace(sanitized, $"$1$2$3{SecretReplacement}");
        sanitized = BareOpenAiKeyPattern().Replace(sanitized, SecretReplacement);
        sanitized = WindowsPathPattern().Replace(sanitized, PathReplacement);
        sanitized = UnixPathPattern().Replace(sanitized, PathReplacement);
        return sanitized;
    }
}
