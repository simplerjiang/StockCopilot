using System.Text.Json;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

internal sealed record StockAgentHistoryValidationResult(
    bool HasCommander,
    bool CommanderSucceeded,
    bool CommanderHasData,
    bool IsCommanderComplete,
    IReadOnlyList<string> MissingAgents,
    IReadOnlyList<string> FailedAgents,
    string? BlockedReason);

internal static class StockAgentHistoryValidation
{
    private static readonly string[] RequiredAgentIds =
    [
        "stock_news",
        "sector_news",
        "financial_analysis",
        "trend_analysis",
        "commander"
    ];

    private static readonly IReadOnlyDictionary<string, string> AgentLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["stock_news"] = "个股资讯Agent",
        ["sector_news"] = "板块资讯Agent",
        ["financial_analysis"] = "个股分析Agent",
        ["trend_analysis"] = "走势分析Agent",
        ["commander"] = "指挥Agent"
    };

    public static StockAgentHistoryValidationResult Validate(JsonElement root)
    {
        if (!TryGetAgents(root, out var agents))
        {
            return new StockAgentHistoryValidationResult(
                false,
                false,
                false,
                false,
                RequiredAgentIds,
                Array.Empty<string>(),
                "历史结果缺少 agents 数组。");
        }

        var agentMap = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var agent in agents.EnumerateArray())
        {
            if (!TryGetAgentId(agent, out var agentId))
            {
                continue;
            }

            agentMap[agentId] = agent;
        }

        var missingAgents = RequiredAgentIds
            .Where(agentId => !agentMap.ContainsKey(agentId))
            .ToArray();

        var failedAgents = agentMap
            .Where(pair => RequiredAgentIds.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            .Where(pair => HasExplicitFailure(pair.Value))
            .Select(pair => pair.Key)
            .ToArray();

        var hasCommander = agentMap.TryGetValue("commander", out var commanderAgent);
        var commanderSucceeded = hasCommander && !HasExplicitFailure(commanderAgent);
        var commanderHasData = hasCommander
            && TryGetPropertyIgnoreCase(commanderAgent, "data", out var commanderData)
            && commanderData.ValueKind == JsonValueKind.Object;

        var isCommanderComplete = missingAgents.Length == 0
            && failedAgents.Length == 0
            && hasCommander
            && commanderSucceeded
            && commanderHasData;

        return new StockAgentHistoryValidationResult(
            hasCommander,
            commanderSucceeded,
            commanderHasData,
            isCommanderComplete,
            missingAgents,
            failedAgents,
            BuildBlockedReason(hasCommander, commanderSucceeded, commanderHasData, missingAgents, failedAgents));
    }

    private static bool TryGetAgents(JsonElement root, out JsonElement agents)
    {
        agents = default;
        if (!TryGetPropertyIgnoreCase(root, "agents", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        agents = value;
        return true;
    }

    private static bool TryGetAgentId(JsonElement agent, out string agentId)
    {
        agentId = string.Empty;
        if (!TryGetPropertyIgnoreCase(agent, "agentId", out var value) || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        agentId = value.GetString()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(agentId);
    }

    private static bool HasExplicitFailure(JsonElement agent)
    {
        if (!TryGetPropertyIgnoreCase(agent, "success", out var success))
        {
            return false;
        }

        return success.ValueKind == JsonValueKind.False;
    }

    private static string BuildBlockedReason(
        bool hasCommander,
        bool commanderSucceeded,
        bool commanderHasData,
        IReadOnlyList<string> missingAgents,
        IReadOnlyList<string> failedAgents)
    {
        if (!hasCommander || missingAgents.Contains("commander", StringComparer.OrdinalIgnoreCase))
        {
            return "缺少指挥Agent结果，当前还不是完整的 commander 历史。";
        }

        if (!commanderSucceeded)
        {
            return "指挥Agent尚未成功完成，当前还不能生成交易计划。";
        }

        if (!commanderHasData)
        {
            return "指挥Agent未返回有效 data，当前还不能生成交易计划。";
        }

        if (missingAgents.Count > 0)
        {
            return $"缺少 {FormatAgentList(missingAgents)}，当前多Agent历史尚未完整。";
        }

        if (failedAgents.Count > 0)
        {
            return $"{FormatAgentList(failedAgents)} 尚未成功完成，请先补齐完整 commander 历史。";
        }

        return string.Empty;
    }

    private static string FormatAgentList(IEnumerable<string> agentIds)
    {
        return string.Join("、", agentIds.Select(agentId => AgentLabels.TryGetValue(agentId, out var label) ? label : agentId));
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string propertyName, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}