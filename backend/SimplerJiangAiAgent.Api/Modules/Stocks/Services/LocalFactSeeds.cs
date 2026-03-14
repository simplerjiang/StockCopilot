using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

internal sealed record LocalStockNewsSeed(
    string Symbol,
    string Name,
    string? SectorName,
    string Title,
    string Category,
    string Source,
    string SourceTag,
    string? ExternalId,
    DateTime PublishTime,
    DateTime CrawledAt,
    string? Url
);

internal sealed record LocalSectorReportSeed(
    string? Symbol,
    string? SectorName,
    string Level,
    string Title,
    string Source,
    string SourceTag,
    string? ExternalId,
    DateTime PublishTime,
    DateTime CrawledAt,
    string? Url
);

internal sealed record EastmoneyCompanyProfileDto(
    string Symbol,
    string Name,
    string? SectorName,
    int? ShareholderCount,
    IReadOnlyList<StockFundamentalFactDto> Facts
);