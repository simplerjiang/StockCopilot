using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

public sealed class StockSyncService : IStockSyncService
{
    private readonly AppDbContext _dbContext;
    private readonly IStockCrawler _crawler;
    private readonly StockSyncOptions _options;

    public StockSyncService(AppDbContext dbContext, IStockCrawler crawler, IOptions<StockSyncOptions> options)
    {
        _dbContext = dbContext;
        _crawler = crawler;
        _options = options.Value;
    }

    public async Task SyncOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_options.MarketIndexSymbol))
        {
            var market = await _crawler.GetMarketIndexAsync(_options.MarketIndexSymbol, cancellationToken);
            _dbContext.MarketIndexSnapshots.Add(MapMarket(market));
        }

        foreach (var symbol in _options.Symbols)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                continue;
            }

            var target = StockSymbolNormalizer.Normalize(symbol.Trim());
            var quote = await _crawler.GetQuoteAsync(target, cancellationToken);
            var kline = await _crawler.GetKLineAsync(target, "day", 60, cancellationToken);
            var minute = await _crawler.GetMinuteLineAsync(target, cancellationToken);
            var messages = await _crawler.GetIntradayMessagesAsync(target, cancellationToken);
            var mergedKLine = StockRealtimeKLineMerge.MergeDailyFromMinuteLines(kline, minute, 60);

            _dbContext.StockQuoteSnapshots.Add(MapQuote(quote));
            await UpsertCompanyProfileAsync(_dbContext, quote, null, cancellationToken);
            await UpsertKLineAsync(_dbContext, target, "day", mergedKLine, cancellationToken);
            await UpsertMinuteLineAsync(_dbContext, target, minute, cancellationToken);
            await UpsertMessagesAsync(_dbContext, target, messages, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveDetailAsync(SimplerJiangAiAgent.Api.Modules.Stocks.Models.StockDetailDto detail, string interval, CancellationToken cancellationToken = default)
    {
        if (detail is null)
        {
            return;
        }

        var symbol = StockSymbolNormalizer.Normalize(detail.Quote.Symbol);

        _dbContext.StockQuoteSnapshots.Add(MapQuote(detail.Quote with { Symbol = symbol }));
        await UpsertCompanyProfileAsync(_dbContext, detail.Quote with { Symbol = symbol }, detail.FundamentalSnapshot, cancellationToken);
        await UpsertMessagesAsync(_dbContext, symbol, detail.Messages, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static MarketIndexSnapshot MapMarket(MarketIndexDto dto)
    {
        return new MarketIndexSnapshot
        {
            Symbol = dto.Symbol,
            Name = dto.Name,
            Price = dto.Price,
            Change = dto.Change,
            ChangePercent = dto.ChangePercent,
            Timestamp = dto.Timestamp
        };
    }

    private static StockQuoteSnapshot MapQuote(StockQuoteDto dto)
    {
        return new StockQuoteSnapshot
        {
            Symbol = dto.Symbol,
            Name = dto.Name,
            Price = dto.Price,
            Change = dto.Change,
            ChangePercent = dto.ChangePercent,
            PeRatio = dto.PeRatio,
            FloatMarketCap = dto.FloatMarketCap,
            VolumeRatio = dto.VolumeRatio,
            ShareholderCount = dto.ShareholderCount,
            SectorName = dto.SectorName,
            Timestamp = dto.Timestamp
        };
    }

    private static async Task UpsertCompanyProfileAsync(AppDbContext dbContext, StockQuoteDto quote, StockFundamentalSnapshotDto? fundamentalSnapshot, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(quote.SectorName) && !quote.ShareholderCount.HasValue && (fundamentalSnapshot?.Facts.Count ?? 0) == 0)
        {
            return;
        }

        var symbol = StockSymbolNormalizer.Normalize(quote.Symbol);
        var serializedFacts = StockFundamentalSnapshotMapper.SerializeFacts(fundamentalSnapshot?.Facts);
        var existing = await dbContext.StockCompanyProfiles.FirstOrDefaultAsync(x => x.Symbol == symbol, cancellationToken);
        if (existing is null)
        {
            dbContext.StockCompanyProfiles.Add(new StockCompanyProfile
            {
                Symbol = symbol,
                Name = quote.Name,
                SectorName = quote.SectorName,
                ShareholderCount = quote.ShareholderCount,
                FundamentalFactsJson = serializedFacts,
                FundamentalUpdatedAt = fundamentalSnapshot?.UpdatedAt,
                UpdatedAt = quote.Timestamp
            });
            return;
        }

        existing.Name = quote.Name;
        existing.SectorName = quote.SectorName ?? existing.SectorName;
        existing.ShareholderCount = quote.ShareholderCount ?? existing.ShareholderCount;
        existing.FundamentalFactsJson = serializedFacts ?? existing.FundamentalFactsJson;
        existing.FundamentalUpdatedAt = fundamentalSnapshot?.UpdatedAt ?? existing.FundamentalUpdatedAt;
        existing.UpdatedAt = quote.Timestamp;
    }

    private static async Task UpsertKLineAsync(AppDbContext dbContext, string symbol, string interval, IReadOnlyList<KLinePointDto> points, CancellationToken cancellationToken)
    {
        if (points.Count == 0)
        {
            return;
        }

        var dates = points.Select(p => p.Date.Date).Distinct().ToArray();
        var existing = await dbContext.KLinePoints
            .Where(x => x.Symbol == symbol && x.Interval == interval && dates.Contains(x.Date.Date))
            .ToListAsync(cancellationToken);

        dbContext.KLinePoints.RemoveRange(existing);
        dbContext.KLinePoints.AddRange(points.Select(p => new KLinePointEntity
        {
            Symbol = symbol,
            Interval = interval,
            Date = p.Date,
            Open = p.Open,
            Close = p.Close,
            High = p.High,
            Low = p.Low,
            Volume = p.Volume
        }));
    }

    private static async Task UpsertMinuteLineAsync(AppDbContext dbContext, string symbol, IReadOnlyList<MinuteLinePointDto> points, CancellationToken cancellationToken)
    {
        if (points.Count == 0)
        {
            return;
        }

        var date = points[0].Date;
        var existing = await dbContext.MinuteLinePoints
            .Where(x => x.Symbol == symbol && x.Date == date)
            .ToListAsync(cancellationToken);

        dbContext.MinuteLinePoints.RemoveRange(existing);
        dbContext.MinuteLinePoints.AddRange(points.Select(p => new MinuteLinePointEntity
        {
            Symbol = symbol,
            Date = p.Date,
            Time = p.Time,
            Price = p.Price,
            AveragePrice = p.AveragePrice,
            Volume = p.Volume
        }));
    }

    private static async Task UpsertMessagesAsync(AppDbContext dbContext, string symbol, IReadOnlyList<IntradayMessageDto> messages, CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return;
        }

        var since = DateTime.UtcNow.AddHours(-6);
        var existing = await dbContext.IntradayMessages
            .Where(x => x.Symbol == symbol && x.PublishedAt >= since)
            .ToListAsync(cancellationToken);

        dbContext.IntradayMessages.RemoveRange(existing);
        dbContext.IntradayMessages.AddRange(messages.Select(m => new IntradayMessageEntity
        {
            Symbol = symbol,
            Title = m.Title,
            Source = m.Source,
            PublishedAt = m.PublishedAt,
            Url = m.Url
        }));
    }
}
