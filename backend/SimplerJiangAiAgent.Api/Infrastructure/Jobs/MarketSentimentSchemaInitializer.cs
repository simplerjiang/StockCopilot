using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

public static class MarketSentimentSchemaInitializer
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
            "IF OBJECT_ID('dbo.MarketSentimentSnapshots', 'U') IS NULL " +
            "BEGIN " +
            "CREATE TABLE dbo.MarketSentimentSnapshots(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MarketSentimentSnapshots PRIMARY KEY, " +
            "TradingDate DATETIME2 NOT NULL, " +
            "SnapshotTime DATETIME2 NOT NULL, " +
            "SessionPhase NVARCHAR(16) NOT NULL, " +
            "StageLabel NVARCHAR(16) NOT NULL, " +
            "StageScore DECIMAL(18,2) NOT NULL, " +
            "MaxLimitUpStreak INT NOT NULL, " +
            "LimitUpCount INT NOT NULL, " +
            "LimitDownCount INT NOT NULL, " +
            "BrokenBoardCount INT NOT NULL, " +
            "BrokenBoardRate DECIMAL(18,2) NOT NULL, " +
            "Advancers INT NOT NULL, " +
            "Decliners INT NOT NULL, " +
            "FlatCount INT NOT NULL, " +
            "TotalTurnover DECIMAL(18,2) NOT NULL, " +
            "Top3SectorTurnoverShare DECIMAL(18,2) NOT NULL, " +
            "Top10SectorTurnoverShare DECIMAL(18,2) NOT NULL, " +
            "DiffusionScore DECIMAL(18,2) NOT NULL CONSTRAINT DF_MarketSentimentSnapshots_DiffusionScore DEFAULT(0), " +
            "ContinuationScore DECIMAL(18,2) NOT NULL CONSTRAINT DF_MarketSentimentSnapshots_ContinuationScore DEFAULT(0), " +
            "StageLabelV2 NVARCHAR(16) NOT NULL CONSTRAINT DF_MarketSentimentSnapshots_StageLabelV2 DEFAULT(N''), " +
            "StageConfidence DECIMAL(18,2) NOT NULL CONSTRAINT DF_MarketSentimentSnapshots_StageConfidence DEFAULT(0), " +
            "Top3SectorTurnoverShare5dAvg DECIMAL(18,2) NOT NULL CONSTRAINT DF_MarketSentimentSnapshots_Top3Share5d DEFAULT(0), " +
            "Top10SectorTurnoverShare5dAvg DECIMAL(18,2) NOT NULL CONSTRAINT DF_MarketSentimentSnapshots_Top10Share5d DEFAULT(0), " +
            "LimitUpCount5dAvg DECIMAL(18,2) NOT NULL CONSTRAINT DF_MarketSentimentSnapshots_LimitUp5d DEFAULT(0), " +
            "BrokenBoardRate5dAvg DECIMAL(18,2) NOT NULL CONSTRAINT DF_MarketSentimentSnapshots_BrokenBoard5d DEFAULT(0), " +
            "SourceTag NVARCHAR(32) NOT NULL, " +
            "RawJson NVARCHAR(MAX) NULL, " +
            "CreatedAt DATETIME2 NOT NULL" +
            "); " +
            "END; " +
            "IF COL_LENGTH('dbo.MarketSentimentSnapshots','DiffusionScore') IS NULL ALTER TABLE dbo.MarketSentimentSnapshots ADD DiffusionScore DECIMAL(18,2) NOT NULL CONSTRAINT DF_MarketSentimentSnapshots_DiffusionScore DEFAULT(0); " +
            "IF COL_LENGTH('dbo.MarketSentimentSnapshots','ContinuationScore') IS NULL ALTER TABLE dbo.MarketSentimentSnapshots ADD ContinuationScore DECIMAL(18,2) NOT NULL CONSTRAINT DF_MarketSentimentSnapshots_ContinuationScore DEFAULT(0); " +
            "IF COL_LENGTH('dbo.MarketSentimentSnapshots','StageLabelV2') IS NULL ALTER TABLE dbo.MarketSentimentSnapshots ADD StageLabelV2 NVARCHAR(16) NOT NULL CONSTRAINT DF_MarketSentimentSnapshots_StageLabelV2 DEFAULT(N''); " +
            "IF COL_LENGTH('dbo.MarketSentimentSnapshots','StageConfidence') IS NULL ALTER TABLE dbo.MarketSentimentSnapshots ADD StageConfidence DECIMAL(18,2) NOT NULL CONSTRAINT DF_MarketSentimentSnapshots_StageConfidence DEFAULT(0); " +
            "IF COL_LENGTH('dbo.MarketSentimentSnapshots','Top3SectorTurnoverShare5dAvg') IS NULL ALTER TABLE dbo.MarketSentimentSnapshots ADD Top3SectorTurnoverShare5dAvg DECIMAL(18,2) NOT NULL CONSTRAINT DF_MarketSentimentSnapshots_Top3Share5d DEFAULT(0); " +
            "IF COL_LENGTH('dbo.MarketSentimentSnapshots','Top10SectorTurnoverShare5dAvg') IS NULL ALTER TABLE dbo.MarketSentimentSnapshots ADD Top10SectorTurnoverShare5dAvg DECIMAL(18,2) NOT NULL CONSTRAINT DF_MarketSentimentSnapshots_Top10Share5d DEFAULT(0); " +
            "IF COL_LENGTH('dbo.MarketSentimentSnapshots','LimitUpCount5dAvg') IS NULL ALTER TABLE dbo.MarketSentimentSnapshots ADD LimitUpCount5dAvg DECIMAL(18,2) NOT NULL CONSTRAINT DF_MarketSentimentSnapshots_LimitUp5d DEFAULT(0); " +
            "IF COL_LENGTH('dbo.MarketSentimentSnapshots','BrokenBoardRate5dAvg') IS NULL ALTER TABLE dbo.MarketSentimentSnapshots ADD BrokenBoardRate5dAvg DECIMAL(18,2) NOT NULL CONSTRAINT DF_MarketSentimentSnapshots_BrokenBoard5d DEFAULT(0); " +
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MarketSentimentSnapshots_TradingDate_SnapshotTime' AND object_id = OBJECT_ID('dbo.MarketSentimentSnapshots')) CREATE INDEX IX_MarketSentimentSnapshots_TradingDate_SnapshotTime ON dbo.MarketSentimentSnapshots(TradingDate, SnapshotTime); " +
            "IF OBJECT_ID('dbo.SectorRotationSnapshots', 'U') IS NULL " +
            "BEGIN " +
            "CREATE TABLE dbo.SectorRotationSnapshots(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SectorRotationSnapshots PRIMARY KEY, " +
            "TradingDate DATETIME2 NOT NULL, " +
            "SnapshotTime DATETIME2 NOT NULL, " +
            "BoardType NVARCHAR(16) NOT NULL, " +
            "SectorCode NVARCHAR(32) NOT NULL, " +
            "SectorName NVARCHAR(128) NOT NULL, " +
            "ChangePercent DECIMAL(18,2) NOT NULL, " +
            "MainNetInflow DECIMAL(18,2) NOT NULL, " +
            "SuperLargeNetInflow DECIMAL(18,2) NOT NULL, " +
            "LargeNetInflow DECIMAL(18,2) NOT NULL, " +
            "MediumNetInflow DECIMAL(18,2) NOT NULL, " +
            "SmallNetInflow DECIMAL(18,2) NOT NULL, " +
            "TurnoverAmount DECIMAL(18,2) NOT NULL, " +
            "TurnoverShare DECIMAL(18,2) NOT NULL, " +
            "BreadthScore DECIMAL(18,2) NULL, " +
            "ContinuityScore DECIMAL(18,2) NOT NULL, " +
            "StrengthScore DECIMAL(18,2) NOT NULL, " +
            "NewsSentiment NVARCHAR(16) NOT NULL, " +
            "NewsHotCount INT NOT NULL, " +
            "LeaderSymbol NVARCHAR(32) NULL, " +
            "LeaderName NVARCHAR(128) NULL, " +
            "LeaderChangePercent DECIMAL(18,2) NULL, " +
            "RankNo INT NOT NULL, " +
            "Momentum5d DECIMAL(18,2) NULL, " +
            "Momentum10d DECIMAL(18,2) NULL, " +
            "Momentum20d DECIMAL(18,2) NULL, " +
            "RankChange5d INT NOT NULL CONSTRAINT DF_SectorRotationSnapshots_RankChange5d DEFAULT(0), " +
            "RankChange10d INT NOT NULL CONSTRAINT DF_SectorRotationSnapshots_RankChange10d DEFAULT(0), " +
            "RankChange20d INT NOT NULL CONSTRAINT DF_SectorRotationSnapshots_RankChange20d DEFAULT(0), " +
            "StrengthAvg5d DECIMAL(18,2) NOT NULL CONSTRAINT DF_SectorRotationSnapshots_StrengthAvg5d DEFAULT(0), " +
            "StrengthAvg10d DECIMAL(18,2) NOT NULL CONSTRAINT DF_SectorRotationSnapshots_StrengthAvg10d DEFAULT(0), " +
            "StrengthAvg20d DECIMAL(18,2) NOT NULL CONSTRAINT DF_SectorRotationSnapshots_StrengthAvg20d DEFAULT(0), " +
            "DiffusionRate DECIMAL(18,2) NULL, " +
            "AdvancerCount INT NULL, " +
            "DeclinerCount INT NULL, " +
            "FlatMemberCount INT NULL, " +
            "LimitUpMemberCount INT NULL, " +
            "LeaderStabilityScore DECIMAL(18,2) NOT NULL CONSTRAINT DF_SectorRotationSnapshots_LeaderStabilityScore DEFAULT(0), " +
            "MainlineScore DECIMAL(18,2) NOT NULL CONSTRAINT DF_SectorRotationSnapshots_MainlineScore DEFAULT(0), " +
            "IsMainline BIT NOT NULL CONSTRAINT DF_SectorRotationSnapshots_IsMainline DEFAULT(0), " +
            "SourceTag NVARCHAR(32) NOT NULL, " +
            "RawJson NVARCHAR(MAX) NULL, " +
            "CreatedAt DATETIME2 NOT NULL" +
            "); " +
            "END; " +
            "IF COL_LENGTH('dbo.SectorRotationSnapshots','RankChange5d') IS NULL ALTER TABLE dbo.SectorRotationSnapshots ADD RankChange5d INT NOT NULL CONSTRAINT DF_SectorRotationSnapshots_RankChange5d DEFAULT(0); " +
            "IF COL_LENGTH('dbo.SectorRotationSnapshots','RankChange10d') IS NULL ALTER TABLE dbo.SectorRotationSnapshots ADD RankChange10d INT NOT NULL CONSTRAINT DF_SectorRotationSnapshots_RankChange10d DEFAULT(0); " +
            "IF COL_LENGTH('dbo.SectorRotationSnapshots','RankChange20d') IS NULL ALTER TABLE dbo.SectorRotationSnapshots ADD RankChange20d INT NOT NULL CONSTRAINT DF_SectorRotationSnapshots_RankChange20d DEFAULT(0); " +
            "IF COL_LENGTH('dbo.SectorRotationSnapshots','StrengthAvg5d') IS NULL ALTER TABLE dbo.SectorRotationSnapshots ADD StrengthAvg5d DECIMAL(18,2) NOT NULL CONSTRAINT DF_SectorRotationSnapshots_StrengthAvg5d DEFAULT(0); " +
            "IF COL_LENGTH('dbo.SectorRotationSnapshots','StrengthAvg10d') IS NULL ALTER TABLE dbo.SectorRotationSnapshots ADD StrengthAvg10d DECIMAL(18,2) NOT NULL CONSTRAINT DF_SectorRotationSnapshots_StrengthAvg10d DEFAULT(0); " +
            "IF COL_LENGTH('dbo.SectorRotationSnapshots','StrengthAvg20d') IS NULL ALTER TABLE dbo.SectorRotationSnapshots ADD StrengthAvg20d DECIMAL(18,2) NOT NULL CONSTRAINT DF_SectorRotationSnapshots_StrengthAvg20d DEFAULT(0); " +
            "IF COL_LENGTH('dbo.SectorRotationSnapshots','DiffusionRate') IS NULL ALTER TABLE dbo.SectorRotationSnapshots ADD DiffusionRate DECIMAL(18,2) NULL; " +
            "IF COL_LENGTH('dbo.SectorRotationSnapshots','AdvancerCount') IS NULL ALTER TABLE dbo.SectorRotationSnapshots ADD AdvancerCount INT NULL; " +
            "IF COL_LENGTH('dbo.SectorRotationSnapshots','DeclinerCount') IS NULL ALTER TABLE dbo.SectorRotationSnapshots ADD DeclinerCount INT NULL; " +
            "IF COL_LENGTH('dbo.SectorRotationSnapshots','FlatMemberCount') IS NULL ALTER TABLE dbo.SectorRotationSnapshots ADD FlatMemberCount INT NULL; " +
            "IF COL_LENGTH('dbo.SectorRotationSnapshots','LimitUpMemberCount') IS NULL ALTER TABLE dbo.SectorRotationSnapshots ADD LimitUpMemberCount INT NULL; " +
            "IF COL_LENGTH('dbo.SectorRotationSnapshots','LeaderStabilityScore') IS NULL ALTER TABLE dbo.SectorRotationSnapshots ADD LeaderStabilityScore DECIMAL(18,2) NOT NULL CONSTRAINT DF_SectorRotationSnapshots_LeaderStabilityScore DEFAULT(0); " +
            "IF COL_LENGTH('dbo.SectorRotationSnapshots','MainlineScore') IS NULL ALTER TABLE dbo.SectorRotationSnapshots ADD MainlineScore DECIMAL(18,2) NOT NULL CONSTRAINT DF_SectorRotationSnapshots_MainlineScore DEFAULT(0); " +
            "IF COL_LENGTH('dbo.SectorRotationSnapshots','IsMainline') IS NULL ALTER TABLE dbo.SectorRotationSnapshots ADD IsMainline BIT NOT NULL CONSTRAINT DF_SectorRotationSnapshots_IsMainline DEFAULT(0); " +
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SectorRotationSnapshots_BoardType_SnapshotTime_RankNo' AND object_id = OBJECT_ID('dbo.SectorRotationSnapshots')) CREATE INDEX IX_SectorRotationSnapshots_BoardType_SnapshotTime_RankNo ON dbo.SectorRotationSnapshots(BoardType, SnapshotTime, RankNo); " +
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SectorRotationSnapshots_SectorCode_BoardType_SnapshotTime' AND object_id = OBJECT_ID('dbo.SectorRotationSnapshots')) CREATE INDEX IX_SectorRotationSnapshots_SectorCode_BoardType_SnapshotTime ON dbo.SectorRotationSnapshots(SectorCode, BoardType, SnapshotTime); " +
            "IF OBJECT_ID('dbo.SectorRotationLeaderSnapshots', 'U') IS NULL " +
            "BEGIN " +
            "CREATE TABLE dbo.SectorRotationLeaderSnapshots(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SectorRotationLeaderSnapshots PRIMARY KEY, " +
            "SectorRotationSnapshotId BIGINT NOT NULL, " +
            "RankInSector INT NOT NULL, " +
            "Symbol NVARCHAR(32) NOT NULL, " +
            "Name NVARCHAR(128) NOT NULL, " +
            "ChangePercent DECIMAL(18,2) NOT NULL, " +
            "TurnoverAmount DECIMAL(18,2) NOT NULL, " +
            "IsLimitUp BIT NOT NULL, " +
            "IsBrokenBoard BIT NOT NULL, " +
            "CreatedAt DATETIME2 NOT NULL" +
            "); " +
            "END; " +
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SectorRotationLeaderSnapshots_SectorRotationSnapshotId_RankInSector' AND object_id = OBJECT_ID('dbo.SectorRotationLeaderSnapshots')) CREATE INDEX IX_SectorRotationLeaderSnapshots_SectorRotationSnapshotId_RankInSector ON dbo.SectorRotationLeaderSnapshots(SectorRotationSnapshotId, RankInSector);",
            cancellationToken);
    }

    private static async Task EnsureSqliteAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        await AddSqliteColumnIfMissingAsync(dbContext, "MarketSentimentSnapshots", "DiffusionScore", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await AddSqliteColumnIfMissingAsync(dbContext, "MarketSentimentSnapshots", "ContinuationScore", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await AddSqliteColumnIfMissingAsync(dbContext, "MarketSentimentSnapshots", "StageLabelV2", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddSqliteColumnIfMissingAsync(dbContext, "MarketSentimentSnapshots", "StageConfidence", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await AddSqliteColumnIfMissingAsync(dbContext, "MarketSentimentSnapshots", "Top3SectorTurnoverShare5dAvg", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await AddSqliteColumnIfMissingAsync(dbContext, "MarketSentimentSnapshots", "Top10SectorTurnoverShare5dAvg", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await AddSqliteColumnIfMissingAsync(dbContext, "MarketSentimentSnapshots", "LimitUpCount5dAvg", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await AddSqliteColumnIfMissingAsync(dbContext, "MarketSentimentSnapshots", "BrokenBoardRate5dAvg", "REAL NOT NULL DEFAULT 0", cancellationToken);

        await AddSqliteColumnIfMissingAsync(dbContext, "SectorRotationSnapshots", "RankChange5d", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddSqliteColumnIfMissingAsync(dbContext, "SectorRotationSnapshots", "RankChange10d", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddSqliteColumnIfMissingAsync(dbContext, "SectorRotationSnapshots", "RankChange20d", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddSqliteColumnIfMissingAsync(dbContext, "SectorRotationSnapshots", "StrengthAvg5d", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await AddSqliteColumnIfMissingAsync(dbContext, "SectorRotationSnapshots", "StrengthAvg10d", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await AddSqliteColumnIfMissingAsync(dbContext, "SectorRotationSnapshots", "StrengthAvg20d", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await AddSqliteColumnIfMissingAsync(dbContext, "SectorRotationSnapshots", "DiffusionRate", "REAL", cancellationToken);
        await AddSqliteColumnIfMissingAsync(dbContext, "SectorRotationSnapshots", "AdvancerCount", "INTEGER", cancellationToken);
        await AddSqliteColumnIfMissingAsync(dbContext, "SectorRotationSnapshots", "DeclinerCount", "INTEGER", cancellationToken);
        await AddSqliteColumnIfMissingAsync(dbContext, "SectorRotationSnapshots", "FlatMemberCount", "INTEGER", cancellationToken);
        await AddSqliteColumnIfMissingAsync(dbContext, "SectorRotationSnapshots", "LimitUpMemberCount", "INTEGER", cancellationToken);
        await AddSqliteColumnIfMissingAsync(dbContext, "SectorRotationSnapshots", "LeaderStabilityScore", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await AddSqliteColumnIfMissingAsync(dbContext, "SectorRotationSnapshots", "MainlineScore", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await AddSqliteColumnIfMissingAsync(dbContext, "SectorRotationSnapshots", "IsMainline", "INTEGER NOT NULL DEFAULT 0", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_MarketSentimentSnapshots_TradingDate_SnapshotTime ON MarketSentimentSnapshots(TradingDate, SnapshotTime);",
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_SectorRotationSnapshots_BoardType_SnapshotTime_RankNo ON SectorRotationSnapshots(BoardType, SnapshotTime, RankNo);",
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_SectorRotationSnapshots_SectorCode_BoardType_SnapshotTime ON SectorRotationSnapshots(SectorCode, BoardType, SnapshotTime);",
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_SectorRotationLeaderSnapshots_SectorRotationSnapshotId_RankInSector ON SectorRotationLeaderSnapshots(SectorRotationSnapshotId, RankInSector);",
            cancellationToken);
    }

    private static async Task AddSqliteColumnIfMissingAsync(
        AppDbContext dbContext,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        try
        {
            var sql = "ALTER TABLE " + tableName + " ADD COLUMN " + columnName + " " + columnDefinition + ";";
            await dbContext.Database.ExecuteSqlRawAsync(
                sql,
                cancellationToken);
        }
        catch (Exception ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
    }
}
