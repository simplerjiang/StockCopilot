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
            source.MarketReports.Select(ProjectItem).ToArray());
    }

    private static StockAgentLocalNewsItemDto ProjectItem(LocalNewsItemDto item)
    {
        return new StockAgentLocalNewsItemDto(
            item.Title,
            item.TranslatedTitle,
            item.Source,
            item.SourceTag,
            item.Category,
            item.PublishTime,
            item.CrawledAt,
            item.Url);
    }
}

internal sealed record StockAgentLocalNewsItemDto(
    string Title,
    string? TranslatedTitle,
    string Source,
    string SourceTag,
    string? Category,
    DateTime PublishTime,
    DateTime CrawledAt,
    string? Url
);

internal sealed record StockAgentLocalFactPackageDto(
    string Symbol,
    string? Name,
    string? SectorName,
    IReadOnlyList<StockAgentLocalNewsItemDto> StockNews,
    IReadOnlyList<StockAgentLocalNewsItemDto> SectorReports,
    IReadOnlyList<StockAgentLocalNewsItemDto> MarketReports
);