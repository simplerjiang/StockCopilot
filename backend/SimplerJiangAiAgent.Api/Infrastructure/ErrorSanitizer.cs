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

    private const string Replacement = "[LLM-GATEWAY]";

    /// <summary>
    /// Returns the message with all HTTP(S) URLs replaced by a safe placeholder.
    /// Returns null when the input is null.
    /// </summary>
    public static string? SanitizeErrorMessage(string? message)
    {
        if (string.IsNullOrEmpty(message)) return message;
        return UrlPattern().Replace(message, Replacement);
    }
}
