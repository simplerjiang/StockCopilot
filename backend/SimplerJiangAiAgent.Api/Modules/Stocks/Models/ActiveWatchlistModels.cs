namespace SimplerJiangAiAgent.Api.Modules.Stocks.Models;

public sealed record ActiveWatchlistItemDto(
    long Id,
    string Symbol,
    string? Name,
    string SourceTag,
    string? Note,
    bool IsEnabled,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? LastQuoteSyncAt
);

public sealed record ActiveWatchlistUpsertDto(
    string Symbol,
    string? Name,
    string? SourceTag,
    string? Note,
    bool? IsEnabled
);

public sealed record ActiveWatchlistTouchDto(
    string? Name,
    string? SourceTag,
    string? Note
);