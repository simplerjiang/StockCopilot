using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

public interface IActiveWatchlistService
{
    Task<IReadOnlyList<ActiveWatchlist>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActiveWatchlist>> GetEnabledAsync(int maxCount, CancellationToken cancellationToken = default);

    Task<ActiveWatchlist> UpsertAsync(
        string symbol,
        string? name = null,
        string? sourceTag = null,
        string? note = null,
        bool isEnabled = true,
        CancellationToken cancellationToken = default);

    Task<ActiveWatchlist> TouchAsync(
        string symbol,
        string? name = null,
        string? sourceTag = null,
        string? note = null,
        CancellationToken cancellationToken = default);

    Task MarkSyncedAsync(string symbol, string? name, DateTime syncedAt, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(string symbol, CancellationToken cancellationToken = default);
}

public sealed class ActiveWatchlistService : IActiveWatchlistService
{
    private readonly AppDbContext _dbContext;

    public ActiveWatchlistService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ActiveWatchlist>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.ActiveWatchlists
            .AsNoTracking()
            .OrderByDescending(item => item.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActiveWatchlist>> GetEnabledAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        var take = Math.Max(1, maxCount);
        return await _dbContext.ActiveWatchlists
            .Where(item => item.IsEnabled)
            .OrderByDescending(item => item.UpdatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<ActiveWatchlist> UpsertAsync(
        string symbol,
        string? name = null,
        string? sourceTag = null,
        string? note = null,
        bool isEnabled = true,
        CancellationToken cancellationToken = default)
    {
        return UpsertCoreAsync(symbol, name, sourceTag, note, isEnabled, preserveExistingWhenNull: false, cancellationToken);
    }

    public Task<ActiveWatchlist> TouchAsync(
        string symbol,
        string? name = null,
        string? sourceTag = null,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        return UpsertCoreAsync(symbol, name, sourceTag, note, isEnabled: true, preserveExistingWhenNull: true, cancellationToken);
    }

    public async Task MarkSyncedAsync(string symbol, string? name, DateTime syncedAt, CancellationToken cancellationToken = default)
    {
        var normalized = StockSymbolNormalizer.Normalize(symbol);
        var existing = await _dbContext.ActiveWatchlists.FirstOrDefaultAsync(item => item.Symbol == normalized, cancellationToken);
        if (existing is null)
        {
            return;
        }

        existing.LastQuoteSyncAt = syncedAt;
        existing.UpdatedAt = syncedAt;
        if (!string.IsNullOrWhiteSpace(name))
        {
            existing.Name = name.Trim();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = StockSymbolNormalizer.Normalize(symbol);
        var existing = await _dbContext.ActiveWatchlists.FirstOrDefaultAsync(item => item.Symbol == normalized, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        _dbContext.ActiveWatchlists.Remove(existing);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<ActiveWatchlist> UpsertCoreAsync(
        string symbol,
        string? name,
        string? sourceTag,
        string? note,
        bool isEnabled,
        bool preserveExistingWhenNull,
        CancellationToken cancellationToken)
    {
        var normalized = StockSymbolNormalizer.Normalize(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("symbol 不能为空", nameof(symbol));
        }

        var now = DateTime.UtcNow;
        var existing = await _dbContext.ActiveWatchlists.FirstOrDefaultAsync(item => item.Symbol == normalized, cancellationToken);
        if (existing is null)
        {
            existing = new ActiveWatchlist
            {
                Symbol = normalized,
                Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
                SourceTag = string.IsNullOrWhiteSpace(sourceTag) ? "manual" : sourceTag.Trim(),
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                IsEnabled = isEnabled,
                CreatedAt = now,
                UpdatedAt = now
            };
            _dbContext.ActiveWatchlists.Add(existing);
        }
        else
        {
            existing.Name = SelectValue(existing.Name, name, preserveExistingWhenNull);
            existing.SourceTag = SelectValue(existing.SourceTag, sourceTag, preserveExistingWhenNull) ?? "manual";
            existing.Note = SelectValue(existing.Note, note, preserveExistingWhenNull);
            existing.IsEnabled = isEnabled;
            existing.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }

    private static string? SelectValue(string? current, string? incoming, bool preserveExistingWhenNull)
    {
        if (string.IsNullOrWhiteSpace(incoming))
        {
            return preserveExistingWhenNull ? current : null;
        }

        return incoming.Trim();
    }
}