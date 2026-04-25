namespace SimplerJiangAiAgent.Api.Modules.Stocks.Models;

public sealed record LocalNewsItemDto(
    long? LocalFactId,
    string? SourceRecordId,
    string Title,
    string? TranslatedTitle,
    string Source,
    string SourceTag,
    string? Category,
    string Sentiment,
    DateTime PublishTime,
    DateTime CrawledAt,
    string? Url,
    string? Excerpt,
    string? Summary,
    string ReadMode,
    string ReadStatus,
    DateTime? IngestedAt,
    string? AiTarget,
    IReadOnlyList<string> AiTags,
    bool IsAiProcessed
);

public sealed record LocalFundamentalFactDto(
    string Label,
    string Value,
    string Source
);

public sealed record LocalNewsBucketDto(
    string Symbol,
    string Level,
    string? SectorName,
    IReadOnlyList<LocalNewsItemDto> Items
);

public sealed record LocalFactPackageDto(
    string Symbol,
    string? Name,
    string? SectorName,
    IReadOnlyList<LocalNewsItemDto> StockNews,
    IReadOnlyList<LocalNewsItemDto> SectorReports,
    IReadOnlyList<LocalNewsItemDto> MarketReports,
    DateTime? FundamentalUpdatedAt,
    IReadOnlyList<LocalFundamentalFactDto> FundamentalFacts
);

public sealed record LocalNewsArchiveItemDto(
    string Level,
    string? Symbol,
    string? Name,
    string? SectorName,
    long? LocalFactId,
    string? SourceRecordId,
    string Title,
    string? TranslatedTitle,
    string Source,
    string SourceTag,
    string? Category,
    string Sentiment,
    DateTime PublishTime,
    DateTime CrawledAt,
    string? Url,
    string? Excerpt,
    string? Summary,
    string ReadMode,
    string ReadStatus,
    DateTime? IngestedAt,
    string? AiTarget,
    IReadOnlyList<string> AiTags,
    bool IsAiProcessed
);

public sealed record LocalNewsArchivePageDto(
    int Page,
    int PageSize,
    int Total,
    string? Keyword,
    string? Level,
    string? Sentiment,
    IReadOnlyList<LocalNewsArchiveItemDto> Items,
    int PendingTotal = 0
);

public sealed record StockAgentQueryPolicyDto(
    bool AllowInternet,
    string Reason,
    string Mode
);