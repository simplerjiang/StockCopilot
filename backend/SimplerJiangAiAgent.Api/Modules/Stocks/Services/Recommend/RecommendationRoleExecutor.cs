using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using SimplerJiangAiAgent.Api.Infrastructure;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend;

public sealed record RecommendRoleExecutionContext(
    string RoleId,
    string SystemPrompt,
    string UserInput,
    string? UpstreamArtifactsJson,
    long SessionId,
    long TurnId,
    long StageId,
    string? StageType = null);

public sealed record RecommendRoleExecutionResult(
    string RoleId,
    bool Success,
    string? OutputJson,
    string? ErrorCode,
    string? ErrorMessage,
    string? LlmTraceId,
    int ToolCallCount);

public interface IRecommendationRoleExecutor
{
    Task<RecommendRoleExecutionResult> ExecuteAsync(RecommendRoleExecutionContext context, CancellationToken ct = default);
}

public sealed class RecommendationRoleExecutor : IRecommendationRoleExecutor
{
    private const int MaxInvalidFinalResponses = 2;

    private readonly ILlmService _llmService;
    private readonly IRecommendEventBus _eventBus;
    private readonly IRecommendRoleContractRegistry _contractRegistry;
    private readonly IRecommendToolDispatcher _toolDispatcher;
    private readonly ILogger<RecommendationRoleExecutor> _logger;
    private readonly ISessionFileLogger? _sessionLogger;

    public RecommendationRoleExecutor(
        ILlmService llmService,
        IRecommendEventBus eventBus,
        IRecommendRoleContractRegistry contractRegistry,
        IRecommendToolDispatcher toolDispatcher,
        ILogger<RecommendationRoleExecutor> logger)
        : this(llmService, eventBus, contractRegistry, toolDispatcher, logger, null)
    {
    }

    public RecommendationRoleExecutor(
        ILlmService llmService,
        IRecommendEventBus eventBus,
        IRecommendRoleContractRegistry contractRegistry,
        IRecommendToolDispatcher toolDispatcher,
        ILogger<RecommendationRoleExecutor> logger,
        ISessionFileLogger? sessionLogger)
    {
        _llmService = llmService;
        _eventBus = eventBus;
        _contractRegistry = contractRegistry;
        _toolDispatcher = toolDispatcher;
        _logger = logger;
        _sessionLogger = sessionLogger;
    }

    public async Task<RecommendRoleExecutionResult> ExecuteAsync(RecommendRoleExecutionContext context, CancellationToken ct = default)
    {
        // V048-S2 #85: 在 RoleStarted 事件 DetailJson 中暴露工具上限和角色起点，供前端显示 ETA 与 N/M 工具进度
        var startedContract = _contractRegistry.GetContract(context.RoleId);
        _eventBus.Publish(new RecommendEvent(
            RecommendEventType.RoleStarted, context.SessionId, context.TurnId,
            context.StageId, context.StageType, context.RoleId, null,
            $"角色 {context.RoleId} 开始执行",
            JsonSerializer.Serialize(new
            {
                maxToolCalls = startedContract.MaxToolCalls,
                startedAt = DateTime.UtcNow
            }),
            DateTime.UtcNow));

        try
        {
            var contract = startedContract;
            var prompt = BuildPrompt(contract, context);

            _sessionLogger?.LogRoleLlmRequest(context.SessionId, context.TurnId, context.RoleId,
                context.SystemPrompt ?? "", prompt);

            string? lastTraceId = null;
            int toolCallCount = 0;
            int maxCalls = contract.MaxToolCalls;
            int invalidFinalResponseCount = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var request = new LlmChatRequest(prompt, null, 0.3, false, ResponseFormat: LlmResponseFormats.Json);
                var llmSw = Stopwatch.StartNew();
                var llmResult = await CallLlmWithRetryAsync(context, request, ct);
                llmSw.Stop();
                lastTraceId = llmResult.TraceId;
                var content = llmResult.Content?.Trim() ?? "";

                _sessionLogger?.LogRoleLlmResponse(context.SessionId, context.TurnId, context.RoleId,
                    content, lastTraceId, llmSw.ElapsedMilliseconds);

                var hasToolCall = TryParseToolCall(content, out var toolName, out var toolArgs);

                if (toolCallCount < maxCalls && hasToolCall)
                {
                    invalidFinalResponseCount = 0;

                    _eventBus.Publish(new RecommendEvent(
                        RecommendEventType.ToolDispatched, context.SessionId, context.TurnId,
                        context.StageId, context.StageType, context.RoleId, lastTraceId,
                        $"角色 {context.RoleId} 调用工具: {toolName}",
                        JsonSerializer.Serialize(new { toolName, args = toolArgs }),
                        DateTime.UtcNow));

                    var toolResult = await _toolDispatcher.DispatchAsync(toolName, toolArgs, ct);
                    toolCallCount++;

                    _sessionLogger?.LogRoleToolCall(context.SessionId, context.TurnId, context.RoleId,
                        toolName, JsonSerializer.Serialize(toolArgs), toolResult);

                    _eventBus.Publish(new RecommendEvent(
                        RecommendEventType.ToolCompleted, context.SessionId, context.TurnId,
                        context.StageId, context.StageType, context.RoleId, lastTraceId,
                        $"工具 {toolName} 返回完成 (第 {toolCallCount} 次)",
                        JsonSerializer.Serialize(new { toolName, status = "Completed", resultPreview = Truncate(toolResult, 2000) }),
                        DateTime.UtcNow));

                    // P0-4: Detect tool errors and provide explicit failure context to LLM
                    if (IsToolError(toolResult))
                    {
                        prompt += $"\n\n## 工具调用 {toolCallCount}: {toolName}\n### ⚠️ 工具调用失败\n{toolResult}\n\n该工具暂时不可用。请基于已有信息继续分析，或换用其他工具补充数据。不要重复调用刚才失败的工具。";
                    }
                    else
                    {
                        prompt += $"\n\n## 工具调用 {toolCallCount}: {toolName}\n### 工具返回结果\n{toolResult}\n\n请继续分析，或输出最终结果 JSON。";
                    }
                    continue;
                }

                if (TryExtractFinalJsonObject(content, out var cleanedContent))
                {
                    _eventBus.Publish(new RecommendEvent(
                        RecommendEventType.RoleSummaryReady, context.SessionId, context.TurnId,
                        context.StageId, context.StageType, context.RoleId, lastTraceId,
                        cleanedContent, null, DateTime.UtcNow));

                    _eventBus.Publish(new RecommendEvent(
                        RecommendEventType.RoleCompleted, context.SessionId, context.TurnId,
                        context.StageId, context.StageType, context.RoleId, lastTraceId,
                        $"角色 {context.RoleId} 执行完成 (工具调用 {toolCallCount} 次)", null, DateTime.UtcNow));

                    return new RecommendRoleExecutionResult(
                        context.RoleId, Success: true, OutputJson: cleanedContent,
                        ErrorCode: null, ErrorMessage: null, LlmTraceId: lastTraceId,
                        ToolCallCount: toolCallCount);
                }

                invalidFinalResponseCount += 1;
                var invalidReason = hasToolCall
                    ? $"已达到工具调用上限 {maxCalls}，不能继续输出 tool_call。"
                    : "输出不是有效 JSON object。";

                if (invalidFinalResponseCount >= MaxInvalidFinalResponses)
                {
                    var errorMessage = $"角色 {context.RoleId} 连续 {invalidFinalResponseCount} 次返回非 JSON / 非法响应，已终止。最后原因：{invalidReason}";

                    _eventBus.Publish(new RecommendEvent(
                        RecommendEventType.RoleFailed, context.SessionId, context.TurnId,
                        context.StageId, context.StageType, context.RoleId, lastTraceId,
                        errorMessage, null, DateTime.UtcNow));

                    return new RecommendRoleExecutionResult(
                        context.RoleId, Success: false, OutputJson: null,
                        ErrorCode: "LLM_INVALID_JSON_RESPONSE", ErrorMessage: errorMessage,
                        LlmTraceId: lastTraceId, ToolCallCount: toolCallCount);
                }

                _eventBus.Publish(new RecommendEvent(
                    RecommendEventType.SystemNotice, context.SessionId, context.TurnId,
                    context.StageId, context.StageType, context.RoleId, lastTraceId,
                    $"角色 {context.RoleId} 输出不是有效 JSON，正在执行有界纠正 ({invalidFinalResponseCount}/{MaxInvalidFinalResponses - 1})...",
                    null, DateTime.UtcNow));

                prompt += BuildInvalidJsonCorrectionBlock(contract, content, invalidReason, toolCallCount < maxCalls);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Recommend role {RoleId} execution failed", context.RoleId);

            _sessionLogger?.LogRoleLlmError(context.SessionId, context.TurnId, context.RoleId,
                ex.GetType().Name, ex.Message);

            var sanitized = ErrorSanitizer.SanitizeErrorMessage(ex.Message);

            _eventBus.Publish(new RecommendEvent(
                RecommendEventType.RoleFailed, context.SessionId, context.TurnId,
                context.StageId, context.StageType, context.RoleId, null,
                $"角色 {context.RoleId} 执行失败: {sanitized}", null, DateTime.UtcNow));

            return new RecommendRoleExecutionResult(
                context.RoleId, Success: false, OutputJson: null,
                ErrorCode: "LLM_ERROR", ErrorMessage: sanitized,
                LlmTraceId: null, ToolCallCount: 0);
        }
    }

    private async Task<LlmChatResult> CallLlmWithRetryAsync(
        RecommendRoleExecutionContext context, LlmChatRequest request, CancellationToken ct)
    {
        const int maxRetries = 2;
        int[] backoffMs = [2000, 6000];

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await _llmService.ChatAsync("active", request, ct);
            }
            catch (Exception ex) when (attempt < maxRetries && IsRetryableException(ex, ct))
            {
                _logger.LogWarning(ex, "LLM call attempt {Attempt} failed for role {RoleId}, retrying",
                    attempt + 1, context.RoleId);

                _eventBus.Publish(new RecommendEvent(
                    RecommendEventType.SystemNotice, context.SessionId, context.TurnId,
                    context.StageId, context.StageType, context.RoleId, null,
                    $"角色 {context.RoleId} LLM 调用超时，正在重试 ({attempt + 2}/3)...", null, DateTime.UtcNow));

                await Task.Delay(backoffMs[attempt], ct);
            }
        }
    }

    private static bool IsRetryableException(Exception ex, CancellationToken ct)
    {
        // Never retry if the user explicitly cancelled
        if (ct.IsCancellationRequested)
            return false;

        return ex is TaskCanceledException
            || (ex is OperationCanceledException && !ct.IsCancellationRequested)
            || ex is HttpRequestException
            || (ex is InvalidOperationException && ex.Message.Contains("超时"));
    }

    private static bool IsToolError(string toolResult)
    {
        if (string.IsNullOrWhiteSpace(toolResult))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(toolResult);
            var root = doc.RootElement;
            if (root.TryGetProperty("tool_error", out var te) && te.ValueKind == JsonValueKind.True)
                return true;
            if (root.TryGetProperty("error", out _) && !root.TryGetProperty("data", out _))
                return true;
        }
        catch (JsonException)
        {
            // Not JSON → not a tool error structure
        }
        return false;
    }

    private static string BuildPrompt(RecommendRoleContract contract, RecommendRoleExecutionContext context)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(contract.SystemPrompt);
        sb.AppendLine();
        sb.AppendLine(RecommendPromptTemplates.BuildTimeContext());

        if (contract.MaxToolCalls > 0)
            sb.AppendLine(RecommendPromptTemplates.BuildToolRules(contract.MaxToolCalls));

        sb.AppendLine(RecommendPromptTemplates.QualityConstraints);

        if (!string.IsNullOrWhiteSpace(context.UpstreamArtifactsJson))
        {
            sb.AppendLine("## 上游阶段输出");
            sb.AppendLine(context.UpstreamArtifactsJson);
            sb.AppendLine();
        }

        sb.AppendLine("## 用户指令");
        sb.AppendLine(context.UserInput);
        sb.AppendLine();
        sb.AppendLine("## 最终输出 JSON schema");
        sb.AppendLine(contract.OutputSchemaDescription);
        sb.AppendLine();
        sb.AppendLine("## 输出硬约束");
        sb.AppendLine("- 如果还需要工具，一次只能输出一个 tool_call JSON，不要混入解释。");
        sb.AppendLine("- 如果分析结束，只能输出一个 JSON object，必须匹配上面的 schema。");
        sb.AppendLine("- 不要输出解释、Markdown、代码块、自然语言、反问、致歉，或“好的，请提供您需要我处理的请求。”之类的对话文本。");
        sb.AppendLine("- 信息不足时也必须按 schema 输出字段完整的 JSON object，不要等待用户继续补充。");

        return sb.ToString();
    }

    private static string BuildInvalidJsonCorrectionBlock(
        RecommendRoleContract contract,
        string content,
        string invalidReason,
        bool toolCallsAllowed)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## 输出纠正");
        sb.AppendLine($"上一条输出未通过协议校验：{invalidReason}");
        sb.AppendLine("上一条输出摘要：");
        sb.AppendLine(Truncate(content, 1200));
        if (toolCallsAllowed)
        {
            sb.AppendLine("如果仍需工具，只能输出单个 JSON：{\"tool_call\":{\"name\":\"工具名\",\"args\":{参数}}}");
        }
        else
        {
            sb.AppendLine("你已经达到工具调用上限，禁止再输出 tool_call。");
        }

        sb.AppendLine("如果不再需要工具，只能输出一个与以下 schema 匹配的 JSON object：");
        sb.AppendLine(contract.OutputSchemaDescription);
        sb.AppendLine("不要输出解释、Markdown、代码块、自然语言、反问、致歉，或“好的，请提供您需要我处理的请求。”之类的对话文本。");
        sb.AppendLine("如果信息不足，也必须按 schema 输出字段完整的 JSON object。");
        return sb.ToString();
    }

    /// <summary>
    /// Parse tool_call JSON from LLM output. Tolerant of surrounding text.
    /// Returns false on any parse failure — caller should treat content as final output.
    /// </summary>
    internal static bool TryParseToolCall(string content, out string toolName, out Dictionary<string, string> toolArgs)
    {
        toolName = "";
        toolArgs = new Dictionary<string, string>();

        var idx = content.IndexOf("{\"tool_call\"", StringComparison.Ordinal);
        if (idx < 0)
            return false;

        try
        {
            // Find matching closing brace using a depth counter
            var depth = 0;
            var endIdx = -1;
            for (var i = idx; i < content.Length; i++)
            {
                if (content[i] == '{') depth++;
                else if (content[i] == '}') { depth--; if (depth == 0) { endIdx = i; break; } }
            }
            if (endIdx < 0) return false;

            var jsonSlice = content[idx..(endIdx + 1)];
            using var doc = JsonDocument.Parse(jsonSlice);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tool_call", out var toolCallEl))
                return false;

            if (!toolCallEl.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                return false;

            toolName = nameEl.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(toolName))
                return false;

            if (toolCallEl.TryGetProperty("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in argsEl.EnumerateObject())
                {
                    toolArgs[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? ""
                        : prop.Value.GetRawText();
                }
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    /// <summary>
    /// Attempts to extract clean JSON from LLM output that may be wrapped in markdown code blocks
    /// or surrounded by extra text. Returns original content if no JSON object is found.
    /// </summary>
    internal static string TryCleanJsonOutput(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return content;

        var trimmed = content.Trim();

        // Already valid JSON object or array
        if ((trimmed.StartsWith('{') && trimmed.EndsWith('}')) ||
            (trimmed.StartsWith('[') && trimmed.EndsWith(']')))
        {
            return trimmed;
        }

        // Try to extract from markdown code block: ```json ... ``` or ``` ... ```
        var codeBlockPattern = @"```(?:json|JSON)?\s*\n?([\s\S]*?)\n?\s*```";
        var match = Regex.Match(trimmed, codeBlockPattern);
        if (match.Success)
        {
            var extracted = match.Groups[1].Value.Trim();
            if ((extracted.StartsWith('{') && extracted.EndsWith('}')) ||
                (extracted.StartsWith('[') && extracted.EndsWith(']')))
            {
                return extracted;
            }
        }

        // Try to find first JSON object in the text
        var braceStart = trimmed.IndexOf('{');
        if (braceStart >= 0)
        {
            var depth = 0;
            for (var i = braceStart; i < trimmed.Length; i++)
            {
                if (trimmed[i] == '{') depth++;
                else if (trimmed[i] == '}') { depth--; if (depth == 0) return trimmed[braceStart..(i + 1)]; }
            }
        }

        // No JSON found, return original
        return trimmed;
    }

    internal static bool TryExtractFinalJsonObject(string content, out string json)
    {
        json = "";

        var cleaned = TryCleanJsonOutput(content);
        if (string.IsNullOrWhiteSpace(cleaned))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            json = doc.RootElement.GetRawText();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
