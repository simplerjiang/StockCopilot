using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SimplerJiangAiAgent.Api.Data;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend;

internal static class RecommendStageRunIndexRepairer
{
    public static async Task<int> RepairDuplicateStageRunIndexesAsync(
        AppDbContext db,
        ILogger? logger,
        CancellationToken ct)
    {
        var snapshots = await db.RecommendationStageSnapshots
            .OrderBy(snapshot => snapshot.TurnId)
            .ThenBy(snapshot => snapshot.StageRunIndex)
            .ThenBy(snapshot => snapshot.StartedAt ?? snapshot.CompletedAt ?? DateTime.MinValue)
            .ThenBy(snapshot => snapshot.Id)
            .ToListAsync(ct);

        var repairedCount = 0;

        foreach (var turnGroup in snapshots.GroupBy(snapshot => snapshot.TurnId))
        {
            var ordered = turnGroup.ToArray();
            var hasDuplicate = ordered
                .GroupBy(snapshot => snapshot.StageRunIndex)
                .Any(group => group.Count() > 1);

            if (!hasDuplicate)
            {
                continue;
            }

            for (var index = 0; index < ordered.Length; index++)
            {
                if (ordered[index].StageRunIndex == index)
                {
                    continue;
                }

                ordered[index].StageRunIndex = index;
                repairedCount++;
            }
        }

        if (repairedCount > 0)
        {
            await db.SaveChangesAsync(ct);
            logger?.LogWarning("Repaired {Count} duplicate recommend stage snapshot run indexes", repairedCount);
        }

        return repairedCount;
    }
}