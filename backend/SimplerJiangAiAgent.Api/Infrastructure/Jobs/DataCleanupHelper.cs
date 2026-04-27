using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

/// <summary>
/// Idempotent data cleanup executed on every startup.
/// Fixes legacy dirty data: "示例名称" placeholders and extra spaces after ST prefixes.
/// </summary>
public static class DataCleanupHelper
{
    public static async Task CleanStockNamesAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
    {
        // Clean StockQueryHistories – remove "示例名称" placeholder names
        var badNames = await dbContext.StockQueryHistories
            .Where(h => h.Name.Contains("示例名称"))
            .ToListAsync(cancellationToken);
        foreach (var h in badNames)
            h.Name = string.Empty;

        // Clean StockQueryHistories – normalize legacy ST-prefix spacing without touching ordinary names.
        var spacedNames = await dbContext.StockQueryHistories
            .Where(h => h.Name.Contains("ST") && h.Name.Length <= 12)
            .ToListAsync(cancellationToken);
        foreach (var h in spacedNames)
            h.Name = StockNameNormalizer.NormalizeDisplayName(h.Name);

        if (badNames.Count > 0 || spacedNames.Count > 0)
            await dbContext.SaveChangesAsync(cancellationToken);
    }
}
