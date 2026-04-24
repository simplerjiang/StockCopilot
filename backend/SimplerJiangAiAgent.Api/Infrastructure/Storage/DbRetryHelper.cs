using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SimplerJiangAiAgent.Api.Infrastructure.Storage;

public static class DbRetryHelper
{
    public static async Task SaveChangesWithRetryAsync(DbContext context, int maxRetries = 3, CancellationToken ct = default)
    {
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                await context.SaveChangesAsync(ct);
                return;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 5 && attempt < maxRetries) // SQLITE_BUSY
            {
                await Task.Delay(100 * (attempt + 1), ct);
            }
        }
    }
}
