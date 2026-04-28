using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

public static class StockMarketDataSchemaInitializer
{
    public static async Task EnsureAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var provider = dbContext.Database.ProviderName ?? string.Empty;

        // SQLite: EnsureCreated() only works when the DB file is brand-new.
        // For existing databases, v0.5.0 tables must be created explicitly.
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            var conn = dbContext.Database.GetDbConnection();
            await conn.OpenAsync(cancellationToken);
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE IF NOT EXISTS IndexConstituents (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        IndexCode TEXT NOT NULL,
                        StockCode TEXT NOT NULL,
                        StockName TEXT NOT NULL,
                        UpdateDate TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS IX_IndexConstituents_IndexCode_StockCode
                        ON IndexConstituents (IndexCode, StockCode);

                    CREATE TABLE IF NOT EXISTS StockIndustryClassifications (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        StockCode TEXT NOT NULL,
                        StockName TEXT NOT NULL,
                        Industry TEXT NOT NULL,
                        IndustryCode TEXT NOT NULL,
                        ClassificationSystem TEXT NOT NULL DEFAULT '',
                        UpdateDate TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS IX_StockIndustryClassifications_StockCode
                        ON StockIndustryClassifications (StockCode);
                    """;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                await conn.CloseAsync();
            }
            return;
        }

        if (!provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID('dbo.ActiveWatchlists', 'U') IS NULL " +
            "BEGIN " +
            "CREATE TABLE dbo.ActiveWatchlists(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ActiveWatchlists PRIMARY KEY, " +
            "Symbol NVARCHAR(32) NOT NULL, " +
            "Name NVARCHAR(128) NULL, " +
            "SourceTag NVARCHAR(64) NOT NULL CONSTRAINT DF_ActiveWatchlists_SourceTag DEFAULT('manual'), " +
            "Note NVARCHAR(256) NULL, " +
            "IsEnabled BIT NOT NULL CONSTRAINT DF_ActiveWatchlists_IsEnabled DEFAULT(1), " +
            "CreatedAt DATETIME2 NOT NULL, " +
            "UpdatedAt DATETIME2 NOT NULL, " +
            "LastQuoteSyncAt DATETIME2 NULL" +
            "); " +
            "END; " +
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ActiveWatchlists_Symbol' AND object_id = OBJECT_ID('dbo.ActiveWatchlists')) " +
            "CREATE UNIQUE INDEX IX_ActiveWatchlists_Symbol ON dbo.ActiveWatchlists(Symbol); " +
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ActiveWatchlists_IsEnabled_UpdatedAt' AND object_id = OBJECT_ID('dbo.ActiveWatchlists')) " +
            "CREATE INDEX IX_ActiveWatchlists_IsEnabled_UpdatedAt ON dbo.ActiveWatchlists(IsEnabled, UpdatedAt);",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('dbo.StockQuoteSnapshots','PeRatio') IS NULL ALTER TABLE dbo.StockQuoteSnapshots ADD PeRatio DECIMAL(18,2) NOT NULL CONSTRAINT DF_StockQuoteSnapshots_PeRatio DEFAULT(0); " +
            "IF COL_LENGTH('dbo.StockQuoteSnapshots','FloatMarketCap') IS NULL ALTER TABLE dbo.StockQuoteSnapshots ADD FloatMarketCap DECIMAL(18,2) NOT NULL CONSTRAINT DF_StockQuoteSnapshots_FloatMarketCap DEFAULT(0); " +
            "IF COL_LENGTH('dbo.StockQuoteSnapshots','VolumeRatio') IS NULL ALTER TABLE dbo.StockQuoteSnapshots ADD VolumeRatio DECIMAL(18,2) NOT NULL CONSTRAINT DF_StockQuoteSnapshots_VolumeRatio DEFAULT(0); " +
            "IF COL_LENGTH('dbo.StockQuoteSnapshots','ShareholderCount') IS NULL ALTER TABLE dbo.StockQuoteSnapshots ADD ShareholderCount INT NULL; " +
            "IF COL_LENGTH('dbo.StockQuoteSnapshots','SectorName') IS NULL ALTER TABLE dbo.StockQuoteSnapshots ADD SectorName NVARCHAR(128) NULL;",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID('dbo.StockCompanyProfiles', 'U') IS NULL " +
            "BEGIN " +
            "CREATE TABLE dbo.StockCompanyProfiles(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockCompanyProfiles PRIMARY KEY, " +
            "Symbol NVARCHAR(32) NOT NULL, " +
            "Name NVARCHAR(128) NOT NULL, " +
            "SectorName NVARCHAR(128) NULL, " +
            "ShareholderCount INT NULL, " +
            "FundamentalFactsJson NVARCHAR(MAX) NULL, " +
            "FundamentalUpdatedAt DATETIME2 NULL, " +
            "UpdatedAt DATETIME2 NOT NULL" +
            "); " +
            "END; " +
            "IF COL_LENGTH('dbo.StockCompanyProfiles','FundamentalFactsJson') IS NULL ALTER TABLE dbo.StockCompanyProfiles ADD FundamentalFactsJson NVARCHAR(MAX) NULL; " +
            "IF COL_LENGTH('dbo.StockCompanyProfiles','FundamentalUpdatedAt') IS NULL ALTER TABLE dbo.StockCompanyProfiles ADD FundamentalUpdatedAt DATETIME2 NULL; " +
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_StockCompanyProfiles_Symbol' AND object_id = OBJECT_ID('dbo.StockCompanyProfiles')) " +
            "CREATE UNIQUE INDEX IX_StockCompanyProfiles_Symbol ON dbo.StockCompanyProfiles(Symbol);",
            cancellationToken);
    }
}