using System.Collections.Concurrent;
using System.Text;
using SimplerJiangAiAgent.Api.Infrastructure.Storage;

namespace SimplerJiangAiAgent.Api.Infrastructure.Logging;

public interface ISessionFileLogger
{
    void LogTurnStart(string pipelineType, long sessionId, long turnId, int turnIndex,
        string symbol, string? stockName, string userPrompt);

    void LogRoleToolCall(long sessionId, long turnId, string roleId,
        string toolName, string toolArgs, string toolResult);

    void LogRoleLlmRequest(long sessionId, long turnId, string roleId,
        string systemPrompt, string userContent);

    void LogRoleLlmResponse(long sessionId, long turnId, string roleId,
        string responseContent, string? traceId, long elapsedMs);

    void LogRoleLlmError(long sessionId, long turnId, string roleId,
        string errorType, string errorMessage);

    void LogTurnEnd(long sessionId, long turnId, string status);
}

public sealed class SessionFileLogger : ISessionFileLogger
{
    private readonly string _sessionsDir;
    private readonly ConcurrentDictionary<(long SessionId, long TurnId), string> _activeTurnFiles = new();
    private readonly ConcurrentDictionary<string, object> _fileLocks = new();

    public SessionFileLogger(AppRuntimePaths paths)
    {
        _sessionsDir = Path.Combine(paths.LogsPath, "sessions");
    }

    public void LogTurnStart(string pipelineType, long sessionId, long turnId, int turnIndex,
        string symbol, string? stockName, string userPrompt)
    {
        try
        {
            Directory.CreateDirectory(_sessionsDir);
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var fileName = $"{pipelineType}-S{sessionId}-T{turnIndex}_{timestamp}.md";
            var filePath = Path.Combine(_sessionsDir, fileName);
            _activeTurnFiles[(sessionId, turnId)] = filePath;

            var sb = new StringBuilder();
            sb.AppendLine($"# {Capitalize(pipelineType)} Session S{sessionId} Turn {turnIndex}");
            sb.AppendLine($"- **Pipeline**: {pipelineType}");
            sb.AppendLine($"- **Session ID**: {sessionId}");
            sb.AppendLine($"- **Turn ID**: {turnId}");
            sb.AppendLine($"- **Symbol**: {symbol}");
            if (!string.IsNullOrWhiteSpace(stockName))
                sb.AppendLine($"- **Stock Name**: {stockName}");
            sb.AppendLine($"- **User Prompt**: {userPrompt}");
            sb.AppendLine($"- **Started**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            WriteToFile(filePath, sb.ToString());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SessionFileLogger] LogTurnStart failed: {ex.Message}");
        }
    }

    public void LogRoleToolCall(long sessionId, long turnId, string roleId,
        string toolName, string toolArgs, string toolResult)
    {
        try
        {
            var filePath = GetFilePath(sessionId, turnId);
            if (filePath is null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"### Tool Call: {toolName}");
            sb.AppendLine($"**Args**:");
            sb.AppendLine("```json");
            sb.AppendLine(toolArgs);
            sb.AppendLine("```");
            sb.AppendLine($"**Result**:");
            sb.AppendLine("```json");
            sb.AppendLine(toolResult);
            sb.AppendLine("```");
            sb.AppendLine();

            AppendToFile(filePath, sb.ToString());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SessionFileLogger] LogRoleToolCall failed: {ex.Message}");
        }
    }

    public void LogRoleLlmRequest(long sessionId, long turnId, string roleId,
        string systemPrompt, string userContent)
    {
        try
        {
            var filePath = GetFilePath(sessionId, turnId);
            if (filePath is null) return;

            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"## Role: {roleId}");
            sb.AppendLine();
            sb.AppendLine("### LLM Request");
            sb.AppendLine("**System Prompt**:");
            sb.AppendLine("```");
            sb.AppendLine(systemPrompt);
            sb.AppendLine("```");
            sb.AppendLine("**User Content**:");
            sb.AppendLine("```");
            sb.AppendLine(userContent);
            sb.AppendLine("```");
            sb.AppendLine();

            AppendToFile(filePath, sb.ToString());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SessionFileLogger] LogRoleLlmRequest failed: {ex.Message}");
        }
    }

    public void LogRoleLlmResponse(long sessionId, long turnId, string roleId,
        string responseContent, string? traceId, long elapsedMs)
    {
        try
        {
            var filePath = GetFilePath(sessionId, turnId);
            if (filePath is null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"### LLM Response [traceId={traceId ?? "n/a"}, elapsed={elapsedMs}ms]");
            sb.AppendLine("```");
            sb.AppendLine(responseContent);
            sb.AppendLine("```");
            sb.AppendLine();

            AppendToFile(filePath, sb.ToString());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SessionFileLogger] LogRoleLlmResponse failed: {ex.Message}");
        }
    }

    public void LogRoleLlmError(long sessionId, long turnId, string roleId,
        string errorType, string errorMessage)
    {
        try
        {
            var filePath = GetFilePath(sessionId, turnId);
            if (filePath is null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"### LLM Error [{errorType}]");
            sb.AppendLine("```");
            sb.AppendLine(errorMessage);
            sb.AppendLine("```");
            sb.AppendLine();

            AppendToFile(filePath, sb.ToString());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SessionFileLogger] LogRoleLlmError failed: {ex.Message}");
        }
    }

    public void LogTurnEnd(long sessionId, long turnId, string status)
    {
        try
        {
            var filePath = GetFilePath(sessionId, turnId);
            if (filePath is null) return;

            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"# Turn Completed: {status}");
            sb.AppendLine($"- **Ended**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            AppendToFile(filePath, sb.ToString());
            _activeTurnFiles.TryRemove((sessionId, turnId), out _);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SessionFileLogger] LogTurnEnd failed: {ex.Message}");
        }
    }

    private string? GetFilePath(long sessionId, long turnId)
    {
        _activeTurnFiles.TryGetValue((sessionId, turnId), out var path);
        return path;
    }

    private void WriteToFile(string filePath, string content)
    {
        var lockObj = _fileLocks.GetOrAdd(filePath, _ => new object());
        lock (lockObj)
        {
            File.WriteAllText(filePath, content, Encoding.UTF8);
        }
    }

    private void AppendToFile(string filePath, string content)
    {
        var lockObj = _fileLocks.GetOrAdd(filePath, _ => new object());
        lock (lockObj)
        {
            File.AppendAllText(filePath, content, Encoding.UTF8);
        }
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
