using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Storage;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public sealed class StockHistoryService : IStockHistoryService
{
    private readonly AppDbContext _dbContext;
    private readonly IStockDataService _dataService;

    public StockHistoryService(AppDbContext dbContext, IStockDataService dataService)
    {
        _dbContext = dbContext;
        _dataService = dataService;
    }

    public async Task UpsertAsync(StockQuoteDto quote, CancellationToken cancellationToken = default)
    {
        await UpsertCoreAsync(
            quote.Symbol,
            quote.Name,
            quote.Price,
            quote.ChangePercent,
            quote.TurnoverRate,
            quote.PeRatio,
            quote.High,
            quote.Low,
            quote.Speed,
            cancellationToken);
    }

    public async Task<StockQueryHistory?> RecordAsync(StockHistoryRecordRequestDto request, CancellationToken cancellationToken = default)
    {
        return await UpsertCoreAsync(
            request.Symbol,
            request.Name,
            request.Price,
            request.ChangePercent,
            request.TurnoverRate,
            request.PeRatio,
            request.High,
            request.Low,
            request.Speed,
            cancellationToken);
    }

    private async Task<StockQueryHistory?> UpsertCoreAsync(
        string symbolValue,
        string name,
        decimal price,
        decimal changePercent,
        decimal turnoverRate,
        decimal peRatio,
        decimal high,
        decimal low,
        decimal speed,
        CancellationToken cancellationToken)
    {
        if (!StockSymbolNormalizer.IsValid(symbolValue))
            return null;

        var symbol = StockSymbolNormalizer.Normalize(symbolValue);
        var existing = await _dbContext.StockQueryHistories
            .FirstOrDefaultAsync(x => x.Symbol == symbol, cancellationToken);

        if (existing is null)
        {
            existing = new StockQueryHistory { Symbol = symbol };
            _dbContext.StockQueryHistories.Add(existing);
        }

        existing.Name = name;
        existing.Price = price;
        existing.ChangePercent = changePercent;
        existing.TurnoverRate = turnoverRate;
        existing.PeRatio = peRatio;
        existing.High = high;
        existing.Low = low;
        existing.Speed = speed;
        existing.UpdatedAt = DateTime.UtcNow;

        await DbRetryHelper.SaveChangesWithRetryAsync(_dbContext, ct: cancellationToken);
        return existing;
    }

    public async Task<IReadOnlyList<StockQueryHistory>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.StockQueryHistories
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StockQueryHistory>> RefreshAsync(string? source = null, CancellationToken cancellationToken = default)
    {
        var list = await _dbContext.StockQueryHistories
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var item in list)
        {
            var quote = await _dataService.GetQuoteAsync(item.Symbol, source, cancellationToken);
            if (quote is null)
            {
                continue;
            }

            item.Name = quote.Name;
            item.Price = quote.Price;
            item.ChangePercent = quote.ChangePercent;
            item.TurnoverRate = quote.TurnoverRate;
            item.PeRatio = quote.PeRatio;
            item.High = quote.High;
            item.Low = quote.Low;
            item.Speed = quote.Speed;
            item.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return list;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.StockQueryHistories
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
        {
            return false;
        }

        _dbContext.StockQueryHistories.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
