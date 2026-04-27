using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend;

public static class RecommendSessionSchemaInitializer
{
    public static async Task EnsureAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var provider = dbContext.Database.ProviderName ?? string.Empty;
        if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await EnsureSqlServerAsync(dbContext, cancellationToken);
        }
        else
        {
            await EnsureSqliteAsync(dbContext, cancellationToken);
        }
    }

    private static async Task EnsureSqlServerAsync(AppDbContext dbContext, CancellationToken ct)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'dbo.RecommendationSessions', N'U') IS NULL " +
            "CREATE TABLE dbo.RecommendationSessions(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RecommendationSessions PRIMARY KEY, " +
            "SessionKey NVARCHAR(128) NOT NULL, " +
            "Status NVARCHAR(32) NOT NULL, " +
            "ActiveTurnId BIGINT NULL, " +
            "LastUserIntent NVARCHAR(MAX) NULL, " +
            "MarketSentiment NVARCHAR(64) NULL, " +
            "TopSectorsJson NVARCHAR(MAX) NULL, " +
            "CreatedAt DATETIME2 NOT NULL, " +
            "UpdatedAt DATETIME2 NOT NULL);", ct);

        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'dbo.RecommendationTurns', N'U') IS NULL " +
            "CREATE TABLE dbo.RecommendationTurns(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RecommendationTurns PRIMARY KEY, " +
            "SessionId BIGINT NOT NULL, " +
            "TurnIndex INT NOT NULL, " +
            "UserPrompt NVARCHAR(MAX) NOT NULL, " +
            "Status NVARCHAR(32) NOT NULL, " +
            "ContinuationMode NVARCHAR(32) NOT NULL, " +
            "RoutingDecision NVARCHAR(MAX) NULL, " +
            "RoutingReasoning NVARCHAR(MAX) NULL, " +
            "RoutingConfidence DECIMAL(18,2) NULL, " +
            "RequestedAt DATETIME2 NOT NULL, " +
            "StartedAt DATETIME2 NULL, " +
            "CompletedAt DATETIME2 NULL);", ct);

        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'dbo.RecommendationStageSnapshots', N'U') IS NULL " +
            "CREATE TABLE dbo.RecommendationStageSnapshots(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RecommendationStageSnapshots PRIMARY KEY, " +
            "TurnId BIGINT NOT NULL, " +
            "StageType NVARCHAR(64) NOT NULL, " +
            "StageRunIndex INT NOT NULL, " +
            "ExecutionMode NVARCHAR(32) NOT NULL, " +
            "Status NVARCHAR(32) NOT NULL, " +
            "ActiveRoleIdsJson NVARCHAR(MAX) NULL, " +
            "Summary NVARCHAR(MAX) NULL, " +
            "StartedAt DATETIME2 NULL, " +
            "CompletedAt DATETIME2 NULL);", ct);

        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'dbo.RecommendationRoleStates', N'U') IS NULL " +
            "CREATE TABLE dbo.RecommendationRoleStates(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RecommendationRoleStates PRIMARY KEY, " +
            "StageId BIGINT NOT NULL, " +
            "RoleId NVARCHAR(64) NOT NULL, " +
            "RunIndex INT NOT NULL, " +
            "Status NVARCHAR(32) NOT NULL, " +
            "ToolPolicyClass NVARCHAR(64) NULL, " +
            "InputRefsJson NVARCHAR(MAX) NULL, " +
            "OutputRefsJson NVARCHAR(MAX) NULL, " +
            "OutputContentJson NVARCHAR(MAX) NULL, " +
            "ErrorCode NVARCHAR(64) NULL, " +
            "ErrorMessage NVARCHAR(MAX) NULL, " +
            "LlmTraceId NVARCHAR(128) NULL, " +
            "StartedAt DATETIME2 NULL, " +
            "CompletedAt DATETIME2 NULL);", ct);

        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'dbo.RecommendationFeedItems', N'U') IS NULL " +
            "CREATE TABLE dbo.RecommendationFeedItems(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RecommendationFeedItems PRIMARY KEY, " +
            "TurnId BIGINT NOT NULL, " +
            "StageId BIGINT NULL, " +
            "RoleId NVARCHAR(64) NULL, " +
            "ItemType NVARCHAR(32) NOT NULL, " +
            "Content NVARCHAR(MAX) NOT NULL, " +
            "MetadataJson NVARCHAR(MAX) NULL, " +
            "TraceId NVARCHAR(128) NULL, " +
            "CreatedAt DATETIME2 NOT NULL);", ct);

        // Indexes
        await EnsureIndexAsync(dbContext, "IX_RecommendationSessions_SessionKey",
            "CREATE UNIQUE INDEX IX_RecommendationSessions_SessionKey ON dbo.RecommendationSessions(SessionKey);", ct);
        await EnsureIndexAsync(dbContext, "IX_RecommendationSessions_UpdatedAt",
            "CREATE INDEX IX_RecommendationSessions_UpdatedAt ON dbo.RecommendationSessions(UpdatedAt);", ct);
        await EnsureIndexAsync(dbContext, "IX_RecommendationTurns_SessionId_TurnIndex",
            "CREATE UNIQUE INDEX IX_RecommendationTurns_SessionId_TurnIndex ON dbo.RecommendationTurns(SessionId, TurnIndex);", ct);
        await EnsureIndexAsync(dbContext, "IX_RecommendationStageSnapshots_TurnId_StageType",
            "CREATE INDEX IX_RecommendationStageSnapshots_TurnId_StageType ON dbo.RecommendationStageSnapshots(TurnId, StageType, StageRunIndex);", ct);
        await RepairDuplicateStageRunIndexesBeforeUniqueIndexAsync(dbContext, ct);
        await EnsureUniqueIndexAsync(dbContext, "UX_RecommendationStageSnapshots_TurnId_StageRunIndex",
            "CREATE UNIQUE INDEX UX_RecommendationStageSnapshots_TurnId_StageRunIndex ON dbo.RecommendationStageSnapshots(TurnId, StageRunIndex);", ct);
        await EnsureIndexAsync(dbContext, "IX_RecommendationRoleStates_StageId_RoleId",
            "CREATE INDEX IX_RecommendationRoleStates_StageId_RoleId ON dbo.RecommendationRoleStates(StageId, RoleId, RunIndex);", ct);
        await EnsureIndexAsync(dbContext, "IX_RecommendationFeedItems_TurnId_CreatedAt",
            "CREATE INDEX IX_RecommendationFeedItems_TurnId_CreatedAt ON dbo.RecommendationFeedItems(TurnId, CreatedAt);", ct);
    }

    private static async Task EnsureIndexAsync(AppDbContext dbContext, string indexName, string createSql, CancellationToken ct)
    {
        SqlIdentifierGuard.ValidateSqlIdentifier(indexName, nameof(indexName));
#pragma warning disable EF1002
        await dbContext.Database.ExecuteSqlRawAsync(
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{indexName}') {createSql}", ct);
#pragma warning restore EF1002
    }

    private static async Task EnsureUniqueIndexAsync(AppDbContext dbContext, string indexName, string createSql, CancellationToken ct)
    {
        SqlIdentifierGuard.ValidateSqlIdentifier(indexName, nameof(indexName));
#pragma warning disable EF1002
        await dbContext.Database.ExecuteSqlRawAsync(
            $"IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.RecommendationStageSnapshots') AND name = N'{indexName}' AND is_unique = 0) " +
            $"DROP INDEX {indexName} ON dbo.RecommendationStageSnapshots; " +
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.RecommendationStageSnapshots') AND name = N'{indexName}' AND is_unique = 1) {createSql}", ct);
#pragma warning restore EF1002
    }

    private static async Task EnsureSqliteAsync(AppDbContext dbContext, CancellationToken ct)
    {
        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS RecommendationSessions (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionKey      TEXT    NOT NULL,
                Status          TEXT    NOT NULL,
                ActiveTurnId    INTEGER NULL,
                LastUserIntent  TEXT    NULL,
                MarketSentiment TEXT    NULL,
                TopSectorsJson  TEXT    NULL,
                CreatedAt       TEXT    NOT NULL,
                UpdatedAt       TEXT    NOT NULL
            );", ct);

        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS RecommendationTurns (
                Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId           INTEGER NOT NULL,
                TurnIndex           INTEGER NOT NULL,
                UserPrompt          TEXT    NOT NULL,
                Status              TEXT    NOT NULL,
                ContinuationMode    TEXT    NOT NULL,
                RoutingDecision     TEXT    NULL,
                RoutingReasoning    TEXT    NULL,
                RoutingConfidence   REAL    NULL,
                RequestedAt         TEXT    NOT NULL,
                StartedAt           TEXT    NULL,
                CompletedAt         TEXT    NULL
            );", ct);

        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS RecommendationStageSnapshots (
                Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                TurnId              INTEGER NOT NULL,
                StageType           TEXT    NOT NULL,
                StageRunIndex       INTEGER NOT NULL,
                ExecutionMode       TEXT    NOT NULL,
                Status              TEXT    NOT NULL,
                ActiveRoleIdsJson   TEXT    NULL,
                Summary             TEXT    NULL,
                StartedAt           TEXT    NULL,
                CompletedAt         TEXT    NULL
            );", ct);

        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS RecommendationRoleStates (
                Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                StageId             INTEGER NOT NULL,
                RoleId              TEXT    NOT NULL,
                RunIndex            INTEGER NOT NULL,
                Status              TEXT    NOT NULL,
                ToolPolicyClass     TEXT    NULL,
                InputRefsJson       TEXT    NULL,
                OutputRefsJson      TEXT    NULL,
                OutputContentJson   TEXT    NULL,
                ErrorCode           TEXT    NULL,
                ErrorMessage        TEXT    NULL,
                LlmTraceId          TEXT    NULL,
                StartedAt           TEXT    NULL,
                CompletedAt         TEXT    NULL
            );", ct);

        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS RecommendationFeedItems (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                TurnId          INTEGER NOT NULL,
                StageId         INTEGER NULL,
                RoleId          TEXT    NULL,
                ItemType        TEXT    NOT NULL,
                Content         TEXT    NOT NULL,
                MetadataJson    TEXT    NULL,
                TraceId         TEXT    NULL,
                CreatedAt       TEXT    NOT NULL
            );", ct);

        // SQLite indexes
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_RecommendationSessions_SessionKey ON RecommendationSessions(SessionKey);", ct);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_RecommendationSessions_UpdatedAt ON RecommendationSessions(UpdatedAt);", ct);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_RecommendationTurns_SessionId_TurnIndex ON RecommendationTurns(SessionId, TurnIndex);", ct);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_RecommendationStageSnapshots_TurnId ON RecommendationStageSnapshots(TurnId, StageType, StageRunIndex);", ct);
        await RepairDuplicateStageRunIndexesBeforeUniqueIndexAsync(dbContext, ct);
        await dbContext.Database.ExecuteSqlRawAsync(
            "DROP INDEX IF EXISTS UX_RecommendationStageSnapshots_TurnId_StageRunIndex; " +
            "CREATE UNIQUE INDEX UX_RecommendationStageSnapshots_TurnId_StageRunIndex ON RecommendationStageSnapshots(TurnId, StageRunIndex);", ct);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_RecommendationRoleStates_StageId ON RecommendationRoleStates(StageId, RoleId, RunIndex);", ct);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_RecommendationFeedItems_TurnId ON RecommendationFeedItems(TurnId, CreatedAt);", ct);
    }

    private static Task<bool> HasDuplicateStageRunIndexesAsync(AppDbContext dbContext, CancellationToken ct) =>
        dbContext.RecommendationStageSnapshots
            .AsNoTracking()
            .GroupBy(snapshot => new { snapshot.TurnId, snapshot.StageRunIndex })
            .AnyAsync(group => group.Count() > 1, ct);

    private static async Task RepairDuplicateStageRunIndexesBeforeUniqueIndexAsync(AppDbContext dbContext, CancellationToken ct)
    {
        await RecommendStageRunIndexRepairer.RepairDuplicateStageRunIndexesAsync(dbContext, null, ct);

        if (await HasDuplicateStageRunIndexesAsync(dbContext, ct))
        {
            throw new InvalidOperationException(
                "Cannot create recommendation stage snapshot unique index because duplicate TurnId/StageRunIndex rows remain after automatic repair.");
        }
    }
}

file static class SqlIdentifierGuard
{
    private static readonly Regex SafeIdentifier =
        new(@"^\w+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static void ValidateSqlIdentifier(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value) || !SafeIdentifier.IsMatch(value))
            throw new ArgumentException(
                $"SQL identifier '{paramName}' contains unsafe characters: '{value}'.", paramName);
    }
}
