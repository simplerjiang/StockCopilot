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
    private readonly ILocalFactArticleReadService _articleReadService;

    public QueryLocalFactDatabaseTool(AppDbContext dbContext, ILocalFactArticleReadService articleReadService)
    {
        _dbContext = dbContext;
        _articleReadService = articleReadService;
    }

    public async Task<LocalFactPackageDto> QueryAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = StockSymbolNormalizer.Normalize(symbol);
        var companyProfile = await _dbContext.StockCompanyProfiles
            .AsNoTracking()
            .Where(item => item.Symbol == normalized)
            .OrderByDescending(item => item.FundamentalUpdatedAt ?? item.UpdatedAt)
            .Select(item => new
            {
                item.Name,
                item.SectorName,
                item.FundamentalUpdatedAt,
                item.FundamentalFactsJson
            })
            .FirstOrDefaultAsync(cancellationToken);
        var stockName = companyProfile?.Name;

        var stockNewsRows = await _dbContext.LocalStockNews
            .Where(item => item.Symbol == normalized)
            .OrderByDescending(item => item.PublishTime)
            .Take(40)
            .ToListAsync(cancellationToken);

        await _articleReadService.PrepareAsync(stockNewsRows, cancellationToken);

        var stockNews = stockNewsRows
            .Where(item => LocalFactDisplayPolicy.IsStrongStockMatch(
                normalized,
                stockName ?? item.Name,
                item.Title,
                item.TranslatedTitle,
                item.AiTarget))
            .Take(20)
            .Select(item => new
            {
                item.Name,
                item.SectorName,
                Dto = new LocalNewsItemDto(
                    item.Id,
                    $"stock_news:{item.Id}",
                    item.Title,
                    LocalFactDisplayPolicy.SanitizeTranslatedTitle(item.Title, item.TranslatedTitle),
                    item.Source,
                    item.SourceTag,
                    item.Category,
                    NormalizeAiSentiment(item.AiSentiment),
                    item.PublishTime,
                    item.CrawledAt,
                    item.Url,
                    item.ArticleExcerpt,
                    item.ArticleSummary,
                    NormalizeReadMode(item.ReadMode, item.Url),
                    NormalizeReadStatus(item.ReadStatus, item.Url, item.ArticleSummary, item.ArticleExcerpt),
                    item.IngestedAt,
                    item.AiTarget,
                    ParseAiTags(item.AiTags))
            })
            .ToList();

        var sectorReportRows = await _dbContext.LocalSectorReports
            .Where(item => item.Symbol == normalized && item.Level == "sector")
            .OrderByDescending(item => item.PublishTime)
            .Take(12)
            .ToListAsync(cancellationToken);

        await _articleReadService.PrepareAsync(sectorReportRows, cancellationToken);

        var sectorReports = sectorReportRows
            .Select(item => new
            {
                item.SectorName,
                Dto = new LocalNewsItemDto(
                    item.Id,
                    $"sector_report:{item.Id}",
                    item.Title,
                    item.TranslatedTitle,
                    item.Source,
                    item.SourceTag,
                    item.Level,
                    NormalizeAiSentiment(item.AiSentiment),
                    item.PublishTime,
                    item.CrawledAt,
                    item.Url,
                    item.ArticleExcerpt,
                    item.ArticleSummary,
                    NormalizeReadMode(item.ReadMode, item.Url),
                    NormalizeReadStatus(item.ReadStatus, item.Url, item.ArticleSummary, item.ArticleExcerpt),
                    item.IngestedAt,
                    item.AiTarget,
                    ParseAiTags(item.AiTags))
            })
            .ToList();

        var fallbackSectorName = stockNews.Select(item => item.SectorName)
            .Concat(sectorReports.Select(item => item.SectorName))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? companyProfile?.SectorName;

        if (sectorReports.Count == 0 && !string.IsNullOrWhiteSpace(fallbackSectorName))
        {
            sectorReports = (await BuildSectorFallbackReportsAsync(normalized, fallbackSectorName!, cancellationToken))
                .Select(item => new
                {
                    SectorName = (string?)fallbackSectorName,
                    Dto = item
                })
                .ToList();
        }

        var marketReportRows = await _dbContext.LocalSectorReports
            .Where(item => item.Level == "market")
            .OrderByDescending(item => item.PublishTime)
            .Take(12)
            .ToListAsync(cancellationToken);

        await _articleReadService.PrepareAsync(marketReportRows, cancellationToken);

        if (_dbContext.ChangeTracker.HasChanges())
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var marketReports = marketReportRows
            .Select(item => new LocalNewsItemDto(
                item.Id,
                $"sector_report:{item.Id}",
                item.Title,
                item.TranslatedTitle,
                item.Source,
                item.SourceTag,
                item.Level,
                NormalizeAiSentiment(item.AiSentiment),
                item.PublishTime,
                item.CrawledAt,
                item.Url,
                item.ArticleExcerpt,
                item.ArticleSummary,
                NormalizeReadMode(item.ReadMode, item.Url),
                NormalizeReadStatus(item.ReadStatus, item.Url, item.ArticleSummary, item.ArticleExcerpt),
                item.IngestedAt,
                item.AiTarget,
                ParseAiTags(item.AiTags)))
            .ToList();

        var name = stockNews.Select(item => item.Name).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? companyProfile?.Name;
        var sectorName = fallbackSectorName;
        var fundamentalFacts = StockFundamentalSnapshotMapper.DeserializeFacts(companyProfile?.FundamentalFactsJson)
            .Select(item => new LocalFundamentalFactDto(item.Label, item.Value, item.Source))
            .ToArray();

        return new LocalFactPackageDto(
            normalized,
            name,
            sectorName,
            stockNews.Select(item => item.Dto).ToArray(),
            sectorReports.Select(item => item.Dto).ToArray(),
            marketReports,
            companyProfile?.FundamentalUpdatedAt,
            fundamentalFacts);
    }

    private async Task<IReadOnlyList<LocalNewsItemDto>> BuildSectorFallbackReportsAsync(
        string symbol,
        string sectorName,
        CancellationToken cancellationToken)
    {
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        var bareSymbol = StripExchangePrefix(normalizedSymbol);

        var sectorCandidates = await _dbContext.SectorRotationSnapshots
            .AsNoTracking()
            .Where(item =>
                item.SectorName == sectorName
                || item.SectorName.Contains(sectorName)
                || sectorName.Contains(item.SectorName))
            .ToListAsync(cancellationToken);

        var snapshot = sectorCandidates
            .OrderByDescending(item => item.SnapshotTime)
            .ThenByDescending(item => item.IsMainline)
            .ThenByDescending(item => item.MainlineScore)
            .ThenBy(item => item.RankNo)
            .FirstOrDefault();

        if (snapshot is null)
        {
            var leaderCandidates = await _dbContext.SectorRotationLeaderSnapshots
                .AsNoTracking()
                .Where(item => item.Symbol == bareSymbol || item.Symbol == normalizedSymbol)
                .Join(
                    _dbContext.SectorRotationSnapshots.AsNoTracking(),
                    leader => leader.SectorRotationSnapshotId,
                    sector => sector.Id,
                    (_, sector) => sector)
                .ToListAsync(cancellationToken);

            snapshot = leaderCandidates
                .OrderByDescending(item => item.SnapshotTime)
                .ThenByDescending(item => item.IsMainline)
                .ThenByDescending(item => item.MainlineScore)
                .ThenBy(item => item.RankNo)
                .FirstOrDefault();
        }

        if (snapshot is null)
        {
            return await BuildMarketContextFallbackReportsAsync(sectorName, cancellationToken);
        }

        var leaders = await _dbContext.SectorRotationLeaderSnapshots
            .AsNoTracking()
            .Where(item => item.SectorRotationSnapshotId == snapshot.Id)
            .OrderBy(item => item.RankInSector)
            .Take(3)
            .ToListAsync(cancellationToken);

        var items = new List<LocalNewsItemDto>
        {
            new(
                snapshot.Id,
                $"sector_snapshot:{snapshot.Id}",
                $"{snapshot.SectorName}板块最新轮动排名第{snapshot.RankNo}，主线分数{snapshot.MainlineScore:0.##}，扩散度{snapshot.DiffusionRate:0.##}",
                null,
                "本地板块轮动快照",
                "sector-rotation-fallback",
                "sector_snapshot",
                NormalizeSnapshotSentiment(snapshot.NewsSentiment, snapshot.IsMainline),
                snapshot.SnapshotTime,
                snapshot.CreatedAt,
                null,
                $"{snapshot.SectorName}板块轮动快照：主线分数{snapshot.MainlineScore:0.##}，扩散度{snapshot.DiffusionRate:0.##}。",
                $"{snapshot.SectorName}板块轮动快照。",
                "local_fact",
                "summary_only",
                snapshot.CreatedAt,
                $"板块:{snapshot.SectorName}",
                BuildSectorFallbackTags(snapshot))
        };

        foreach (var leader in leaders)
        {
            items.Add(new LocalNewsItemDto(
                leader.Id,
                $"sector_leader:{leader.Id}",
                $"{snapshot.SectorName}龙头 {leader.Name}({leader.Symbol}) 涨跌幅 {leader.ChangePercent:0.##}%" +
                (leader.IsLimitUp ? "，封板" : string.Empty) +
                (leader.IsBrokenBoard ? "，曾炸板" : string.Empty),
                null,
                "本地板块轮动快照",
                "sector-rotation-fallback",
                "sector_leader_snapshot",
                NormalizeSnapshotSentiment(snapshot.NewsSentiment, snapshot.IsMainline),
                snapshot.SnapshotTime,
                snapshot.CreatedAt,
                null,
                $"{leader.Name}({leader.Symbol}) 涨跌幅 {leader.ChangePercent:0.##}%。",
                $"{snapshot.SectorName}龙头表现摘要。",
                "local_fact",
                "summary_only",
                snapshot.CreatedAt,
                $"个股:{leader.Name}",
                new[] { "板块轮动", snapshot.BoardType, snapshot.SectorName }));
        }

        return items;
    }

    private async Task<IReadOnlyList<LocalNewsItemDto>> BuildMarketContextFallbackReportsAsync(
        string sectorName,
        CancellationToken cancellationToken)
    {
        var marketRows = await _dbContext.LocalSectorReports
            .AsNoTracking()
            .Where(item => item.Level == "market")
            .OrderByDescending(item => item.PublishTime)
            .Take(3)
            .Select(item => new
            {
                item.Id,
                item.Title,
                item.TranslatedTitle,
                item.Source,
                item.SourceTag,
                item.AiSentiment,
                item.AiTarget,
                item.AiTags,
                item.PublishTime,
                item.CrawledAt,
                item.Url,
                item.ArticleExcerpt,
                item.ArticleSummary,
                item.ReadMode,
                item.ReadStatus,
                item.IngestedAt
            })
            .ToListAsync(cancellationToken);

        if (marketRows.Count == 0)
        {
            return Array.Empty<LocalNewsItemDto>();
        }

        var items = new List<LocalNewsItemDto>
        {
            new(
                null,
                $"sector_market_fallback:{sectorName}",
                $"{sectorName}板块暂无专属本地资讯，以下补充最新市场环境要点作为板块上下文参考",
                null,
                "本地市场环境摘要",
                "sector-market-fallback",
                "sector_market_summary",
                "中性",
                marketRows[0].PublishTime,
                marketRows[0].CrawledAt,
                null,
                $"{sectorName}板块当前缺少专属本地资讯，已回退到市场环境摘要。",
                $"{sectorName}板块使用市场环境兜底。",
                "local_fact",
                "summary_only",
                marketRows[0].CrawledAt,
                $"板块:{sectorName}",
                new[] { "板块上下文兜底", sectorName, "市场环境" })
        };

        items.AddRange(marketRows.Select(item => new LocalNewsItemDto(
            item.Id,
            $"sector_report:{item.Id}",
            item.Title,
            item.TranslatedTitle,
            item.Source,
            item.SourceTag,
            "sector_market_fallback",
            NormalizeAiSentiment(item.AiSentiment),
            item.PublishTime,
            item.CrawledAt,
            item.Url,
            item.ArticleExcerpt,
            item.ArticleSummary,
            NormalizeReadMode(item.ReadMode, item.Url),
            NormalizeReadStatus(item.ReadStatus, item.Url, item.ArticleSummary, item.ArticleExcerpt),
            item.IngestedAt,
            item.AiTarget ?? $"板块:{sectorName}",
            ParseAiTags(item.AiTags).Concat(new[] { sectorName, "市场环境" }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())));

        return items;
    }

    private static string StripExchangePrefix(string symbol)
    {
        var normalized = StockSymbolNormalizer.Normalize(symbol);
        return normalized.Length > 2 && (normalized.StartsWith("sh") || normalized.StartsWith("sz"))
            ? normalized[2..]
            : normalized;
    }

    private static string NormalizeSnapshotSentiment(string? newsSentiment, bool isMainline)
    {
        if (!string.IsNullOrWhiteSpace(newsSentiment))
        {
            return NormalizeAiSentiment(newsSentiment);
        }

        return isMainline ? "利好" : "中性";
    }

    private static IReadOnlyList<string> BuildSectorFallbackTags(Data.Entities.SectorRotationSnapshot snapshot)
    {
        var tags = new List<string> { "板块轮动" };
        if (!string.IsNullOrWhiteSpace(snapshot.BoardType))
        {
            tags.Add(snapshot.BoardType);
        }

        if (snapshot.IsMainline)
        {
            tags.Add("主线板块");
        }

        return tags;
    }

    private static string NormalizeReadMode(string? readMode, string? url)
    {
        if (!string.IsNullOrWhiteSpace(readMode))
        {
            return readMode;
        }

        return string.IsNullOrWhiteSpace(url) ? "url_unavailable" : "local_fact";
    }

    private static string NormalizeReadStatus(string? readStatus, string? url, string? summary, string? excerpt)
    {
        if (!string.IsNullOrWhiteSpace(readStatus))
        {
            return readStatus;
        }

        if (!string.IsNullOrWhiteSpace(summary) || !string.IsNullOrWhiteSpace(excerpt))
        {
            return string.IsNullOrWhiteSpace(url) ? "summary_only" : "title_only";
        }

        return string.IsNullOrWhiteSpace(url) ? "metadata_only" : "unverified";
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
                item.Id,
                $"sector_report:{item.Id}",
                item.Title,
                item.TranslatedTitle,
                item.Source,
                item.SourceTag,
                item.Level,
                NormalizeAiSentiment(item.AiSentiment),
                item.PublishTime,
                item.CrawledAt,
                item.Url,
                item.ArticleExcerpt,
                item.ArticleSummary,
                NormalizeReadMode(item.ReadMode, item.Url),
                NormalizeReadStatus(item.ReadStatus, item.Url, item.ArticleSummary, item.ArticleExcerpt),
                item.IngestedAt,
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
                    item.Id,
                    $"stock_news:{item.Id}",
                    item.Title,
                    LocalFactDisplayPolicy.SanitizeTranslatedTitle(item.Title, item.TranslatedTitle),
                    item.Source,
                    item.SourceTag,
                    item.Category,
                    NormalizeAiSentiment(item.AiSentiment),
                    item.PublishTime,
                    item.CrawledAt,
                    item.Url,
                    item.ArticleExcerpt,
                    item.ArticleSummary,
                    NormalizeReadMode(item.ReadMode, item.Url),
                    NormalizeReadStatus(item.ReadStatus, item.Url, item.ArticleSummary, item.ArticleExcerpt),
                    item.IngestedAt,
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
                    item.Id,
                    $"sector_report:{item.Id}",
                    item.Title,
                    LocalFactDisplayPolicy.SanitizeTranslatedTitle(item.Title, item.TranslatedTitle),
                    item.Source,
                    item.SourceTag,
                    item.Level,
                    NormalizeAiSentiment(item.AiSentiment),
                    item.PublishTime,
                    item.CrawledAt,
                    item.Url,
                    item.ArticleExcerpt,
                    item.ArticleSummary,
                    NormalizeReadMode(item.ReadMode, item.Url),
                    NormalizeReadStatus(item.ReadStatus, item.Url, item.ArticleSummary, item.ArticleExcerpt),
                    item.IngestedAt,
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