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
    Task<LocalNewsArchivePageDto> QueryArchiveAsync(string? keyword, string? level, string? sentiment, int page, int pageSize, CancellationToken cancellationToken = default);
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

    public async Task<LocalNewsArchivePageDto> QueryArchiveAsync(
        string? keyword,
        string? level,
        string? sentiment,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page <= 0)
        {
            throw new ArgumentException("page 必须大于 0", nameof(page));
        }

        if (pageSize <= 0)
        {
            throw new ArgumentException("pageSize 必须大于 0", nameof(pageSize));
        }

        var normalizedLevel = NormalizeArchiveLevel(level);
        var normalizedSentiment = NormalizeArchiveSentiment(sentiment);
        var normalizedKeyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();

        var archiveItems = new List<LocalNewsArchiveItemDto>();

        if (normalizedLevel is null or "stock")
        {
            var stockQuery = _dbContext.LocalStockNews.AsNoTracking().AsQueryable();

            if (normalizedSentiment is not null)
            {
                stockQuery = stockQuery.Where(item => item.AiSentiment == normalizedSentiment);
            }

            if (normalizedKeyword is not null)
            {
                stockQuery = stockQuery.Where(item =>
                    item.Symbol.Contains(normalizedKeyword) ||
                    item.Name.Contains(normalizedKeyword) ||
                    (item.SectorName != null && item.SectorName.Contains(normalizedKeyword)) ||
                    item.Title.Contains(normalizedKeyword) ||
                    (item.TranslatedTitle != null && item.TranslatedTitle.Contains(normalizedKeyword)) ||
                    item.Source.Contains(normalizedKeyword) ||
                    (item.AiTarget != null && item.AiTarget.Contains(normalizedKeyword)));
            }

            archiveItems.AddRange(await stockQuery
                .Select(item => new LocalNewsArchiveItemDto(
                    "stock",
                    item.Symbol,
                    item.Name,
                    item.SectorName,
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
                    ParseAiTags(item.AiTags)))
                .ToListAsync(cancellationToken));
        }

        if (normalizedLevel != "stock")
        {
            var reportQuery = _dbContext.LocalSectorReports.AsNoTracking().AsQueryable();

            if (normalizedLevel is not null)
            {
                reportQuery = reportQuery.Where(item => item.Level == normalizedLevel);
            }

            if (normalizedSentiment is not null)
            {
                reportQuery = reportQuery.Where(item => item.AiSentiment == normalizedSentiment);
            }

            if (normalizedKeyword is not null)
            {
                reportQuery = reportQuery.Where(item =>
                    (item.Symbol != null && item.Symbol.Contains(normalizedKeyword)) ||
                    (item.SectorName != null && item.SectorName.Contains(normalizedKeyword)) ||
                    item.Title.Contains(normalizedKeyword) ||
                    (item.TranslatedTitle != null && item.TranslatedTitle.Contains(normalizedKeyword)) ||
                    item.Source.Contains(normalizedKeyword) ||
                    (item.AiTarget != null && item.AiTarget.Contains(normalizedKeyword)));
            }

            archiveItems.AddRange(await reportQuery
                .Select(item => new LocalNewsArchiveItemDto(
                    item.Level,
                    item.Symbol,
                    null,
                    item.SectorName,
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
                .ToListAsync(cancellationToken));
        }

        var orderedItems = archiveItems
            .OrderByDescending(item => item.PublishTime)
            .ThenByDescending(item => item.CrawledAt)
            .ToArray();

        var total = orderedItems.Length;
        var pagedItems = orderedItems
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

        return new LocalNewsArchivePageDto(
            page,
            pageSize,
            total,
            normalizedKeyword,
            normalizedLevel,
            normalizedSentiment,
            pagedItems);
    }

    private static string? NormalizeArchiveLevel(string? level)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            return null;
        }

        var normalized = level.Trim().ToLowerInvariant();
        return normalized is "stock" or "sector" or "market"
            ? normalized
            : throw new ArgumentException("level 仅支持 stock/sector/market", nameof(level));
    }

    private static string? NormalizeArchiveSentiment(string? sentiment)
    {
        if (string.IsNullOrWhiteSpace(sentiment))
        {
            return null;
        }

        var normalized = sentiment.Trim();
        return normalized is "利好" or "中性" or "利空"
            ? normalized
            : throw new ArgumentException("sentiment 仅支持 利好/中性/利空", nameof(sentiment));
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