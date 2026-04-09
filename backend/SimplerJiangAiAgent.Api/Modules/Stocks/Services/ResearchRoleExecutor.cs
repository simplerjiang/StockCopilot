using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public sealed record RoleExecutionContext(
    long SessionId, long TurnId, long StageId,
    string Symbol, string RoleId, string UserPrompt,
    IReadOnlyList<string> UpstreamArtifacts,
    string? PositionContext = null);

public sealed record RoleExecutionResult(
    string RoleId,
    ResearchRoleStatus Status,
    string? OutputContentJson,
    string? LlmTraceId,
    IReadOnlyList<string> DegradedFlags,
    string? ErrorCode,
    string? ErrorMessage,
    string? OutputRefsJson = null);

internal sealed record ResearchToolOutputRef(
    string ToolName,
    string Status,
    string Summary,
    string? ResultJson,
    string? ErrorMessage,
    IReadOnlyList<string> DegradedFlags);

public interface IResearchRoleExecutor
{
    Task<RoleExecutionResult> ExecuteRoleAsync(RoleExecutionContext context, CancellationToken cancellationToken = default);
}

public sealed class ResearchRoleExecutor : IResearchRoleExecutor
{
    private readonly IMcpToolGateway _mcpGateway;
    private readonly IRoleToolPolicyService _policyService;
    private readonly IStockAgentRoleContractRegistry _contractRegistry;
    private readonly ILlmService _llmService;
    private readonly IResearchEventBus _eventBus;
    private readonly ILogger<ResearchRoleExecutor> _logger;
    private readonly ILlmSettingsStore? _llmSettingsStore;

    internal sealed record PromptGovernancePlan(
        string ProviderKey,
        string Model,
        int NumCtx,
        int UpstreamBudgetChars,
        int ToolBudgetChars,
        int MaxArtifactChars,
        int MaxToolChars,
        int MaxStringChars,
        int MaxArrayItems,
        int MaxObjectProperties,
        int MaxDepth);

    internal sealed record PromptGovernanceStats(
        int OriginalUpstreamChars,
        int CompactedUpstreamChars,
        int OriginalToolChars,
        int CompactedToolChars,
        int TotalUserContentChars,
        int UpstreamArtifactCount,
        int ToolResultCount);

    public ResearchRoleExecutor(
        IMcpToolGateway mcpGateway,
        IRoleToolPolicyService policyService,
        IStockAgentRoleContractRegistry contractRegistry,
        ILlmService llmService,
        IResearchEventBus eventBus,
        ILogger<ResearchRoleExecutor> logger)
        : this(mcpGateway, policyService, contractRegistry, llmService, eventBus, logger, null)
    {
    }

    public ResearchRoleExecutor(
        IMcpToolGateway mcpGateway,
        IRoleToolPolicyService policyService,
        IStockAgentRoleContractRegistry contractRegistry,
        ILlmService llmService,
        IResearchEventBus eventBus,
        ILogger<ResearchRoleExecutor> logger,
        ILlmSettingsStore? llmSettingsStore)
    {
        _mcpGateway = mcpGateway;
        _policyService = policyService;
        _contractRegistry = contractRegistry;
        _llmService = llmService;
        _eventBus = eventBus;
        _logger = logger;
        _llmSettingsStore = llmSettingsStore;
    }

    private const int MaxLlmRetries = 2;
    private const int MaxToolRetries = 2;
    private const string ResearchToolTaskScopeSeparator = "::";
    private const int DefaultFinancialReportPeriods = 4;
    private const int DefaultFinancialTrendPeriods = 8;
    private static readonly TimeSpan DefaultToolTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MarketContextToolTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan LlmCallTimeout = TimeSpan.FromSeconds(180);
    private static readonly int[] LlmRetryDelaysMs = [2000, 5000];
    private static readonly int[] ToolRetryDelaysMs = [2000, 5000, 15000];
    // Limit concurrent MCP tool calls across all parallel roles to avoid external API throttling
    private static readonly SemaphoreSlim McpConcurrencyGate = new(4);
    private static readonly JsonSerializerOptions RelaxedJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly JsonSerializerOptions CompactJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashSet<string> _slimExcludedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        // Envelope metadata – not useful for analysis
        "traceId", "taskId", "latencyMs",
        "cache", "cacheHit",
        "freshnessTag", "sourceTier", "rolePolicyClass",
        "meta",
        // Evidence internal fields
        "crawledAt", "readMode", "readStatus", "ingestedAt",
        "localFactId", "sourceRecordId"
    };

    private static string SlimToolResultJson(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            WriteSlimElement(writer, doc.RootElement);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteSlimElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    if (_slimExcludedFields.Contains(prop.Name))
                        continue;
                    if (prop.Value.ValueKind == JsonValueKind.Null)
                        continue;
                    writer.WritePropertyName(prop.Name);
                    WriteSlimElement(writer, prop.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteSlimElement(writer, item);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
    private static readonly Dictionary<string, int> PropertyPriority = new(StringComparer.OrdinalIgnoreCase)
    {
        ["roleId"] = 0,
        ["headline"] = 1,
        ["summary"] = 2,
        ["claim"] = 3,
        ["analysis"] = 4,
        ["rationale"] = 5,
        ["executive_summary"] = 6,
        ["executiveSummary"] = 6,
        ["investment_thesis"] = 7,
        ["investmentThesis"] = 7,
        ["research_conclusion"] = 8,
        ["researchConclusion"] = 8,
        ["rating"] = 9,
        ["action"] = 10,
        ["direction"] = 11,
        ["proposal_assessment"] = 12,
        ["proposalAssessment"] = 12,
        ["confidence"] = 13,
        ["confidence_explanation"] = 14,
        ["confidenceExplanation"] = 14,
        ["converged"] = 15,
        ["key_points"] = 16,
        ["keyPoints"] = 16,
        ["open_questions"] = 17,
        ["openQuestions"] = 17,
        ["counter_points"] = 18,
        ["counterPoints"] = 18,
        ["counter_target_role"] = 19,
        ["counterTargetRole"] = 19,
        ["adopted_bull_points"] = 20,
        ["adoptedBullPoints"] = 20,
        ["adopted_bear_points"] = 21,
        ["adoptedBearPoints"] = 21,
        ["shelved_disputes"] = 22,
        ["shelvedDisputes"] = 22,
        ["risk_limits"] = 23,
        ["riskLimits"] = 23,
        ["invalidations"] = 24,
        ["invalidation_conditions"] = 24,
        ["invalidationConditions"] = 24,
        ["next_actions"] = 25,
        ["nextActions"] = 25,
        ["final_decision"] = 26,
        ["finalDecision"] = 26,
        ["entry_plan"] = 27,
        ["entryPlan"] = 27,
        ["exit_plan"] = 28,
        ["exitPlan"] = 28,
        ["position_sizing"] = 29,
        ["positionSizing"] = 29,
        ["evidence_refs"] = 30,
        ["evidenceRefs"] = 30,
        ["supporting_evidence_refs"] = 31,
        ["supportingEvidenceRefs"] = 31,
        ["counter_evidence_refs"] = 32,
        ["counterEvidenceRefs"] = 32,
        ["symbol"] = 33,
        ["name"] = 34,
        ["companyName"] = 35,
        ["mainBusiness"] = 36,
        ["businessScope"] = 37,
        ["industry"] = 38,
        ["csrcIndustry"] = 39,
        ["region"] = 40,
        ["stageLabel"] = 41,
        ["stageConfidence"] = 42,
        ["mainlineSectorName"] = 43,
        ["suggestedPositionScale"] = 44,
        ["executionFrequencyLabel"] = 45,
        ["isMainlineAligned"] = 46,
        ["counterTrendWarning"] = 47,
        ["price"] = 48,
        ["change"] = 49,
        ["changePercent"] = 50,
        ["tool"] = 51,
        ["evidenceCount"] = 52
    };
    private static readonly HashSet<string> SkippedPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "evidence_details",
        "evidenceDetails",
        "supporting_evidence",
        "supportingEvidence",
        "counter_evidence",
        "counterEvidence",
        "citations",
        "citationDetails",
        "raw",
        "rawText",
        "raw_text",
        "rawResponse",
        "raw_response",
        "html",
        "markdown",
        "body",
        "fullText",
        "full_text",
        "contentHtml",
        "content_html",
        "log",
        "logs",
        "traceId",
        "trace_id",
        "taskId",
        "task_id",
        "cache",
        "meta",
        "latencyMs",
        "latency_ms",
        "freshnessTag",
        "freshness_tag",
        "sourceTier",
        "source_tier",
        "rolePolicyClass",
        "role_policy_class",
        "cacheHit",
        "cache_hit",
        "errorCode",
        "error_code",
        "toolName",
        "tool_name",
        "features"
    };
    private static readonly HashSet<string> NoisyArrayPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "items",
        "points",
        "records",
        "series",
        "candles",
        "bars",
        "history",
        "snapshots",
        "kLines",
        "minuteLines",
        "minutes",
        "leaders",
        "components",
        "articles",
        "news",
        "evidence",
        "features",
        "quotes",
        "realtimeSeries",
        "boardItems"
    };

    public async Task<RoleExecutionResult> ExecuteRoleAsync(RoleExecutionContext context, CancellationToken cancellationToken = default)
    {
        var contract = _contractRegistry.GetRequired(context.RoleId);
        var degradedFlags = new List<string>();
        var toolResults = new List<string>();
        var toolOutputRefs = new List<ResearchToolOutputRef>();

        _eventBus.Publish(new ResearchEvent(
            ResearchEventType.RoleStarted,
            context.SessionId, context.TurnId, context.StageId,
            context.RoleId, null,
            $"Role {context.RoleId} started", null, DateTime.UtcNow));

        // Phase 1: Dispatch MCP tools in parallel if the role has direct query tools
        if (contract.AllowsDirectQueryTools && contract.PreferredMcpSequence.Count > 0)
        {
            var isLocalRequired = string.Equals(contract.ToolAccessMode, "local_required", StringComparison.OrdinalIgnoreCase);

            // Pre-authorize all tools; fail fast if any required tool is blocked
            var authorizedTools = new List<string>();
            foreach (var toolName in contract.PreferredMcpSequence)
            {
                var auth = _policyService.AuthorizeRole(context.RoleId, toolName);
                if (!auth.IsAllowed)
                {
                    if (isLocalRequired)
                    {
                        toolOutputRefs.Add(new ResearchToolOutputRef(
                            toolName,
                            "Blocked",
                            $"{toolName} 被策略拦截，无法执行。",
                            null,
                            auth.Reason,
                            new[] { $"tool_blocked:{toolName}" }));
                        return new RoleExecutionResult(context.RoleId, ResearchRoleStatus.Failed,
                            null, null, [$"tool_blocked:{toolName}"], "TOOL_BLOCKED",
                            $"Required tool {toolName} blocked: {auth.Reason}",
                            JsonSerializer.Serialize(toolOutputRefs, RelaxedJsonOptions));
                    }
                    degradedFlags.Add($"tool_unavailable:{toolName}");
                    toolOutputRefs.Add(new ResearchToolOutputRef(
                        toolName,
                        "Unavailable",
                        $"{toolName} 当前不可用，角色已按降级路径继续。",
                        null,
                        auth.Reason,
                        new[] { $"tool_unavailable:{toolName}" }));
                    continue;
                }
                authorizedTools.Add(toolName);
            }

            // Dispatch all authorized tools in parallel, each with its own retry loop
            if (authorizedTools.Count > 0)
            {
                foreach (var toolName in authorizedTools)
                {
                    _eventBus.Publish(new ResearchEvent(
                        ResearchEventType.ToolDispatched,
                        context.SessionId, context.TurnId, context.StageId,
                        context.RoleId, null,
                        $"正在调用 {toolName}",
                        JsonSerializer.Serialize(new { toolName, symbol = context.Symbol, requestedAt = DateTime.UtcNow }),
                        DateTime.UtcNow));
                }

                var parallelTasks = authorizedTools.Select(toolName => ExecuteToolWithRetryAsync(context, toolName, cancellationToken)).ToList();
                var outcomes = await Task.WhenAll(parallelTasks);

                foreach (var (toolName, outcome) in authorizedTools.Zip(outcomes))
                {
                    if (outcome.Success)
                    {
                        toolResults.Add($"[{toolName}]\n{outcome.ResultJson}");
                        toolOutputRefs.Add(new ResearchToolOutputRef(
                            toolName,
                            "Completed",
                            BuildToolSummary(toolName, outcome.ResultJson!),
                            outcome.ResultJson,
                            null,
                            Array.Empty<string>()));

                        var toolSummary = BuildToolSummary(toolName, outcome.ResultJson!);
                        _eventBus.Publish(new ResearchEvent(
                            ResearchEventType.ToolCompleted,
                            context.SessionId, context.TurnId, context.StageId,
                            context.RoleId, null,
                            $"{toolName} 完成",
                            JsonSerializer.Serialize(new { toolName, status = "Completed", summary = toolSummary, resultPreview = outcome.ResultJson }),
                            DateTime.UtcNow));
                    }
                    else
                    {
                        degradedFlags.Add($"tool_error:{toolName}");
                        toolOutputRefs.Add(new ResearchToolOutputRef(
                            toolName,
                            "Failed",
                            $"{toolName} 获取失败，已按降级路径继续。",
                            null,
                            "tool execution failed after retries",
                            new[] { $"tool_error:{toolName}" }));
                        _eventBus.Publish(new ResearchEvent(
                            ResearchEventType.ToolCompleted,
                            context.SessionId, context.TurnId, context.StageId,
                            context.RoleId, null,
                            $"{toolName} failed after retries", null, DateTime.UtcNow));
                    }
                }
            }
        }

        if (toolResults.Count < contract.MinimumEvidenceCount && contract.AllowsDirectQueryTools)
        {
            degradedFlags.Add($"insufficient_evidence:{toolResults.Count}/{contract.MinimumEvidenceCount}");
        }

        // Phase 2: Build prompt and call LLM (with retry)
        var systemPrompt = TradingWorkbenchPromptTemplates.GetSystemPrompt(context.RoleId);
        var promptGovernance = await ResolvePromptGovernanceAsync(cancellationToken);
        var userContent = BuildUserContent(context, toolResults, promptGovernance, out var promptStats);
        if (promptGovernance is not null && promptStats is not null)
        {
            _logger.LogInformation(
                "Role {RoleId}: local prompt governance applied provider={Provider} model={Model} numCtx={NumCtx} upstreamChars={OriginalUpstreamChars}->{CompactedUpstreamChars} toolChars={OriginalToolChars}->{CompactedToolChars} userContentChars={TotalUserContentChars} upstreamBudget={UpstreamBudgetChars} toolBudget={ToolBudgetChars}",
                context.RoleId,
                promptGovernance.ProviderKey,
                promptGovernance.Model,
                promptGovernance.NumCtx,
                promptStats.OriginalUpstreamChars,
                promptStats.CompactedUpstreamChars,
                promptStats.OriginalToolChars,
                promptStats.CompactedToolChars,
                promptStats.TotalUserContentChars,
                promptGovernance.UpstreamBudgetChars,
                promptGovernance.ToolBudgetChars);
        }
        var traceId = $"research-{context.SessionId}-{context.TurnId}-{context.RoleId}";
        Exception? lastLlmException = null;

        for (var attempt = 0; attempt <= MaxLlmRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var delayMs = LlmRetryDelaysMs[Math.Min(attempt - 1, LlmRetryDelaysMs.Length - 1)];
                    _eventBus.Publish(new ResearchEvent(
                        ResearchEventType.RetryAttempt,
                        context.SessionId, context.TurnId, context.StageId,
                        context.RoleId, null,
                        $"LLM retry attempt {attempt + 1}/{MaxLlmRetries + 1} (waiting {delayMs}ms)",
                        null, DateTime.UtcNow));
                    await Task.Delay(delayMs, cancellationToken);
                }

                using var llmCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                llmCts.CancelAfter(LlmCallTimeout);
                var llmResult = await _llmService.ChatAsync("active",
                    new LlmChatRequest($"{systemPrompt}\n\n{userContent}", null, 0.3, false, traceId,
                        ResponseFormat: LlmResponseFormats.Json,
                        MaxOutputTokens: 4096),
                    llmCts.Token);

                var feedSummary = ExtractFeedSummary(llmResult.Content);

                // Store full LLM content in DetailJson so frontend can show expandable report
                var roleSummaryDetail = feedSummary != llmResult.Content
                    ? llmResult.Content
                    : null;
                _eventBus.Publish(new ResearchEvent(
                    ResearchEventType.RoleSummaryReady,
                    context.SessionId, context.TurnId, context.StageId,
                    context.RoleId, llmResult.TraceId,
                    feedSummary, roleSummaryDetail, DateTime.UtcNow));

                var status = degradedFlags.Count > 0 ? ResearchRoleStatus.Degraded : ResearchRoleStatus.Completed;

                _eventBus.Publish(new ResearchEvent(
                    ResearchEventType.RoleCompleted,
                    context.SessionId, context.TurnId, context.StageId,
                    context.RoleId, llmResult.TraceId,
                    $"Role {context.RoleId} {status}", null, DateTime.UtcNow));

                return new RoleExecutionResult(context.RoleId, status,
                    JsonSerializer.Serialize(new { content = llmResult.Content }, RelaxedJsonOptions),
                    llmResult.TraceId, degradedFlags, null, null,
                    JsonSerializer.Serialize(toolOutputRefs, RelaxedJsonOptions));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (attempt < MaxLlmRetries)
            {
                lastLlmException = ex;
                _logger.LogWarning(ex, "Role {RoleId}: LLM attempt {Attempt} failed, will retry",
                    context.RoleId, attempt + 1);
            }
            catch (Exception ex)
            {
                lastLlmException = ex;
            }
        }

        // All LLM retries exhausted
        _logger.LogError(lastLlmException, "Role {RoleId}: LLM failed after {MaxAttempts} attempts",
            context.RoleId, MaxLlmRetries + 1);

        _eventBus.Publish(new ResearchEvent(
            ResearchEventType.RoleFailed,
            context.SessionId, context.TurnId, context.StageId,
            context.RoleId, null,
            $"Role {context.RoleId} LLM failed after {MaxLlmRetries + 1} attempts: {lastLlmException?.Message}",
            null, DateTime.UtcNow));

        return new RoleExecutionResult(context.RoleId, ResearchRoleStatus.Failed,
            null, null, degradedFlags, "LLM_FAILED", lastLlmException?.Message,
            JsonSerializer.Serialize(toolOutputRefs, RelaxedJsonOptions));
    }

    private sealed record ToolOutcome(bool Success, string? ResultJson);

    private async Task<ToolOutcome> ExecuteToolWithRetryAsync(RoleExecutionContext context, string toolName, CancellationToken cancellationToken)
    {
        var toolTimeout = ResolveToolTimeout(toolName);

        for (var attempt = 0; attempt <= MaxToolRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var delayMs = ToolRetryDelaysMs[Math.Min(attempt - 1, ToolRetryDelaysMs.Length - 1)];
                    _eventBus.Publish(new ResearchEvent(
                        ResearchEventType.RetryAttempt,
                        context.SessionId, context.TurnId, context.StageId,
                        context.RoleId, null,
                        $"Retrying {toolName} (attempt {attempt + 1}, backoff {delayMs}ms)", null, DateTime.UtcNow));
                    await Task.Delay(delayMs, cancellationToken);
                }

                await McpConcurrencyGate.WaitAsync(cancellationToken);
                try
                {
                    using var toolCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    toolCts.CancelAfter(toolTimeout);
                    var toolResult = await DispatchToolAsync(context, toolName, toolCts.Token);
                    return new ToolOutcome(true, toolResult);
                }
                finally
                {
                    McpConcurrencyGate.Release();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (OperationCanceledException ex) when (attempt >= MaxToolRetries)
            {
                _logger.LogWarning(ex, "Role {RoleId}: tool {Tool} timed out after {Timeout}s (attempt {Attempt})",
                    context.RoleId, toolName, toolTimeout.TotalSeconds, attempt + 1);
            }
            catch (Exception ex) when (attempt < MaxToolRetries)
            {
                _logger.LogWarning(ex, "Role {RoleId}: tool {Tool} failed (attempt {Attempt}), will retry",
                    context.RoleId, toolName, attempt + 1);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Role {RoleId}: tool {Tool} failed after {MaxAttempts} attempts",
                    context.RoleId, toolName, MaxToolRetries + 1);
            }
        }
        return new ToolOutcome(false, null);
    }

    internal static TimeSpan ResolveToolTimeout(string toolName)
    {
        return string.Equals(toolName, StockMcpToolNames.MarketContext, StringComparison.Ordinal)
            ? MarketContextToolTimeout
            : DefaultToolTimeout;
    }

    private async Task<string> DispatchToolAsync(RoleExecutionContext context, string toolName, CancellationToken ct)
    {
        var symbol = context.Symbol;
        var taskId = BuildScopedToolTaskId(context, toolName);

        return toolName switch
        {
            StockMcpToolNames.CompanyOverview => SlimToolResultJson(JsonSerializer.Serialize(await _mcpGateway.GetCompanyOverviewAsync(symbol, taskId, null, ct), CompactJsonOptions)),
            StockMcpToolNames.Product => SlimToolResultJson(JsonSerializer.Serialize(await _mcpGateway.GetProductAsync(symbol, taskId, null, ct), CompactJsonOptions)),
            StockMcpToolNames.Fundamentals => SlimToolResultJson(JsonSerializer.Serialize(await _mcpGateway.GetFundamentalsAsync(symbol, taskId, null, ct), CompactJsonOptions)),
            StockMcpToolNames.FinancialReport => SlimToolResultJson(JsonSerializer.Serialize(await _mcpGateway.GetFinancialReportAsync(symbol, DefaultFinancialReportPeriods, taskId, ct), CompactJsonOptions)),
            StockMcpToolNames.FinancialTrend => SlimToolResultJson(JsonSerializer.Serialize(await _mcpGateway.GetFinancialTrendAsync(symbol, DefaultFinancialTrendPeriods, taskId, ct), CompactJsonOptions)),
            StockMcpToolNames.Shareholder => SlimToolResultJson(JsonSerializer.Serialize(await _mcpGateway.GetShareholderAsync(symbol, taskId, null, ct), CompactJsonOptions)),
            StockMcpToolNames.MarketContext => SlimToolResultJson(JsonSerializer.Serialize(await _mcpGateway.GetMarketContextAsync(symbol, taskId, null, ct), CompactJsonOptions)),
            StockMcpToolNames.SocialSentiment => SlimToolResultJson(JsonSerializer.Serialize(await _mcpGateway.GetSocialSentimentAsync(symbol, taskId, null, ct), CompactJsonOptions)),
            StockMcpToolNames.Kline => SlimToolResultJson(JsonSerializer.Serialize(await _mcpGateway.GetKlineAsync(symbol, "day", 60, null, taskId, null, ct), CompactJsonOptions)),
            StockMcpToolNames.Minute => SlimToolResultJson(JsonSerializer.Serialize(await _mcpGateway.GetMinuteAsync(symbol, null, taskId, null, ct), CompactJsonOptions)),
            StockMcpToolNames.Strategy => SlimToolResultJson(JsonSerializer.Serialize(await _mcpGateway.GetStrategyAsync(symbol, "day", 60, null, null, taskId, null, ct), CompactJsonOptions)),
            StockMcpToolNames.News => SlimToolResultJson(JsonSerializer.Serialize(await _mcpGateway.GetNewsAsync(symbol, "stock", taskId, null, ct), CompactJsonOptions)),
            StockMcpToolNames.Search => SlimToolResultJson(JsonSerializer.Serialize(await _mcpGateway.SearchAsync(symbol, true, taskId, ct), CompactJsonOptions)),
            _ => throw new ArgumentException($"Unknown tool: {toolName}")
        };
    }

    private static string BuildScopedToolTaskId(RoleExecutionContext context, string toolName)
    {
        var normalizedSymbol = StockSymbolNormalizer.Normalize(context.Symbol);
        return $"research:{context.SessionId}:{context.TurnId}:{normalizedSymbol}{ResearchToolTaskScopeSeparator}{toolName}";
    }

    private async Task<PromptGovernancePlan?> ResolvePromptGovernanceAsync(CancellationToken cancellationToken)
    {
        if (_llmSettingsStore is null)
        {
            return null;
        }

        try
        {
            var activeProviderKey = await _llmSettingsStore.GetActiveProviderKeyAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(activeProviderKey))
            {
                _logger.LogWarning("Role prompt governance resolution returned no active provider; applying fail-safe compact prompt governance");
                return CreateFailSafePromptGovernance();
            }

            var settings = await _llmSettingsStore.GetProviderAsync(activeProviderKey, cancellationToken);
            if (settings is null)
            {
                _logger.LogWarning("Role prompt governance resolution could not load settings for provider {Provider}; applying fail-safe compact prompt governance", activeProviderKey);
                return CreateFailSafePromptGovernance();
            }

            if (!OllamaRuntimeDefaults.IsOllamaProvider(activeProviderKey, settings?.ProviderType))
            {
                return null;
            }

            return CreateLocalPromptGovernance(
                OllamaRuntimeDefaults.ResolveNumCtx(settings?.OllamaNumCtx),
                activeProviderKey,
                settings?.Model ?? string.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Role prompt governance resolution failed; applying fail-safe compact prompt governance");
            return CreateFailSafePromptGovernance();
        }
    }

    private static PromptGovernancePlan CreateFailSafePromptGovernance()
    {
        return CreateLocalPromptGovernance(
            Math.Max(1024, OllamaRuntimeDefaults.NumCtx / 2),
            "failsafe_compact",
            "resolution_failed");
    }

    internal static PromptGovernancePlan CreateLocalPromptGovernance(int numCtx, string providerKey = "ollama", string? model = null)
    {
        var totalPromptBudget = Math.Clamp(numCtx * 6, 8000, 48000);
        var upstreamBudget = (int)Math.Round(totalPromptBudget * 0.45d, MidpointRounding.AwayFromZero);
        var toolBudget = totalPromptBudget - upstreamBudget;

        return new PromptGovernancePlan(
            providerKey,
            model ?? string.Empty,
            numCtx,
            upstreamBudget,
            int.MaxValue,                                  // ToolBudgetChars – no cap
            Math.Clamp(upstreamBudget / 4, 900, 2800),    // MaxArtifactChars
            int.MaxValue,                                  // MaxToolChars – no cap
            100_000,                                       // MaxStringChars – effectively unlimited
            10_000,                                        // MaxArrayItems – effectively unlimited
            10_000,                                        // MaxObjectProperties – effectively unlimited
            20);                                           // MaxDepth – generous
    }

    internal static string BuildUserContent(
        RoleExecutionContext context,
        IReadOnlyList<string> toolResults,
        PromptGovernancePlan? governance,
        out PromptGovernanceStats? stats)
    {
        IReadOnlyList<string> upstreamArtifacts = context.UpstreamArtifacts;
        IReadOnlyList<string> localToolResults = toolResults;
        var originalUpstreamChars = context.UpstreamArtifacts.Sum(static item => item.Length);
        var originalToolChars = toolResults.Sum(static item => item.Length);
        var compactedUpstreamChars = originalUpstreamChars;
        var compactedToolChars = originalToolChars;

        if (governance is not null)
        {
            upstreamArtifacts = CompactUpstreamArtifacts(context.UpstreamArtifacts, governance, out compactedUpstreamChars);
            localToolResults = CompactToolResults(toolResults, governance, out compactedToolChars);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"## 目标个股: {context.Symbol}");
        sb.AppendLine($"## 用户意图: {context.UserPrompt}");

        if (!string.IsNullOrEmpty(context.PositionContext))
        {
            sb.AppendLine($"\n## 用户持仓信息:\n{context.PositionContext}");
        }

        if (governance is not null && (upstreamArtifacts.Count > 0 || localToolResults.Count > 0))
        {
            sb.AppendLine("\n## 本地模型上下文压缩说明:");
            sb.AppendLine("以下上游产出与工具结果已压缩，只保留关键信息、评级、风险限制和证据引用，长数组与噪声细节已省略。");
        }

        if (upstreamArtifacts.Count > 0)
        {
            sb.AppendLine("\n## 上游角色产出:");
            foreach (var a in upstreamArtifacts)
            {
                sb.AppendLine(a);
                sb.AppendLine("---");
            }
        }

        if (localToolResults.Count > 0)
        {
            sb.AppendLine("\n## 本地工具数据:");
            foreach (var r in localToolResults)
            {
                sb.AppendLine(r);
                sb.AppendLine("---");
            }
        }

        stats = governance is null
            ? null
            : new PromptGovernanceStats(
                originalUpstreamChars,
                compactedUpstreamChars,
                originalToolChars,
                compactedToolChars,
                sb.Length,
                context.UpstreamArtifacts.Count,
                toolResults.Count);

        return sb.ToString();
    }

    private static IReadOnlyList<string> CompactUpstreamArtifacts(
        IReadOnlyList<string> upstreamArtifacts,
        PromptGovernancePlan governance,
        out int compactedChars)
    {
        var compacted = upstreamArtifacts
            .Select(item => CompactUpstreamArtifact(item, governance))
            .ToArray();

        return ApplySectionBudget(compacted, governance.UpstreamBudgetChars, governance.MaxArtifactChars, "上游产出", out compactedChars);
    }

    private static IReadOnlyList<string> CompactToolResults(
        IReadOnlyList<string> toolResults,
        PromptGovernancePlan governance,
        out int compactedChars)
    {
        var compacted = toolResults
            .Select(item => CompactToolResult(item, governance))
            .ToArray();

        return ApplySectionBudget(compacted, governance.ToolBudgetChars, governance.MaxToolChars, "工具结果", out compactedChars);
    }

    private static IReadOnlyList<string> ApplySectionBudget(
        IReadOnlyList<string> items,
        int totalBudget,
        int perItemBudget,
        string itemLabel,
        out int compactedChars)
    {
        if (items.Count == 0)
        {
            compactedChars = 0;
            return Array.Empty<string>();
        }

        var output = new List<string>(items.Count);
        var used = 0;
        var processed = 0;

        foreach (var item in items)
        {
            processed++;
            var boundedItem = TruncateAtBoundary(item, perItemBudget);
            if (string.IsNullOrWhiteSpace(boundedItem))
            {
                continue;
            }

            if (used + boundedItem.Length > totalBudget)
            {
                var remaining = totalBudget - used;
                if (remaining > 180)
                {
                    var truncated = TruncateAtBoundary(boundedItem, remaining);
                    if (!string.IsNullOrWhiteSpace(truncated))
                    {
                        output.Add(truncated);
                        used += truncated.Length;
                    }
                }
                break;
            }

            output.Add(boundedItem);
            used += boundedItem.Length;
        }

        var omitted = Math.Max(0, items.Count - processed);
        if (omitted > 0)
        {
            var note = $"[context_governance]\n为适配本地模型上下文，已省略 {omitted} 条{itemLabel}。";
            if (used + note.Length <= totalBudget + 120)
            {
                output.Add(note);
                used += note.Length;
            }
        }

        compactedChars = output.Sum(static item => item.Length);
        return output;
    }

    private static string CompactUpstreamArtifact(string artifact, PromptGovernancePlan governance)
    {
        var (roleId, content) = ResearchRunner.SplitRoleOutput(artifact);
        var resolvedRoleId = string.IsNullOrWhiteSpace(roleId) ? "upstream" : roleId;
        var jsonText = TryExtractJsonText(content);

        if (jsonText is null)
        {
            var normalized = NormalizeWhitespace(content);
            return $"[{resolvedRoleId}]\n" + JsonSerializer.Serialize(new
            {
                roleId = resolvedRoleId,
                fallbackType = ResolveFallbackType(content),
                charCount = normalized.Length,
                preview = TruncateAtBoundary(normalized, ResolveFallbackPreviewChars(governance))
            }, RelaxedJsonOptions);
        }

        using var doc = JsonDocument.Parse(jsonText);
        var compact = CompactJsonElement(doc.RootElement, governance, 0, null);
        Dictionary<string, object?> payload;

        if (compact is Dictionary<string, object?> compactObject)
        {
            payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["roleId"] = resolvedRoleId
            };

            foreach (var key in OrderPropertyNames(compactObject.Keys))
            {
                if (string.Equals(key, "roleId", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                payload[key] = compactObject[key];
            }
        }
        else
        {
            payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["roleId"] = resolvedRoleId,
                ["content"] = compact
            };
        }

        return $"[{resolvedRoleId}]\n{JsonSerializer.Serialize(payload, CompactJsonOptions)}";
    }

    private static string CompactToolResult(string toolResult, PromptGovernancePlan governance)
    {
        var (toolName, content) = ResearchRunner.SplitRoleOutput(toolResult);
        var resolvedToolName = string.IsNullOrWhiteSpace(toolName) ? "tool" : toolName;
        var jsonText = TryExtractJsonText(content);

        if (jsonText is null)
        {
            var normalized = NormalizeWhitespace(content);
            return $"[{resolvedToolName}]\n" + JsonSerializer.Serialize(new
            {
                tool = resolvedToolName,
                summary = $"{resolvedToolName}: 已使用结构化回退摘要",
                fallbackType = ResolveFallbackType(content),
                charCount = normalized.Length,
                preview = TruncateAtBoundary(normalized, ResolveFallbackPreviewChars(governance))
            }, RelaxedJsonOptions);
        }

        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["tool"] = resolvedToolName,
            ["summary"] = BuildToolSummary(resolvedToolName, jsonText)
        };

        if (TryGetPropertyIgnoreCase(root, "degradedFlags", out var degradedFlags) && degradedFlags.ValueKind == JsonValueKind.Array)
        {
            payload["degradedFlags"] = CompactJsonElement(degradedFlags, governance, 0, "degradedFlags");
        }

        if (TryGetPropertyIgnoreCase(root, "warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array)
        {
            payload["warnings"] = CompactJsonElement(warnings, governance, 0, "warnings");
        }

        if (TryGetPropertyIgnoreCase(root, "errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
        {
            payload["errors"] = CompactJsonElement(errors, governance, 0, "errors");
        }

        if (TryGetPropertyIgnoreCase(root, "evidence", out var evidence) && evidence.ValueKind == JsonValueKind.Array)
        {
            payload["evidenceCount"] = evidence.GetArrayLength();

            var evidenceRefs = BuildEvidenceRefs(evidence, governance);
            if (evidenceRefs.Count > 0)
            {
                payload["evidenceRefs"] = evidenceRefs;
            }
        }

        if (TryGetPropertyIgnoreCase(root, "data", out var data))
        {
            payload["data"] = CompactJsonElement(data, governance, 0, "data");
        }
        else
        {
            payload["data"] = CompactJsonElement(root, governance, 0, "data");
        }

        return $"[{resolvedToolName}]\n{JsonSerializer.Serialize(payload, CompactJsonOptions)}";
    }

    private static IReadOnlyList<string> BuildEvidenceRefs(JsonElement evidenceArray, PromptGovernancePlan governance)
    {
        var refs = new List<string>();
        var index = 0;

        foreach (var item in evidenceArray.EnumerateArray())
        {
            if (index >= governance.MaxArrayItems)
            {
                break;
            }

            index++;
            if (item.ValueKind == JsonValueKind.String)
            {
                refs.Add(TruncateAtBoundary(NormalizeWhitespace(item.GetString()), governance.MaxStringChars));
                continue;
            }

            if (item.ValueKind == JsonValueKind.Object)
            {
                var parts = new List<string>();
                var source = ReadStringProperty(item, "source") ?? ReadStringProperty(item, "provider");
                var title = ReadStringProperty(item, "title") ?? ReadStringProperty(item, "headline") ?? ReadStringProperty(item, "id");
                var publishedAt = ReadStringProperty(item, "publishedAt") ?? ReadStringProperty(item, "published_at");

                if (!string.IsNullOrWhiteSpace(source)) parts.Add(source);
                if (!string.IsNullOrWhiteSpace(title)) parts.Add(title);
                if (!string.IsNullOrWhiteSpace(publishedAt)) parts.Add(publishedAt);

                refs.Add(parts.Count > 0
                    ? TruncateAtBoundary(string.Join(" | ", parts), governance.MaxStringChars)
                    : TruncateAtBoundary(item.GetRawText(), governance.MaxStringChars));
                continue;
            }

            refs.Add(TruncateAtBoundary(item.GetRawText(), governance.MaxStringChars));
        }

        var total = evidenceArray.GetArrayLength();
        if (total > refs.Count)
        {
            refs.Add($"... {total - refs.Count} more evidence refs ...");
        }

        return refs;
    }

    private static object? CompactJsonElement(JsonElement element, PromptGovernancePlan governance, int depth, string? propertyName)
    {
        if (depth >= governance.MaxDepth)
        {
            return SummarizeElement(element, governance);
        }

        return element.ValueKind switch
        {
            JsonValueKind.Object => CompactObject(element, governance, depth),
            JsonValueKind.Array => CompactArray(element, governance, depth, propertyName),
            JsonValueKind.String => CompactStringValue(element.GetString(), governance),
            JsonValueKind.Number => CompactNumber(element),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => TruncateAtBoundary(element.GetRawText(), governance.MaxStringChars)
        };
    }

    private static object? CompactStringValue(string? value, PromptGovernancePlan governance)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (IsGarbledText(value))
            return null;

        return TruncateAtBoundary(NormalizeWhitespace(value), governance.MaxStringChars);
    }

    /// <summary>
    /// Detects if a string contains excessive garbled/mojibake characters.
    /// Returns true if the string is predominantly garbled and should be discarded.
    /// </summary>
    private static bool IsGarbledText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 20)
            return false;

        var garbledCount = 0;
        var totalCount = 0;

        foreach (var ch in text)
        {
            totalCount++;
            // Count characters that are likely mojibake or garbled encoding:
            // - Private Use Area (U+E000-U+F8FF)
            // - Control chars except common whitespace
            // - Hebrew block (U+0590-U+05FF) — garbled CJK indicator
            // - Combining diacriticals (U+0300-U+036F)
            // - NKo block (U+07F0-U+07FF) — rare, garbled indicator
            if ((ch >= '\uE000' && ch <= '\uF8FF') ||
                (ch < ' ' && ch != '\n' && ch != '\r' && ch != '\t') ||
                (ch >= '\u0590' && ch <= '\u05FF') ||
                (ch >= '\u0300' && ch <= '\u036F') ||
                (ch >= '\u07F0' && ch <= '\u07FF'))
            {
                garbledCount++;
            }
        }

        // If more than 15% of characters are garbled indicators, consider it garbled
        return totalCount > 0 && (double)garbledCount / totalCount > 0.15;
    }

    private static Dictionary<string, object?> CompactObject(JsonElement element, PromptGovernancePlan governance, int depth)
    {
        var properties = element.EnumerateObject()
            .Where(property => !SkippedPropertyNames.Contains(property.Name))
            .OrderBy(property => GetPropertyPriority(property.Name))
            .ThenBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var property in properties)
        {
            if (added >= governance.MaxObjectProperties && GetPropertyPriority(property.Name) >= 1000)
            {
                break;
            }

            var compacted = CompactJsonElement(property.Value, governance, depth + 1, property.Name);

            // Skip null values - they waste context without providing information
            if (compacted is null)
                continue;

            // Skip empty collections - they add no value
            if (compacted is System.Collections.ICollection { Count: 0 })
                continue;

            result[property.Name] = compacted;
            added++;
        }

        if (properties.Length > result.Count)
        {
            result["_omittedPropertyCount"] = properties.Length - result.Count;
        }

        return result;
    }

    private static object CompactArray(JsonElement element, PromptGovernancePlan governance, int depth, string? propertyName)
    {
        var items = element.EnumerateArray().ToArray();
        if (items.Length == 0)
        {
            return Array.Empty<object?>();
        }

        if (ShouldSummarizeNoisyArray(items, governance, propertyName))
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["count"] = items.Length,
                ["sample"] = BuildArraySample(items, governance, depth + 1)
            };
        }

        return BuildArraySample(items, governance, depth + 1);
    }

    private static bool ShouldSummarizeNoisyArray(JsonElement[] items, PromptGovernancePlan governance, string? propertyName)
    {
        return !string.IsNullOrWhiteSpace(propertyName)
            && NoisyArrayPropertyNames.Contains(propertyName)
            && items.Length > governance.MaxArrayItems * 2;
    }

    private static IReadOnlyList<object?> BuildArraySample(JsonElement[] items, PromptGovernancePlan governance, int depth)
    {
        if (items.Length <= governance.MaxArrayItems)
        {
            return items.Select(item => CompactJsonElement(item, governance, depth, null)).ToArray();
        }

        var headCount = Math.Max(1, governance.MaxArrayItems - 1);
        var sample = new List<object?>(governance.MaxArrayItems + 1);

        for (var index = 0; index < headCount; index++)
        {
            sample.Add(CompactJsonElement(items[index], governance, depth, null));
        }

        sample.Add($"... {items.Length - governance.MaxArrayItems} more items ...");
        sample.Add(CompactJsonElement(items[^1], governance, depth, null));
        return sample;
    }

    private static object SummarizeElement(JsonElement element, PromptGovernancePlan governance)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => $"object({element.EnumerateObject().Count()} props)",
            JsonValueKind.Array => $"array({element.GetArrayLength()} items)",
            JsonValueKind.String => TruncateAtBoundary(NormalizeWhitespace(element.GetString()), governance.MaxStringChars),
            JsonValueKind.Number => CompactNumber(element),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Undefined => string.Empty,
            _ => TruncateAtBoundary(element.GetRawText(), governance.MaxStringChars)
        };
    }

    private static object CompactNumber(JsonElement element)
    {
        if (element.TryGetInt64(out var int64Value))
        {
            return int64Value;
        }

        if (element.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        return element.GetRawText();
    }

    private static IEnumerable<string> OrderPropertyNames(IEnumerable<string> propertyNames)
    {
        return propertyNames
            .OrderBy(GetPropertyPriority)
            .ThenBy(static name => name, StringComparer.OrdinalIgnoreCase);
    }

    private static int GetPropertyPriority(string propertyName)
    {
        return PropertyPriority.TryGetValue(propertyName, out var priority)
            ? priority
            : 1000;
    }

    private static string? TryExtractJsonText(string content)
    {
        var trimmed = content.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        var objectStart = trimmed.IndexOf('{');
        var arrayStart = trimmed.IndexOf('[');
        var start = objectStart < 0
            ? arrayStart
            : arrayStart < 0
                ? objectStart
                : Math.Min(objectStart, arrayStart);

        if (start < 0)
        {
            return null;
        }

        var candidate = trimmed[start..].Trim();

        try
        {
            var unwrapped = candidate.StartsWith("{", StringComparison.Ordinal)
                ? ResearchRunner.UnwrapContentWrapper(candidate)
                : candidate;
            using var doc = JsonDocument.Parse(unwrapped);
            return doc.RootElement.GetRawText();
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveFallbackType(string content)
    {
        var trimmed = content.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "plain_text";
        }

        var objectStart = trimmed.IndexOf('{');
        var arrayStart = trimmed.IndexOf('[');
        var start = objectStart < 0
            ? arrayStart
            : arrayStart < 0
                ? objectStart
                : Math.Min(objectStart, arrayStart);

        return start >= 0 && start <= 32
            ? "malformed_json"
            : "plain_text";
    }

    private static int ResolveFallbackPreviewChars(PromptGovernancePlan governance)
    {
        return Math.Clamp(governance.MaxStringChars, 120, 220);
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? ReadStringProperty(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => NormalizeWhitespace(value.GetString()),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        var lastWasWhitespace = false;

        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasWhitespace)
                {
                    sb.Append(' ');
                    lastWasWhitespace = true;
                }

                continue;
            }

            sb.Append(ch);
            lastWasWhitespace = false;
        }

        return sb.ToString().Trim();
    }

    private static string TruncateAtBoundary(string? value, int maxLength)
    {
        var normalized = NormalizeWhitespace(value);
        if (string.IsNullOrEmpty(normalized) || normalized.Length <= maxLength)
        {
            return normalized;
        }

        var truncated = normalized[..maxLength].TrimEnd();
        var boundary = truncated.LastIndexOfAny(new[] { '。', '；', ';', '，', ',', ' ' });
        if (boundary >= maxLength / 2)
        {
            truncated = truncated[..boundary].TrimEnd();
        }

        return $"{truncated}...";
    }

    private static string BuildToolSummary(string toolName, string toolResultJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolResultJson);
            var root = doc.RootElement;
            var evidenceCount = root.TryGetProperty("evidence", out var evidenceNode) && evidenceNode.ValueKind == JsonValueKind.Array
                ? evidenceNode.GetArrayLength()
                : 0;

            if (root.TryGetProperty("data", out var dataNode) && dataNode.ValueKind == JsonValueKind.Object)
            {
                var fragments = new List<string>();
                foreach (var prop in dataNode.EnumerateObject())
                {
                    if (fragments.Count >= 3)
                    {
                        break;
                    }

                    if (prop.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                    {
                        fragments.Add($"{prop.Name}={prop.Value}");
                    }
                }

                if (fragments.Count > 0)
                {
                    return $"{toolName}: {string.Join("，", fragments)}；evidence={evidenceCount}";
                }
            }

            return $"{toolName}: 已获取结果；evidence={evidenceCount}";
        }
        catch
        {
            return $"{toolName}: 已获取结果";
        }
    }

    /// <summary>Extract a human-readable summary from LLM output for feed display.</summary>
    internal static string ExtractFeedSummary(string? llmContent, int maxLength = 600)
    {
        if (string.IsNullOrWhiteSpace(llmContent)) return "分析完成";

        try
        {
            var jsonStart = llmContent.IndexOf('{');
            var jsonEnd = llmContent.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                using var doc = JsonDocument.Parse(llmContent[jsonStart..(jsonEnd + 1)]);
                var root = doc.RootElement;
                foreach (var field in new[] { "summary", "analysis", "headline", "executive_summary",
                    "rationale", "recommendation", "conclusion", "verdict", "assessment", "claim" })
                {
                    if (root.TryGetProperty(field, out var val) && val.ValueKind == JsonValueKind.String)
                    {
                        var text = val.GetString();
                        if (!string.IsNullOrWhiteSpace(text)) return text;
                    }
                }

                // No named summary field found — try first substantial string property
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var text = prop.Value.GetString();
                        if (text is not null && text.Length > 30) return text;
                    }
                }
            }
        }
        catch { /* JSON parse failed — try plain text */ }

        // Fallback: if it looks like raw JSON, give generic message
        var trimmed = llmContent.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            return "分析完成（详见研究报告）";

        // Plain text/markdown — use as-is, truncated
        return llmContent.Length > maxLength ? llmContent[..maxLength] + "..." : llmContent;
    }
}
