using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

internal static class ForumPostCountSchemaInitializer
{
    public static async Task EnsureAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var provider = dbContext.Database.ProviderName ?? string.Empty;
        if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await EnsureSqlServerAsync(dbContext, cancellationToken);
            return;
        }

        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await EnsureSqliteAsync(dbContext, cancellationToken);
            return;
        }

        return;
    }

    private static async Task EnsureSqlServerAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID('dbo.ForumPostCounts', 'U') IS NULL " +
            "BEGIN " +
            "CREATE TABLE dbo.ForumPostCounts(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ForumPostCounts PRIMARY KEY, " +
            "Symbol NVARCHAR(32) NOT NULL, " +
            "Platform NVARCHAR(32) NOT NULL, " +
            "TradingDate NVARCHAR(10) NOT NULL, " +
            "SessionPhase NVARCHAR(16) NOT NULL, " +
            "PostCount INT NOT NULL, " +
            "CollectedAt DATETIME2 NOT NULL, " +
            "CONSTRAINT UQ_ForumPostCounts UNIQUE(Symbol, Platform, TradingDate, SessionPhase)" +
            "); " +
            "END; " +
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ForumPostCounts_Symbol_Date' AND object_id = OBJECT_ID('dbo.ForumPostCounts')) " +
            "CREATE INDEX IX_ForumPostCounts_Symbol_Date ON dbo.ForumPostCounts(Symbol, TradingDate); " +
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ForumPostCounts_TradingDate' AND object_id = OBJECT_ID('dbo.ForumPostCounts')) " +
            "CREATE INDEX IX_ForumPostCounts_TradingDate ON dbo.ForumPostCounts(TradingDate);",
            cancellationToken);
    }

    private static async Task EnsureSqliteAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS ForumPostCounts(" +
            "Id INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "Symbol TEXT NOT NULL, " +
            "Platform TEXT NOT NULL, " +
            "TradingDate TEXT NOT NULL, " +
            "SessionPhase TEXT NOT NULL, " +
            "PostCount INTEGER NOT NULL, " +
            "CollectedAt TEXT NOT NULL, " +
            "UNIQUE(Symbol, Platform, TradingDate, SessionPhase)" +
            ");",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ForumPostCounts_Symbol_Date ON ForumPostCounts(Symbol, TradingDate);",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ForumPostCounts_TradingDate ON ForumPostCounts(TradingDate);",
            cancellationToken);
    }
}
