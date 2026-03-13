using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IQueryLocalFactDatabaseTool
{
    Task<LocalFactPackageDto> QueryAsync(string symbol, CancellationToken cancellationToken = default);
    Task<LocalNewsBucketDto> QueryLevelAsync(string symbol, string level, CancellationToken cancellationToken = default);
    Task<LocalNewsBucketDto> QueryMarketAsync(CancellationToken cancellationToken = default);
}

public sealed class QueryLocalFactDatabaseTool : IQueryLocalFactDatabaseTool
{
    private readonly AppDbContext _dbContext;

    public QueryLocalFactDatabaseTool(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<LocalFactPackageDto> QueryAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = StockSymbolNormalizer.Normalize(symbol);
        var stockNewsRows = await _dbContext.LocalStockNews
            .Where(item => item.Symbol == normalized)
            .OrderByDescending(item => item.PublishTime)
            .Take(20)
            .Select(item => new
            {
                item.Name,
                item.SectorName,
                item.Title,
                item.TranslatedTitle,
                item.Source,
                item.SourceTag,
                item.Category,
                item.AiSentiment,
                item.AiTarget,
                item.AiTags,
                item.PublishTime,
                item.CrawledAt,
                item.Url
            })
            .ToListAsync(cancellationToken);

        var stockNews = stockNewsRows
            .Select(item => new
            {
                item.Name,
                item.SectorName,
                Dto = new LocalNewsItemDto(
                    item.Title,
                    item.TranslatedTitle,
                    item.Source,
                    item.SourceTag,
                    item.Category,
                    NormalizeAiSentiment(item.AiSentiment),
                    item.PublishTime,
                    item.CrawledAt,
                    item.Url,
                    item.AiTarget,
                    ParseAiTags(item.AiTags))
            })
            .ToList();

        var sectorReportRows = await _dbContext.LocalSectorReports
            .Where(item => item.Symbol == normalized && item.Level == "sector")
            .OrderByDescending(item => item.PublishTime)
            .Take(12)
            .Select(item => new
            {
                item.SectorName,
                item.Title,
                item.TranslatedTitle,
                item.Source,
                item.SourceTag,
                item.Level,
                item.AiSentiment,
                item.AiTarget,
                item.AiTags,
                item.PublishTime,
                item.CrawledAt,
                item.Url
            })
            .ToListAsync(cancellationToken);

        var sectorReports = sectorReportRows
            .Select(item => new
            {
                item.SectorName,
                Dto = new LocalNewsItemDto(
                    item.Title,
                    item.TranslatedTitle,
                    item.Source,
                    item.SourceTag,
                    item.Level,
                    NormalizeAiSentiment(item.AiSentiment),
                    item.PublishTime,
                    item.CrawledAt,
                    item.Url,
                    item.AiTarget,
                    ParseAiTags(item.AiTags))
            })
            .ToList();

        var marketReportRows = await _dbContext.LocalSectorReports
            .Where(item => item.Level == "market")
            .OrderByDescending(item => item.PublishTime)
            .Take(12)
            .Select(item => new
            {
                item.Title,
                item.TranslatedTitle,
                item.Source,
                item.SourceTag,
                item.Level,
                item.AiSentiment,
                item.AiTarget,
                item.AiTags,
                item.PublishTime,
                item.CrawledAt,
                item.Url
            })
            .ToListAsync(cancellationToken);

        var marketReports = marketReportRows
            .Select(item => new LocalNewsItemDto(
                item.Title,
                item.TranslatedTitle,
                item.Source,
                item.SourceTag,
                item.Level,
                NormalizeAiSentiment(item.AiSentiment),
                item.PublishTime,
                item.CrawledAt,
                item.Url,
                item.AiTarget,
                ParseAiTags(item.AiTags)))
            .ToList();

        var name = stockNews.Select(item => item.Name).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var sectorName = stockNews.Select(item => item.SectorName).Concat(sectorReports.Select(item => item.SectorName)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return new LocalFactPackageDto(
            normalized,
            name,
            sectorName,
            stockNews.Select(item => item.Dto).ToArray(),
            sectorReports.Select(item => item.Dto).ToArray(),
            marketReports);
    }

    public async Task<LocalNewsBucketDto> QueryLevelAsync(string symbol, string level, CancellationToken cancellationToken = default)
    {
        var package = await QueryAsync(symbol, cancellationToken);
        var normalizedLevel = string.IsNullOrWhiteSpace(level) ? "stock" : level.Trim().ToLowerInvariant();

        return normalizedLevel switch
        {
            "stock" => new LocalNewsBucketDto(package.Symbol, "stock", package.SectorName, package.StockNews),
            "sector" => new LocalNewsBucketDto(package.Symbol, "sector", package.SectorName, package.SectorReports),
            "market" => new LocalNewsBucketDto(package.Symbol, "market", package.SectorName, package.MarketReports),
            _ => throw new ArgumentException("level 仅支持 stock/sector/market", nameof(level))
        };
    }

    public async Task<LocalNewsBucketDto> QueryMarketAsync(CancellationToken cancellationToken = default)
    {
        var marketReportRows = await _dbContext.LocalSectorReports
            .Where(item => item.Level == "market")
            .OrderByDescending(item => item.PublishTime)
            .Take(12)
            .Select(item => new LocalNewsItemDto(
                item.Title,
                item.TranslatedTitle,
                item.Source,
                item.SourceTag,
                item.Level,
                NormalizeAiSentiment(item.AiSentiment),
                item.PublishTime,
                item.CrawledAt,
                item.Url,
                item.AiTarget,
                ParseAiTags(item.AiTags)))
            .ToListAsync(cancellationToken);

        return new LocalNewsBucketDto(string.Empty, "market", "大盘环境", marketReportRows);
    }

    private static string NormalizeAiSentiment(string? value)
    {
        return value is "利好" or "利空" or "中性" ? value : "中性";
    }

    private static IReadOnlyList<string> ParseAiTags(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        try
        {
            var tags = JsonSerializer.Deserialize<List<string>>(value);
            return tags?.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}