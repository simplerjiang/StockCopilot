using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

internal static class StockAgentLocalFactProjection
{
    public static StockAgentLocalFactPackageDto Create(LocalFactPackageDto source)
    {
        return new StockAgentLocalFactPackageDto(
            source.Symbol,
            source.Name,
            source.SectorName,
            source.StockNews.Select(ProjectItem).ToArray(),
            source.SectorReports.Select(ProjectItem).ToArray(),
            source.MarketReports.Select(ProjectItem).ToArray(),
            source.FundamentalUpdatedAt,
            source.FundamentalFacts.Select(item => new StockAgentLocalFundamentalFactDto(item.Label, item.Value, item.Source)).ToArray());
    }

    private static StockAgentLocalNewsItemDto ProjectItem(LocalNewsItemDto item)
    {
        return new StockAgentLocalNewsItemDto(
            item.LocalFactId,
            item.SourceRecordId,
            item.Title,
            item.TranslatedTitle,
            item.Source,
            item.SourceTag,
            item.Category,
            item.Sentiment,
            item.PublishTime,
            item.CrawledAt,
            item.Url,
            item.Excerpt,
            item.Summary,
            item.ReadMode,
            item.ReadStatus,
            item.IngestedAt,
            item.AiTarget,
            item.AiTags);
    }
}

public sealed record StockAgentLocalNewsItemDto(
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
    IReadOnlyList<string> AiTags
);

public sealed record StockAgentLocalFundamentalFactDto(
    string Label,
    string Value,
    string Source
);

public sealed record StockAgentLocalFactPackageDto(
    string Symbol,
    string? Name,
    string? SectorName,
    IReadOnlyList<StockAgentLocalNewsItemDto> StockNews,
    IReadOnlyList<StockAgentLocalNewsItemDto> SectorReports,
    IReadOnlyList<StockAgentLocalNewsItemDto> MarketReports,
    DateTime? FundamentalUpdatedAt,
    IReadOnlyList<StockAgentLocalFundamentalFactDto> FundamentalFacts
);