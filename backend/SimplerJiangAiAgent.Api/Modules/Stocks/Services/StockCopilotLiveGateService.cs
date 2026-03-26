using System.Text;
using System.Text.Json;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IStockCopilotLiveGateService
{
    Task<StockCopilotLiveGateResultDto> RunAsync(StockCopilotLiveGateRequestDto request, CancellationToken cancellationToken = default);
}

public sealed class StockCopilotLiveGateService : IStockCopilotLiveGateService
{
    private const int MaxPlannedToolCalls = 6;
    private const int MaxExternalSearchCalls = 1;
    private const int DefaultReplaySampleTake = 40;
    private const int MaxTotalLatencyMs = 15000;
    private const int MaxPollingSteps = 2000;

    private readonly IStockChatHistoryService _chatHistoryService;
    private readonly ILlmService _llmService;
    private readonly IMcpToolGateway _mcpToolGateway;
    private readonly IRoleToolPolicyService _roleToolPolicyService;
    private readonly IMcpServiceRegistry _mcpServiceRegistry;
    private readonly IStockAgentRoleContractRegistry _roleContractRegistry;
    private readonly IStockMarketContextService _marketContextService;
    private readonly IStockCopilotAcceptanceService _acceptanceService;

    public StockCopilotLiveGateService(
        IStockChatHistoryService chatHistoryService,
        ILlmService llmService,
        IMcpToolGateway mcpToolGateway,
        IRoleToolPolicyService roleToolPolicyService,
        IMcpServiceRegistry mcpServiceRegistry,
        IStockAgentRoleContractRegistry roleContractRegistry,
        IStockMarketContextService marketContextService,
        IStockCopilotAcceptanceService acceptanceService)
    {
        _chatHistoryService = chatHistoryService;
        _llmService = llmService;
        _mcpToolGateway = mcpToolGateway;
        _roleToolPolicyService = roleToolPolicyService;
        _mcpServiceRegistry = mcpServiceRegistry;
        _roleContractRegistry = roleContractRegistry;
        _marketContextService = marketContextService;
        _acceptanceService = acceptanceService;
    }

    public async Task<StockCopilotLiveGateResultDto> RunAsync(StockCopilotLiveGateRequestDto request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Question);

        var normalizedSymbol = StockSymbolNormalizer.Normalize(request.Symbol);
        var trimmedQuestion = request.Question.Trim();
        var sessionTitle = string.IsNullOrWhiteSpace(request.SessionTitle)
            ? BuildSessionTitle(trimmedQuestion)
            : request.SessionTitle.Trim();
        var provider = string.IsNullOrWhiteSpace(request.Provider) ? "active" : request.Provider.Trim();

        var session = await _chatHistoryService.CreateSessionAsync(normalizedSymbol, sessionTitle, request.SessionKey, cancellationToken);
        var marketContext = await _marketContextService.GetLatestAsync(normalizedSymbol, cancellationToken);
        var checklist = _roleContractRegistry.BuildChecklist();
        var prompt = BuildPrompt(normalizedSymbol, trimmedQuestion, request.AllowExternalSearch, marketContext, checklist);
        var llmResult = await _llmService.ChatAsync(
            provider,
            new LlmChatRequest(prompt, request.Model, request.Temperature, false),
            cancellationToken);

        var rawModelResponse = llmResult.Content?.Trim() ?? string.Empty;
        var llmTraceId = llmResult.TraceId ?? string.Empty;

        if (!StockAgentJsonParser.TryParse(rawModelResponse, out var planJson, out var parseError) || planJson is null)
        {
            return await BuildParseFailureResultAsync(
                normalizedSymbol,
                trimmedQuestion,
                provider,
                request.Model,
                session,
                checklist,
                marketContext,
                prompt,
                rawModelResponse,
                llmTraceId,
                parseError ?? "LLM 计划解析失败。",
                cancellationToken);
        }

        var parsedPlan = ParsePlan(planJson.Value);
        var validation = ValidateToolCalls(parsedPlan.ToolCalls, normalizedSymbol, trimmedQuestion, request.AllowExternalSearch);
        var execution = await ExecuteApprovedToolsAsync(normalizedSymbol, trimmedQuestion, request.TaskId, validation.ApprovedCalls, cancellationToken);

        var finalAnswer = BuildFinalAnswer(
            parsedPlan.FinalAnswerDraft,
            llmTraceId,
            execution.ToolResults,
            validation.RejectedCalls,
            execution.StopReason,
            execution.ForcedClose);
        var toolCalls = validation.AllToolCalls;
        var turn = new StockCopilotTurnDto(
            TurnId: $"turn-live-{Guid.NewGuid():N}",
            SessionKey: session.SessionKey,
            Symbol: normalizedSymbol,
            UserQuestion: trimmedQuestion,
            CreatedAt: DateTime.UtcNow,
            Status: finalAnswer.Status,
            PlannerSummary: BuildPlannerSummary(parsedPlan, validation),
            GovernorSummary: BuildGovernorSummary(parsedPlan, validation),
            MarketContext: marketContext,
            PlanSteps: BuildPlanSteps(toolCalls, marketContext, execution.StepStatuses, finalAnswer.Status),
            ToolCalls: toolCalls,
            ToolResults: execution.ToolResults,
            FinalAnswer: finalAnswer,
            FollowUpActions: BuildFollowUpActions(toolCalls, finalAnswer),
            LoopBudget: new StockCopilotLoopBudgetDto(1, MaxPlannedToolCalls, MaxExternalSearchCalls, MaxTotalLatencyMs, MaxPollingSteps),
            LoopExecution: new StockCopilotLoopExecutionDto(
                1,
                execution.ToolExecutions.Count,
                execution.ToolExecutions.Count(item => string.Equals(item.PolicyClass, "external_gated", StringComparison.OrdinalIgnoreCase)),
                execution.TotalLatencyMs,
                Math.Min(MaxPollingSteps, toolCalls.Count + 2),
                finalAnswer.Status,
                execution.StopReason ?? (validation.RejectedCalls.Count > 0 ? "validation_rejections" : "completed"),
                execution.ForcedClose),
            LlmTraceId: llmTraceId);

        var sessionDto = new StockCopilotSessionDto(
            session.SessionKey,
            normalizedSymbol,
            session.Title,
            session.CreatedAt,
            session.UpdatedAt,
            [turn],
            checklist);

        var acceptance = await _acceptanceService.BuildBaselineAsync(
            new StockCopilotAcceptanceBaselineRequestDto(
                normalizedSymbol,
                turn,
                execution.ToolExecutions,
                DefaultReplaySampleTake),
            cancellationToken);

        return new StockCopilotLiveGateResultDto(
            sessionDto,
            acceptance,
            llmTraceId,
            provider,
            request.Model,
            prompt,
            rawModelResponse,
            validation.RejectedCalls);
    }

    private async Task<StockCopilotLiveGateResultDto> BuildParseFailureResultAsync(
        string symbol,
        string question,
        string provider,
        string? model,
        Data.Entities.StockChatSession session,
        StockCopilotRoleContractChecklistDto checklist,
        StockMarketContextDto? marketContext,
        string prompt,
        string rawModelResponse,
        string llmTraceId,
        string parseError,
        CancellationToken cancellationToken)
    {
        var finalAnswer = new StockCopilotFinalAnswerDto(
            Status: "failed",
            Summary: $"live gate 已调用真实 LLM，但返回计划无法解析：{parseError}",
            GroundingMode: "llm_plan_parse_failed",
            ConfidenceScore: 0.05m,
            NeedsToolExecution: false,
            Constraints:
            [
                "本次仅记录真实 LLM-AUDIT 轨迹，不伪造任何工具计划。",
                $"LLM traceId={llmTraceId}",
                "若要继续，需要修正 prompt 或模型输出格式后重新执行。"
            ]);

        var turn = new StockCopilotTurnDto(
            TurnId: $"turn-live-{Guid.NewGuid():N}",
            SessionKey: session.SessionKey,
            Symbol: symbol,
            UserQuestion: question,
            CreatedAt: DateTime.UtcNow,
            Status: "failed",
            PlannerSummary: "LLM live gate 已触发，但未能产出可解析工具计划。",
            GovernorSummary: "governor 未执行任何工具，因为模型输出未通过 JSON 计划解析。",
            MarketContext: marketContext,
            PlanSteps:
            [
                new StockCopilotPlanStepDto(
                    "planner-1",
                    "planner",
                    "生成 live gate 计划",
                    "要求模型返回受控工具计划 JSON。",
                    "failed",
                    Array.Empty<string>(),
                    null)
            ],
            ToolCalls: Array.Empty<StockCopilotToolCallDto>(),
            ToolResults: Array.Empty<StockCopilotToolResultDto>(),
            FinalAnswer: finalAnswer,
            FollowUpActions:
            [
                new StockCopilotFollowUpActionDto(
                    "action-retry-live-gate",
                    "重试 live gate",
                    "retry_live_gate",
                    null,
                    "修正 prompt 或 provider 配置后重新执行 live gate。",
                    true,
                    null)
            ],
            LoopBudget: new StockCopilotLoopBudgetDto(1, MaxPlannedToolCalls, MaxExternalSearchCalls, MaxTotalLatencyMs, MaxPollingSteps),
            LoopExecution: new StockCopilotLoopExecutionDto(0, 0, 0, 0, 1, "failed", "llm_plan_parse_failed", true),
            LlmTraceId: llmTraceId);

        var sessionDto = new StockCopilotSessionDto(
            session.SessionKey,
            symbol,
            session.Title,
            session.CreatedAt,
            session.UpdatedAt,
            [turn],
            checklist);

        var acceptance = await _acceptanceService.BuildBaselineAsync(
            new StockCopilotAcceptanceBaselineRequestDto(symbol, turn, Array.Empty<StockCopilotToolExecutionMetricDto>(), DefaultReplaySampleTake),
            cancellationToken);

        return new StockCopilotLiveGateResultDto(
            sessionDto,
            acceptance,
            llmTraceId,
            provider,
            model,
            prompt,
            rawModelResponse,
            Array.Empty<StockCopilotRejectedToolCallDto>());
    }

    private ValidationResult ValidateToolCalls(
        IReadOnlyList<ParsedToolCall> plannedCalls,
        string symbol,
        string question,
        bool allowExternalSearch)
    {
        var allToolCalls = new List<StockCopilotToolCallDto>();
        var approvedCalls = new List<ApprovedToolCall>();
        var rejectedCalls = new List<StockCopilotRejectedToolCallDto>();
        var seenCalls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var approvedLocalCount = 0;
        var approvedExternalCount = 0;

        for (var index = 0; index < plannedCalls.Count; index++)
        {
            var plannedCall = plannedCalls[index];
            var callId = $"live-call-{index + 1}";

            if (index >= MaxPlannedToolCalls)
            {
                Reject("tool.plan_budget_exceeded", "tool plan 超出最大工具调用预算。", plannedCall.ToolName, "unknown");
                continue;
            }

            if (string.IsNullOrWhiteSpace(plannedCall.RoleId))
            {
                Reject("role.missing", "roleId 不能为空。", plannedCall.ToolName, "unknown");
                continue;
            }

            if (string.IsNullOrWhiteSpace(plannedCall.ToolName))
            {
                Reject(McpErrorCodes.ToolNotRegistered, "toolName 不能为空。", "unknown", "unknown");
                continue;
            }

            StockCopilotRoleContractDto contract;
            try
            {
                contract = _roleContractRegistry.GetRequired(plannedCall.RoleId);
            }
            catch
            {
                Reject("role.contract_not_registered", $"角色 {plannedCall.RoleId} 未注册，不能执行工具计划。", plannedCall.ToolName, "unknown");
                continue;
            }

            McpToolRegistration registration;
            try
            {
                registration = _mcpServiceRegistry.GetRequired(plannedCall.ToolName);
            }
            catch
            {
                Reject(McpErrorCodes.ToolNotRegistered, $"工具 {plannedCall.ToolName} 未注册，不能执行。", plannedCall.ToolName, "unknown");
                continue;
            }

            var normalizedInput = NormalizeInputSummary(plannedCall.InputSummary);
            var dedupeKey = $"{contract.RoleId}|{registration.ToolName}|{normalizedInput}";
            if (!seenCalls.Add(dedupeKey))
            {
                Reject("tool.duplicate", $"重复的工具计划已被拒绝：{registration.ToolName}。", registration.ToolName, registration.PolicyClass);
                continue;
            }

            var authorization = _roleToolPolicyService.AuthorizeRole(contract.RoleId, registration.ToolName);
            if (!authorization.IsAllowed)
            {
                Reject(authorization.ErrorCode, authorization.Reason ?? $"{contract.RoleId} 未被授权调用 {registration.ToolName}。", registration.ToolName, registration.PolicyClass);
                continue;
            }

            if (!contract.AllowsDirectQueryTools)
            {
                Reject(McpErrorCodes.RoleNotAuthorized, contract.Reason ?? $"{contract.RoleId} 不允许直接调用查询工具。", registration.ToolName, registration.PolicyClass);
                continue;
            }

            if (string.Equals(registration.PolicyClass, "external_gated", StringComparison.OrdinalIgnoreCase) && !allowExternalSearch)
            {
                Reject("tool.external_search_not_allowed", "本轮未开启 allowExternalSearch，external_gated 工具被拒绝。", registration.ToolName, registration.PolicyClass);
                continue;
            }

            if (string.Equals(registration.PolicyClass, "external_gated", StringComparison.OrdinalIgnoreCase) && approvedLocalCount == 0)
            {
                Reject("tool.local_first_required", "必须先执行至少一个 local_required 工具，才能进入 external_gated 搜索。", registration.ToolName, registration.PolicyClass);
                continue;
            }

            if (string.Equals(registration.PolicyClass, "external_gated", StringComparison.OrdinalIgnoreCase) && approvedExternalCount >= MaxExternalSearchCalls)
            {
                Reject("tool.external_search_budget_exceeded", "external_gated 搜索预算已耗尽。", registration.ToolName, registration.PolicyClass);
                continue;
            }

            var input = ParseInputSummary(plannedCall.InputSummary);
            if (input.TryGetValue("symbol", out var requestedSymbol)
                && !string.IsNullOrWhiteSpace(requestedSymbol)
                && !string.Equals(StockSymbolNormalizer.Normalize(requestedSymbol), symbol, StringComparison.OrdinalIgnoreCase))
            {
                Reject("tool.symbol_out_of_scope", $"当前 live gate 只允许查询 {symbol}，不能切换到 {requestedSymbol}。", registration.ToolName, registration.PolicyClass);
                continue;
            }

            var toolCall = new StockCopilotToolCallDto(
                CallId: callId,
                StepId: $"tool-{index + 1}",
                ToolName: registration.ToolName,
                PolicyClass: registration.PolicyClass,
                Purpose: string.IsNullOrWhiteSpace(plannedCall.Purpose) ? BuildDefaultPurpose(registration.ToolName) : plannedCall.Purpose.Trim(),
                InputSummary: BuildEffectiveInputSummary(registration.ToolName, plannedCall.InputSummary, symbol, question),
                ApprovalStatus: "approved",
                BlockedReason: null);

            allToolCalls.Add(toolCall);
            approvedCalls.Add(new ApprovedToolCall(callId, contract.RoleId, registration, toolCall));
            if (string.Equals(registration.PolicyClass, "external_gated", StringComparison.OrdinalIgnoreCase))
            {
                approvedExternalCount += 1;
            }
            else
            {
                approvedLocalCount += 1;
            }

            void Reject(string? errorCode, string reason, string toolName, string policyClass)
            {
                var safeToolName = string.IsNullOrWhiteSpace(toolName) ? "unknown" : toolName.Trim();
                var safePolicy = string.IsNullOrWhiteSpace(policyClass) ? "unknown" : policyClass.Trim();
                allToolCalls.Add(new StockCopilotToolCallDto(
                    CallId: callId,
                    StepId: $"tool-{index + 1}",
                    ToolName: safeToolName,
                    PolicyClass: safePolicy,
                    Purpose: string.IsNullOrWhiteSpace(plannedCall.Purpose) ? BuildDefaultPurpose(safeToolName) : plannedCall.Purpose.Trim(),
                    InputSummary: string.IsNullOrWhiteSpace(plannedCall.InputSummary) ? $"symbol={symbol}" : plannedCall.InputSummary.Trim(),
                    ApprovalStatus: "blocked",
                    BlockedReason: reason));
                rejectedCalls.Add(new StockCopilotRejectedToolCallDto(
                    CallId: callId,
                    RoleId: string.IsNullOrWhiteSpace(plannedCall.RoleId) ? "unknown" : plannedCall.RoleId.Trim(),
                    ToolName: safeToolName,
                    PolicyClass: safePolicy,
                    ApprovalStatus: "blocked",
                    Reason: reason,
                    ErrorCode: errorCode));
            }
        }

        return new ValidationResult(allToolCalls, approvedCalls, rejectedCalls);
    }

    private async Task<ExecutionResult> ExecuteApprovedToolsAsync(
        string symbol,
        string question,
        string? taskId,
        IReadOnlyList<ApprovedToolCall> approvedCalls,
        CancellationToken cancellationToken)
    {
        var toolResults = new List<StockCopilotToolResultDto>();
        var toolExecutions = new List<StockCopilotToolExecutionMetricDto>();
        var stepStatuses = approvedCalls.ToDictionary(item => item.ToolCall.StepId, _ => "approved", StringComparer.OrdinalIgnoreCase);
        var totalLatencyMs = 0L;
        string? stopReason = null;
        var forcedClose = false;

        foreach (var approvedCall in approvedCalls)
        {
            stepStatuses[approvedCall.ToolCall.StepId] = "calling_tools";
            var outcome = await ExecuteApprovedToolAsync(symbol, question, taskId, approvedCall, cancellationToken);
            toolResults.Add(outcome.ToolResult);
            toolExecutions.Add(outcome.ExecutionMetric);
            stepStatuses[approvedCall.ToolCall.StepId] = string.Equals(outcome.ToolResult.Status, "completed", StringComparison.OrdinalIgnoreCase)
                ? "completed"
                : "failed";
            totalLatencyMs += Math.Max(0, outcome.ExecutionMetric.LatencyMs);

            if (totalLatencyMs >= MaxTotalLatencyMs)
            {
                stopReason = "time_budget_reached";
                forcedClose = true;
                break;
            }
        }

        return new ExecutionResult(toolResults, toolExecutions, stepStatuses, totalLatencyMs, stopReason, forcedClose);
    }

    private async Task<ToolExecutionOutcome> ExecuteApprovedToolAsync(
        string symbol,
        string question,
        string? taskId,
        ApprovedToolCall approvedCall,
        CancellationToken cancellationToken)
    {
        var input = ParseInputSummary(approvedCall.ToolCall.InputSummary);
        var toolTaskId = string.IsNullOrWhiteSpace(taskId)
            ? approvedCall.CallId
            : $"{taskId.Trim()}-{approvedCall.CallId}";

        try
        {
            return approvedCall.Registration.ToolName switch
            {
                StockMcpToolNames.CompanyOverview => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetCompanyOverviewAsync(symbol, toolTaskId, cancellationToken),
                    envelope => $"公司概况：{envelope.Data.Name}，主营={envelope.Data.MainBusiness ?? "缺失"}，经营范围={envelope.Data.BusinessScope ?? "缺失"}。"),
                StockMcpToolNames.Product => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetProductAsync(symbol, toolTaskId, cancellationToken),
                    envelope => $"产品事实 {envelope.Data.FactCount} 条，行业={envelope.Data.Industry ?? "缺失"}，地区={envelope.Data.Region ?? "缺失"}。"),
                StockMcpToolNames.Fundamentals => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetFundamentalsAsync(symbol, toolTaskId, cancellationToken),
                    envelope => $"基本面事实 {envelope.Data.FactCount} 条。"),
                StockMcpToolNames.Shareholder => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetShareholderAsync(symbol, toolTaskId, cancellationToken),
                    envelope => $"股东事实 {envelope.Data.FactCount} 条，股东户数={envelope.Data.ShareholderCount?.ToString() ?? "缺失"}。"),
                StockMcpToolNames.MarketContext => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetMarketContextAsync(symbol, toolTaskId, cancellationToken),
                    envelope => $"市场阶段={envelope.Data.StageLabel ?? "未知"}，主线={envelope.Data.MainlineSectorName ?? "未知"}。"),
                StockMcpToolNames.SocialSentiment => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetSocialSentimentAsync(symbol, toolTaskId, cancellationToken),
                    envelope => $"情绪状态={envelope.Data.Status}，证据数={envelope.Data.EvidenceCount}。"),
                StockMcpToolNames.Kline => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetKlineAsync(
                        symbol,
                        input.GetValueOrDefault("interval", "day"),
                        ParseInt(input, "count", 60),
                        input.GetValueOrDefault("source"),
                        toolTaskId,
                        cancellationToken),
                    envelope => $"K 线窗口={envelope.Data.WindowSize}，趋势={envelope.Data.TrendState}，5D={envelope.Data.Return5dPercent:0.##}%。"),
                StockMcpToolNames.Minute => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetMinuteAsync(symbol, input.GetValueOrDefault("source"), toolTaskId, cancellationToken),
                    envelope => $"分时 session={envelope.Data.SessionPhase}，点位={envelope.Data.WindowSize}。"),
                StockMcpToolNames.Strategy => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetStrategyAsync(
                        symbol,
                        input.GetValueOrDefault("interval", "day"),
                        ParseInt(input, "count", 60),
                        input.GetValueOrDefault("source"),
                        ParseStrategies(input.GetValueOrDefault("strategies")),
                        toolTaskId,
                        cancellationToken),
                    envelope => $"策略信号 {envelope.Data.Signals.Count} 条。"),
                StockMcpToolNames.News => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.GetNewsAsync(symbol, input.GetValueOrDefault("level", "stock"), toolTaskId, cancellationToken),
                    envelope => $"本地新闻 {envelope.Data.ItemCount} 条，最新时间={envelope.Data.LatestPublishedAt:yyyy-MM-dd HH:mm:ss}。"),
                StockMcpToolNames.Search => MapOutcome(
                    approvedCall,
                    await _mcpToolGateway.SearchAsync(input.GetValueOrDefault("query", question), true, toolTaskId, cancellationToken),
                    envelope => $"外部搜索 provider={envelope.Data.Provider}，结果 {envelope.Data.ResultCount} 条。"),
                _ => BuildFailedOutcome(approvedCall, $"暂不支持工具 {approvedCall.Registration.ToolName} 的 live gate 执行。")
            };
        }
        catch (Exception ex)
        {
            return BuildFailedOutcome(approvedCall, ex.Message);
        }
    }

    private static ToolExecutionOutcome MapOutcome<T>(
        ApprovedToolCall approvedCall,
        StockCopilotMcpEnvelopeDto<T> envelope,
        Func<StockCopilotMcpEnvelopeDto<T>, string> summaryBuilder)
    {
        var summary = summaryBuilder(envelope);
        var toolResult = new StockCopilotToolResultDto(
            approvedCall.CallId,
            approvedCall.Registration.ToolName,
            "completed",
            envelope.TraceId,
            envelope.Evidence.Count,
            envelope.Features.Count,
            envelope.Warnings,
            envelope.DegradedFlags,
            envelope.Evidence,
            summary);

        var executionMetric = new StockCopilotToolExecutionMetricDto(
            approvedCall.CallId,
            approvedCall.Registration.ToolName,
            approvedCall.Registration.PolicyClass,
            envelope.LatencyMs,
            envelope.Evidence.Count,
            envelope.Features.Count,
            envelope.Warnings,
            envelope.DegradedFlags);

        return new ToolExecutionOutcome(toolResult, executionMetric);
    }

    private static ToolExecutionOutcome BuildFailedOutcome(ApprovedToolCall approvedCall, string message)
    {
        var warnings = new[] { message };
        var toolResult = new StockCopilotToolResultDto(
            approvedCall.CallId,
            approvedCall.Registration.ToolName,
            "failed",
            null,
            0,
            0,
            warnings,
            Array.Empty<string>(),
            Array.Empty<StockCopilotMcpEvidenceDto>(),
            $"{approvedCall.Registration.ToolName} 执行失败：{message}");
        var executionMetric = new StockCopilotToolExecutionMetricDto(
            approvedCall.CallId,
            approvedCall.Registration.ToolName,
            approvedCall.Registration.PolicyClass,
            0,
            0,
            0,
            warnings,
            Array.Empty<string>());
        return new ToolExecutionOutcome(toolResult, executionMetric);
    }

    private string BuildPrompt(
        string symbol,
        string question,
        bool allowExternalSearch,
        StockMarketContextDto? marketContext,
        StockCopilotRoleContractChecklistDto checklist)
    {
        var builder = new StringBuilder();
        builder.AppendLine("你是 Stock Copilot 的 live LLM gate planner。你的任务不是直接给投资建议，而是为一次受控 MCP 验收生成工具计划 JSON。");
        builder.AppendLine("硬性规则：");
        builder.AppendLine("1. 只能使用下方角色 contract 和 tool registry 中真实存在的 roleId 与 toolName。");
        builder.AppendLine("2. local_required / local_preferred 工具必须优先；external_gated 只能在 allowExternalSearch=true 且本地链路先执行后再考虑。");
        builder.AppendLine("3. disabled 或 blocked 的角色不得规划任何查询工具。");
        builder.AppendLine("4. 触发 stop rule 时必须停止，不得通过改写角色、改写 toolName 或伪造 fallback 绕过。");
        builder.AppendLine("5. 不允许切换 symbol，不允许生成未注册工具，不允许越权调用。");
        builder.AppendLine($"6. 最多输出 {MaxPlannedToolCalls} 个 toolCalls，其中最多 {MaxExternalSearchCalls} 个 StockSearchMcp。");
        builder.AppendLine("7. 只返回一个 JSON object，不要输出解释、markdown、代码块或思考过程。");
        builder.AppendLine();
        builder.AppendLine("输出 JSON schema：");
        builder.AppendLine("{");
        builder.AppendLine("  \"plannerSummary\": \"string\",");
        builder.AppendLine("  \"governorSummary\": \"string\",");
        builder.AppendLine("  \"finalAnswerDraft\": \"string\",");
        builder.AppendLine("  \"toolCalls\": [");
        builder.AppendLine("    {");
        builder.AppendLine("      \"roleId\": \"string\",");
        builder.AppendLine("      \"toolName\": \"string\",");
        builder.AppendLine("      \"purpose\": \"string\",");
        builder.AppendLine("      \"inputSummary\": \"key=value; key=value\"");
        builder.AppendLine("    }");
        builder.AppendLine("  ]");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("当前请求：");
        builder.AppendLine($"- symbol={symbol}");
        builder.AppendLine($"- question={question}");
        builder.AppendLine($"- allowExternalSearch={allowExternalSearch}");
        if (marketContext is not null)
        {
            builder.AppendLine($"- marketStage={marketContext.StageLabel ?? "unknown"}");
            builder.AppendLine($"- mainlineSector={marketContext.MainlineSectorName ?? "unknown"}");
            builder.AppendLine($"- stockSector={marketContext.StockSectorName ?? "unknown"}");
            builder.AppendLine($"- counterTrendWarning={marketContext.CounterTrendWarning}");
        }

        builder.AppendLine();
        builder.AppendLine("tool registry：");
        foreach (var registration in _mcpServiceRegistry.List())
        {
            builder.AppendLine($"- toolName={registration.ToolName}; policyClass={registration.PolicyClass}");
        }

        builder.AppendLine();
        builder.AppendLine("角色 contract：");
        foreach (var role in checklist.Roles)
        {
            var preferredTools = role.PreferredMcpSequence.Count == 0
                ? "none"
                : string.Join(", ", role.PreferredMcpSequence);
            builder.AppendLine($"- roleId={role.RoleId}; roleClass={role.RoleClass}; toolAccessMode={role.ToolAccessMode}; allowsDirectQueryTools={role.AllowsDirectQueryTools}; minimumEvidenceCount={role.MinimumEvidenceCount}; preferredMcpSequence=[{preferredTools}]; fallbackRule={role.FallbackRule}; stopRule={role.StopRule}; reason={role.Reason ?? "none"}");
        }

        builder.AppendLine();
        builder.AppendLine("你生成的 finalAnswerDraft 必须明确说明：这是 live gate 的计划草案，真正可采信的事实以后端 tool results 为准。不要在 finalAnswerDraft 中编造尚未执行的工具结论。");
        return builder.ToString().Trim();
    }

    private static ParsedPlan ParsePlan(JsonElement root)
    {
        var plannerSummary = ReadString(root, "plannerSummary") ?? "LLM 未提供 plannerSummary。";
        var governorSummary = ReadString(root, "governorSummary") ?? "LLM 未提供 governorSummary。";
        var finalAnswerDraft = ReadString(root, "finalAnswerDraft") ?? "LLM 未提供 finalAnswerDraft。";
        var toolCalls = new List<ParsedToolCall>();

        if (root.TryGetProperty("toolCalls", out var toolCallsNode) && toolCallsNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in toolCallsNode.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                toolCalls.Add(new ParsedToolCall(
                    ReadString(item, "roleId") ?? string.Empty,
                    ReadString(item, "toolName") ?? string.Empty,
                    ReadString(item, "purpose") ?? string.Empty,
                    ReadString(item, "inputSummary") ?? string.Empty));
            }
        }

        return new ParsedPlan(plannerSummary.Trim(), governorSummary.Trim(), SanitizeDraftText(finalAnswerDraft), toolCalls);
    }

    private static string BuildPlannerSummary(ParsedPlan parsedPlan, ValidationResult validation)
    {
        var approvedCount = validation.ApprovedCalls.Count;
        var rejectedCount = validation.RejectedCalls.Count;
        return $"{parsedPlan.PlannerSummary} 当前 live gate 计划中，已批准 {approvedCount} 个工具，拦截 {rejectedCount} 个工具。".Trim();
    }

    private static string BuildGovernorSummary(ParsedPlan parsedPlan, ValidationResult validation)
    {
        if (validation.RejectedCalls.Count == 0)
        {
            return $"{parsedPlan.GovernorSummary} governor 未发现越权工具。".Trim();
        }

        var rejectionSummary = string.Join("；", validation.RejectedCalls.Select(item => $"{item.ToolName}:{item.Reason}"));
        return $"{parsedPlan.GovernorSummary} governor 已拦截 {validation.RejectedCalls.Count} 个工具计划：{rejectionSummary}".Trim();
    }

    private static IReadOnlyList<StockCopilotPlanStepDto> BuildPlanSteps(
        IReadOnlyList<StockCopilotToolCallDto> toolCalls,
        StockMarketContextDto? marketContext,
        IReadOnlyDictionary<string, string> stepStatuses,
        string finalStatus)
    {
        var steps = new List<StockCopilotPlanStepDto>
        {
            new(
                "planner-1",
                "planner",
                "生成 live gate 工具计划",
                marketContext is null
                    ? "基于问题生成受控工具计划，并让 governor 在后端强校验。"
                    : $"基于问题与当前市场阶段={marketContext.StageLabel} 生成受控工具计划，并让 governor 在后端强校验。",
                "completed",
                Array.Empty<string>(),
                null)
        };

        steps.AddRange(toolCalls.Select(call => new StockCopilotPlanStepDto(
            call.StepId,
            string.Equals(call.ApprovalStatus, "approved", StringComparison.OrdinalIgnoreCase) ? "planner" : "governor",
            BuildToolStepTitle(call.ToolName),
            $"{call.Purpose}。输入：{call.InputSummary}",
            stepStatuses.GetValueOrDefault(call.StepId, call.ApprovalStatus),
            ["planner-1"],
            call.ToolName)));

        steps.Add(new StockCopilotPlanStepDto(
            "commander-1",
            "commander",
            "收口 live gate 回执",
            "只根据 LLM 计划、后端校验结果和已执行 tool results 输出验收结论。",
            finalStatus,
            steps.Where(item => item.ToolName is not null).Select(item => item.StepId).ToArray(),
            null));

        return steps;
    }

    private static StockCopilotFinalAnswerDto BuildFinalAnswer(
        string finalAnswerDraft,
        string llmTraceId,
        IReadOnlyList<StockCopilotToolResultDto> toolResults,
        IReadOnlyList<StockCopilotRejectedToolCallDto> rejectedCalls,
        string? stopReason,
        bool forcedClose)
    {
        var completedCount = toolResults.Count(item => string.Equals(item.Status, "completed", StringComparison.OrdinalIgnoreCase));
        var failedCount = toolResults.Count(item => string.Equals(item.Status, "failed", StringComparison.OrdinalIgnoreCase));
        var totalEvidenceCount = toolResults.Sum(item => item.EvidenceCount);
        var warningCount = toolResults.Sum(item => item.Warnings.Count);
        var status = completedCount > 0 && failedCount == 0 && rejectedCalls.Count == 0 && !forcedClose
            ? "done"
            : completedCount > 0 || rejectedCalls.Count > 0
                ? "done_with_gaps"
                : "failed";
        var confidence = Math.Max(0.08m, Math.Min(0.9m, decimal.Round(0.22m + completedCount * 0.09m + totalEvidenceCount * 0.03m - failedCount * 0.08m - rejectedCalls.Count * 0.05m - warningCount * 0.02m, 2)));
        var toolReceipt = toolResults.Count == 0
            ? "本轮没有执行任何被授权的工具。"
            : "工具回执：" + string.Join("；", toolResults.Select(item => $"{item.ToolName}[{item.Status}] traceId={item.TraceId ?? "n/a"} {item.Summary}"));
        var rejectionReceipt = rejectedCalls.Count == 0
            ? string.Empty
            : " 已拦截工具：" + string.Join("；", rejectedCalls.Select(item => $"{item.RoleId}/{item.ToolName} -> {item.Reason}"));
        var summary = $"{finalAnswerDraft}\n\n{toolReceipt}{rejectionReceipt}".Trim();

        var constraints = new List<string>
        {
            "这是 live gate 验收结论，不是多轮产品化对话结果。",
            "真正可采信事实以后端执行成功的 tool result、traceId 和 evidence 为准。",
            $"LLM traceId={llmTraceId}"
        };

        if (rejectedCalls.Count > 0)
        {
            constraints.Add($"本轮拦截了 {rejectedCalls.Count} 个不合规工具计划。\n任何被拒绝的工具都没有进入执行阶段。".Replace("\\n", "\n"));
        }

        if (!string.IsNullOrWhiteSpace(stopReason))
        {
            constraints.Add($"本轮提前收口原因：{stopReason}。" );
        }

        return new StockCopilotFinalAnswerDto(
            Status: status,
            Summary: summary,
            GroundingMode: "llm_plan_with_tool_receipts",
            ConfidenceScore: confidence,
            NeedsToolExecution: false,
            Constraints: constraints);
    }

    private static IReadOnlyList<StockCopilotFollowUpActionDto> BuildFollowUpActions(
        IReadOnlyList<StockCopilotToolCallDto> toolCalls,
        StockCopilotFinalAnswerDto finalAnswer)
    {
        var actions = new List<StockCopilotFollowUpActionDto>();
        foreach (var toolName in toolCalls.Select(item => item.ToolName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            actions.Add(new StockCopilotFollowUpActionDto(
                $"action-{toolName}",
                $"查看 {toolName} 回执",
                "inspect_tool_receipt",
                toolName,
                $"核对 {toolName} 的 traceId、warnings 与 evidence。",
                true,
                null));
        }

        actions.Add(new StockCopilotFollowUpActionDto(
            "action-next-round",
            "发起下一轮修正",
            "retry_live_gate",
            null,
            "如果需要更严格的工具计划，可以在修正 prompt 或授权后重新运行 live gate。",
            !string.Equals(finalAnswer.Status, "done", StringComparison.OrdinalIgnoreCase),
            string.Equals(finalAnswer.Status, "done", StringComparison.OrdinalIgnoreCase) ? "当前 live gate 已完成，可直接进入人工复核。" : null));

        return actions;
    }

    private static string BuildToolStepTitle(string toolName)
    {
        return toolName switch
        {
            StockMcpToolNames.CompanyOverview => "读取公司概况",
            StockMcpToolNames.Product => "读取产品业务事实",
            StockMcpToolNames.Fundamentals => "读取基本面事实",
            StockMcpToolNames.Shareholder => "读取股东结构事实",
            StockMcpToolNames.MarketContext => "读取市场环境",
            StockMcpToolNames.SocialSentiment => "读取情绪代理",
            StockMcpToolNames.Kline => "读取 K 线结构",
            StockMcpToolNames.Minute => "读取分时结构",
            StockMcpToolNames.Strategy => "读取策略信号",
            StockMcpToolNames.News => "读取本地新闻",
            StockMcpToolNames.Search => "执行外部搜索兜底",
            _ => $"执行 {toolName}"
        };
    }

    private static string BuildDefaultPurpose(string toolName)
    {
        return toolName switch
        {
            StockMcpToolNames.CompanyOverview => "确认公司基础画像",
            StockMcpToolNames.Product => "确认主营和产品业务事实",
            StockMcpToolNames.Fundamentals => "确认基本面财务事实",
            StockMcpToolNames.Shareholder => "确认股东结构",
            StockMcpToolNames.MarketContext => "确认市场阶段和主线",
            StockMcpToolNames.SocialSentiment => "确认情绪代理和证据缺口",
            StockMcpToolNames.Kline => "确认 K 线趋势结构",
            StockMcpToolNames.Minute => "确认盘中结构",
            StockMcpToolNames.Strategy => "确认确定性策略信号",
            StockMcpToolNames.News => "确认本地新闻证据",
            StockMcpToolNames.Search => "在本地证据不足时做外部兜底",
            _ => "执行工具查询"
        };
    }

    private static string BuildEffectiveInputSummary(string toolName, string inputSummary, string symbol, string question)
    {
        var input = ParseInputSummary(inputSummary);
        input["symbol"] = symbol;
        if (string.Equals(toolName, StockMcpToolNames.Search, StringComparison.OrdinalIgnoreCase))
        {
            input["query"] = input.GetValueOrDefault("query", question);
        }

        return string.Join("; ", input.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select(item => $"{item.Key}={item.Value}"));
    }

    private static Dictionary<string, string> ParseInputSummary(string inputSummary)
    {
        return string.IsNullOrWhiteSpace(inputSummary)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : inputSummary
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(item => item.Split('=', 2))
                .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
                .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeInputSummary(string inputSummary)
    {
        var input = ParseInputSummary(inputSummary);
        return string.Join(";", input.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select(item => $"{item.Key}={item.Value}"));
    }

    private static int ParseInt(IReadOnlyDictionary<string, string> input, string key, int fallback)
    {
        return input.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : fallback;
    }

    private static IReadOnlyList<string>? ParseStrategies(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var items = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return items.Length == 0 ? null : items;
    }

    private static string BuildSessionTitle(string question)
    {
        var compact = question.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return compact.Length <= 24 ? compact : compact[..24];
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var node) && node.ValueKind == JsonValueKind.String
            ? node.GetString()
            : null;
    }

    private static string SanitizeDraftText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "LLM 未提供 finalAnswerDraft。";
        }

        var cleaned = value
            .Replace("**My Thought Process**", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("My Thought Process", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("思考过程", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return string.IsNullOrWhiteSpace(cleaned)
            ? "LLM 未提供可用的 finalAnswerDraft。"
            : cleaned;
    }

    private sealed record ParsedPlan(
        string PlannerSummary,
        string GovernorSummary,
        string FinalAnswerDraft,
        IReadOnlyList<ParsedToolCall> ToolCalls);

    private sealed record ParsedToolCall(
        string RoleId,
        string ToolName,
        string Purpose,
        string InputSummary);

    private sealed record ApprovedToolCall(
        string CallId,
        string RoleId,
        McpToolRegistration Registration,
        StockCopilotToolCallDto ToolCall);

    private sealed record ValidationResult(
        IReadOnlyList<StockCopilotToolCallDto> AllToolCalls,
        IReadOnlyList<ApprovedToolCall> ApprovedCalls,
        IReadOnlyList<StockCopilotRejectedToolCallDto> RejectedCalls);

    private sealed record ToolExecutionOutcome(
        StockCopilotToolResultDto ToolResult,
        StockCopilotToolExecutionMetricDto ExecutionMetric);

    private sealed record ExecutionResult(
        IReadOnlyList<StockCopilotToolResultDto> ToolResults,
        IReadOnlyList<StockCopilotToolExecutionMetricDto> ToolExecutions,
        IReadOnlyDictionary<string, string> StepStatuses,
        long TotalLatencyMs,
        string? StopReason,
        bool ForcedClose);
}