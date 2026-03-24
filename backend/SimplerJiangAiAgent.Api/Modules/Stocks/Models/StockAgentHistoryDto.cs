using System.Text.Json;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Models;

public sealed record StockAgentHistoryCreateDto(
    string Symbol,
    string Name,
    string? Interval,
    string? Source,
    string? Provider,
    string? Model,
    bool UseInternet,
    JsonElement Result
);

public sealed record StockAgentHistoryItemDto(
    long Id,
    string Symbol,
    string Name,
    string? Summary,
    DateTime CreatedAt,
    bool IsCommanderComplete,
    string? CommanderBlockedReason
);

public sealed record StockAgentHistoryDetailDto(
    long Id,
    string Symbol,
    string Name,
    string? Summary,
    DateTime CreatedAt,
    JsonElement Result,
    bool IsCommanderComplete,
    string? CommanderBlockedReason
);
