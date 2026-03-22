using System.Text.Json;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Models;

public sealed record StockAgentRequestDto(
    string Symbol,
    string? Source,
    string? Provider,
    string? Model,
    string? Interval,
    int? Count,
    bool UseInternet = true,
    bool IsPro = false
);

public sealed record StockAgentSingleRequestDto(
    string Symbol,
    string AgentId,
    string? Source,
    string? Provider,
    string? Model,
    string? Interval,
    int? Count,
    bool UseInternet = true,
    IReadOnlyList<StockAgentResultDto>? DependencyResults = null,
    bool IsPro = false
);

public sealed record StockAgentResponseDto(
    string Symbol,
    string Name,
    DateTime Timestamp,
    IReadOnlyList<StockAgentResultDto> Agents
);

public sealed record StockAgentResultDto(
    string AgentId,
    string AgentName,
    bool Success,
    string? Error,
    JsonElement? Data,
    string? RawContent,
    string? TraceId = null
);
