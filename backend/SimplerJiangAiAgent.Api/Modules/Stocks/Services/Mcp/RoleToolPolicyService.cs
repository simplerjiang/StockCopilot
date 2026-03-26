namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public sealed record McpToolAuthorizationResult(
    bool IsAllowed,
    string ToolName,
    string PolicyClass,
    string? ErrorCode,
    string? Reason);

public interface IRoleToolPolicyService
{
    McpToolAuthorizationResult AuthorizeSystemEndpoint(string toolName);
    McpToolAuthorizationResult AuthorizeRole(string roleType, string toolName);
}

public sealed class RoleToolPolicyService : IRoleToolPolicyService
{
    private const string BlockedToolAccessMode = "blocked";
    private const string DisabledToolAccessMode = "disabled";

    private readonly IMcpServiceRegistry _registry;
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _allowedToolsByRole;

    public RoleToolPolicyService(IMcpServiceRegistry registry, IStockAgentRoleContractRegistry contractRegistry)
    {
        _registry = registry;
        _allowedToolsByRole = BuildAllowedToolsByRole(registry, contractRegistry);
    }

    public McpToolAuthorizationResult AuthorizeSystemEndpoint(string toolName)
    {
        var registration = _registry.GetRequired(toolName);
        return new McpToolAuthorizationResult(true, registration.ToolName, registration.PolicyClass, null, null);
    }

    public McpToolAuthorizationResult AuthorizeRole(string roleType, string toolName)
    {
        if (string.IsNullOrWhiteSpace(roleType))
        {
            throw new ArgumentException("roleType 不能为空", nameof(roleType));
        }

        var registration = _registry.GetRequired(toolName);
        if (_allowedToolsByRole.TryGetValue(roleType.Trim(), out var allowedTools) && allowedTools.Contains(registration.ToolName))
        {
            return new McpToolAuthorizationResult(true, registration.ToolName, registration.PolicyClass, null, null);
        }

        return new McpToolAuthorizationResult(
            false,
            registration.ToolName,
            registration.PolicyClass,
            McpErrorCodes.RoleNotAuthorized,
            $"{roleType} 未被授权调用 {registration.ToolName}");
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> BuildAllowedToolsByRole(
        IMcpServiceRegistry registry,
        IStockAgentRoleContractRegistry contractRegistry)
    {
        var allowedToolsByRole = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var contract in contractRegistry.List())
        {
            if (!contract.AllowsDirectQueryTools ||
                string.Equals(contract.ToolAccessMode, BlockedToolAccessMode, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(contract.ToolAccessMode, DisabledToolAccessMode, StringComparison.OrdinalIgnoreCase))
            {
                allowedToolsByRole[contract.RoleId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            var allowedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var toolName in contract.PreferredMcpSequence)
            {
                var registration = registry.GetRequired(toolName);
                allowedTools.Add(registration.ToolName);
            }

            allowedToolsByRole[contract.RoleId] = allowedTools;
        }

        return allowedToolsByRole;
    }
}