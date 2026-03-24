using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IStockCopilotAcceptanceService
{
    Task<StockCopilotAcceptanceBaselineDto> BuildBaselineAsync(StockCopilotAcceptanceBaselineRequestDto request, CancellationToken cancellationToken = default);
}

public sealed class StockCopilotAcceptanceService : IStockCopilotAcceptanceService
{
    private readonly IStockAgentReplayCalibrationService _replayCalibrationService;

    public StockCopilotAcceptanceService(IStockAgentReplayCalibrationService replayCalibrationService)
    {
        _replayCalibrationService = replayCalibrationService;
    }

    public async Task<StockCopilotAcceptanceBaselineDto> BuildBaselineAsync(StockCopilotAcceptanceBaselineRequestDto request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Symbol);
        ArgumentNullException.ThrowIfNull(request.Turn);

        var normalizedSymbol = StockSymbolNormalizer.Normalize(request.Symbol);
        var turn = request.Turn;
        var toolCalls = turn.ToolCalls ?? Array.Empty<StockCopilotToolCallDto>();
        var toolResults = turn.ToolResults ?? Array.Empty<StockCopilotToolResultDto>();
        var actions = turn.FollowUpActions ?? Array.Empty<StockCopilotFollowUpActionDto>();
        var executions = request.ToolExecutions ?? Array.Empty<StockCopilotToolExecutionMetricDto>();

        var approvedCalls = toolCalls
            .Where(item => string.Equals(item.ApprovalStatus, "approved", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var executedCallIds = executions
            .Select(item => item.CallId)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (executedCallIds.Count == 0)
        {
            foreach (var result in toolResults)
            {
                if (!string.IsNullOrWhiteSpace(result.CallId))
                {
                    executedCallIds.Add(result.CallId);
                }
            }
        }

        var executedApprovedCount = approvedCalls.Count(item => executedCallIds.Contains(item.CallId));
        var localExecutions = executions.Count(item => string.Equals(item.PolicyClass, "local_required", StringComparison.OrdinalIgnoreCase));
        var externalExecutions = executions.Count(item => string.Equals(item.PolicyClass, "external_gated", StringComparison.OrdinalIgnoreCase));
        var evidenceCoveredCount = executions.Count(item => item.EvidenceCount > 0);
        var traceableCount = toolResults.Count(item =>
            executedCallIds.Contains(item.CallId)
            && (!string.IsNullOrWhiteSpace(item.TraceId) || item.EvidenceCount > 0));
        var enabledActionCount = actions.Count(item => item.Enabled);
        var averageLatencyMs = executions.Count == 0 ? 0m : Math.Round(executions.Average(item => (decimal)Math.Max(item.LatencyMs, 0)), 2);
        var warningCount = executions.Sum(item => item.Warnings?.Count ?? 0);
        var degradedFlagCount = executions.Sum(item => item.DegradedFlags?.Count ?? 0);

        var toolEfficiencyRate = Percentage(executedApprovedCount, approvedCalls.Length);
        var evidenceCoverageRate = Percentage(evidenceCoveredCount, executions.Count);
        var localFirstHitRate = Percentage(localExecutions, executions.Count);
        var externalSearchTriggerRate = Percentage(externalExecutions, executions.Count);
        var finalAnswerTraceabilityRate = Percentage(traceableCount, executions.Count);
        var actionQualityRate = Percentage(enabledActionCount, actions.Count);
        var latencyScore = GetLatencyScore(averageLatencyMs);

        var metrics = new[]
        {
            BuildMetric(
                key: "tool_efficiency",
                label: "工具效率",
                value: toolEfficiencyRate,
                unit: "%",
                status: RateStatus(toolEfficiencyRate),
                description: "已执行工具数占已批准工具数的比例。"),
            BuildMetric(
                key: "evidence_coverage",
                label: "证据覆盖率",
                value: evidenceCoverageRate,
                unit: "%",
                status: RateStatus(evidenceCoverageRate),
                description: "已执行工具中，实际返回 evidence 的占比。"),
            BuildMetric(
                key: "local_first_hit",
                label: "Local-First 命中率",
                value: localFirstHitRate,
                unit: "%",
                status: RateStatus(localFirstHitRate),
                description: "本轮执行工具中，Local-First 工具的占比。"),
            BuildMetric(
                key: "external_search_trigger",
                label: "外部搜索触发率",
                value: externalSearchTriggerRate,
                unit: "%",
                status: RateStatus(externalSearchTriggerRate, higherIsBetter: false),
                description: "本轮执行工具中，external-gated 搜索的占比。"),
            BuildMetric(
                key: "final_answer_traceability",
                label: "最终回答可追溯度",
                value: finalAnswerTraceabilityRate,
                unit: "%",
                status: RateStatus(finalAnswerTraceabilityRate),
                description: "已执行工具中，具备 traceId 或 evidence 的结果占比。"),
            BuildMetric(
                key: "action_quality",
                label: "动作卡就绪度",
                value: actionQualityRate,
                unit: "%",
                status: RateStatus(actionQualityRate),
                description: "当前动作卡中，已可执行动作的占比。"),
            BuildMetric(
                key: "tool_latency",
                label: "工具延迟得分",
                value: latencyScore,
                unit: "%",
                status: RateStatus(latencyScore),
                description: "根据平均工具延迟换算出的体验得分。")
        };

        var replayBaseline = await _replayCalibrationService.BuildBaselineAsync(normalizedSymbol, request.ReplaySampleTake <= 0 ? 40 : request.ReplaySampleTake, cancellationToken);

        var overallScore = ClampScore(Math.Round(
            metrics.Average(item => item.Value)
            - Math.Min(18m, warningCount * 2m + degradedFlagCount * 4m),
            2));

        var highlights = BuildHighlights(
            approvedCalls.Length,
            executedApprovedCount,
            evidenceCoverageRate,
            externalSearchTriggerRate,
            replayBaseline,
            overallScore,
            warningCount,
            degradedFlagCount);

        return new StockCopilotAcceptanceBaselineDto(
            Symbol: normalizedSymbol,
            SessionKey: turn.SessionKey,
            TurnId: turn.TurnId,
            GeneratedAt: DateTime.UtcNow,
            OverallScore: overallScore,
            ApprovedToolCallCount: approvedCalls.Length,
            ExecutedToolCallCount: executions.Count,
            AverageLatencyMs: averageLatencyMs,
            WarningCount: warningCount,
            DegradedFlagCount: degradedFlagCount,
            Highlights: highlights,
            Metrics: metrics,
            ReplayBaseline: replayBaseline);
    }

    private static StockCopilotAcceptanceMetricDto BuildMetric(string key, string label, decimal value, string unit, string status, string description)
    {
        return new StockCopilotAcceptanceMetricDto(key, label, Math.Round(value, 2), unit, status, description);
    }

    private static decimal Percentage(int numerator, int denominator)
    {
        if (denominator <= 0)
        {
            return 0m;
        }

        return Math.Round(numerator * 100m / denominator, 2);
    }

    private static decimal ClampScore(decimal value)
    {
        return Math.Max(0m, Math.Min(100m, value));
    }

    private static decimal GetLatencyScore(decimal averageLatencyMs)
    {
        if (averageLatencyMs <= 0m)
        {
            return 0m;
        }

        if (averageLatencyMs <= 800m)
        {
            return 100m;
        }

        if (averageLatencyMs <= 1500m)
        {
            return 82m;
        }

        if (averageLatencyMs <= 3000m)
        {
            return 64m;
        }

        return 40m;
    }

    private static string RateStatus(decimal value, bool higherIsBetter = true)
    {
        if (higherIsBetter)
        {
            if (value >= 75m)
            {
                return "good";
            }

            if (value >= 45m)
            {
                return "watch";
            }

            return "risk";
        }

        if (value <= 15m)
        {
            return "good";
        }

        if (value <= 40m)
        {
            return "watch";
        }

        return "risk";
    }

    private static IReadOnlyList<string> BuildHighlights(
        int approvedToolCount,
        int executedApprovedCount,
        decimal evidenceCoverageRate,
        decimal externalSearchTriggerRate,
        StockAgentReplayBaselineDto replayBaseline,
        decimal overallScore,
        int warningCount,
        int degradedFlagCount)
    {
        var highlights = new List<string>();

        if (approvedToolCount > 0 && executedApprovedCount == approvedToolCount)
        {
            highlights.Add("本轮已执行全部已批准工具。\n");
        }
        else if (approvedToolCount > 0)
        {
            highlights.Add($"本轮已执行 {executedApprovedCount}/{approvedToolCount} 张已批准工具卡。\n");
        }

        if (evidenceCoverageRate >= 60m)
        {
            highlights.Add("已执行工具具备较高证据覆盖，可支撑 grounded final answer。\n");
        }

        if (externalSearchTriggerRate <= 0m)
        {
            highlights.Add("本轮保持 Local-First，没有触发外部搜索。\n");
        }

        if (replayBaseline.SampleCount > 0)
        {
            highlights.Add($"Replay 基线已有 {replayBaseline.SampleCount} 条样本，traceableEvidenceRate={replayBaseline.TraceableEvidenceRate:0.##}%。\n");
        }

        if (warningCount > 0 || degradedFlagCount > 0)
        {
            highlights.Add($"当前还有 {warningCount} 条 warning、{degradedFlagCount} 条 degraded flag 需要收口。\n");
        }

        highlights.Add($"当前 Copilot 验收总分为 {overallScore:0.##}/100。\n");
        return highlights;
    }
}