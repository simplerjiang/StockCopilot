namespace SimplerJiangAiAgent.Api.Modules.Stocks.Models;

public sealed record LocalNewsItemDto(
    string Title,
    string? TranslatedTitle,
    string Source,
    string SourceTag,
    string? Category,
    string Sentiment,
    DateTime PublishTime,
    DateTime CrawledAt,
    string? Url,
    string? AiTarget,
    IReadOnlyList<string> AiTags
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
    IReadOnlyList<LocalNewsItemDto> MarketReports
);

public sealed record LocalNewsArchiveItemDto(
    string Level,
    string? Symbol,
    string? Name,
    string? SectorName,
    string Title,
    string? TranslatedTitle,
    string Source,
    string SourceTag,
    string? Category,
    string Sentiment,
    DateTime PublishTime,
    DateTime CrawledAt,
    string? Url,
    string? AiTarget,
    IReadOnlyList<string> AiTags
);

public sealed record LocalNewsArchivePageDto(
    int Page,
    int PageSize,
    int Total,
    string? Keyword,
    string? Level,
    string? Sentiment,
    IReadOnlyList<LocalNewsArchiveItemDto> Items
);

public sealed record StockAgentQueryPolicyDto(
    bool AllowInternet,
    string Reason,
    string Mode
);