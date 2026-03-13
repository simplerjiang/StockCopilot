using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

public static class LocalFactSchemaInitializer
{
    public static async Task EnsureAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var provider = dbContext.Database.ProviderName ?? string.Empty;
        if (!provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID('dbo.LocalStockNews','U') IS NULL BEGIN " +
            "CREATE TABLE dbo.LocalStockNews (Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY, Symbol NVARCHAR(450) NOT NULL, Name NVARCHAR(MAX) NOT NULL, SectorName NVARCHAR(MAX) NULL, Title NVARCHAR(MAX) NOT NULL, Category NVARCHAR(128) NOT NULL, Source NVARCHAR(256) NOT NULL, SourceTag NVARCHAR(128) NOT NULL, ExternalId NVARCHAR(450) NULL, PublishTime DATETIME2 NOT NULL, CrawledAt DATETIME2 NOT NULL, Url NVARCHAR(MAX) NULL, IsAiProcessed BIT NOT NULL CONSTRAINT DF_LocalStockNews_IsAiProcessed DEFAULT 0, TranslatedTitle NVARCHAR(MAX) NULL, AiSentiment NVARCHAR(64) NOT NULL CONSTRAINT DF_LocalStockNews_AiSentiment DEFAULT N'中性', AiTarget NVARCHAR(256) NULL, AiTags NVARCHAR(MAX) NULL); " +
            "CREATE INDEX IX_LocalStockNews_Symbol_PublishTime ON dbo.LocalStockNews(Symbol, PublishTime); " +
            "CREATE INDEX IX_LocalStockNews_Symbol_SourceTag ON dbo.LocalStockNews(Symbol, SourceTag); " +
            "CREATE INDEX IX_LocalStockNews_IsAiProcessed_Symbol_PublishTime ON dbo.LocalStockNews(IsAiProcessed, Symbol, PublishTime); END;",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID('dbo.LocalSectorReports','U') IS NULL BEGIN " +
            "CREATE TABLE dbo.LocalSectorReports (Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY, Symbol NVARCHAR(450) NULL, SectorName NVARCHAR(MAX) NULL, Level NVARCHAR(64) NOT NULL, Title NVARCHAR(MAX) NOT NULL, Source NVARCHAR(256) NOT NULL, SourceTag NVARCHAR(128) NOT NULL, ExternalId NVARCHAR(450) NULL, PublishTime DATETIME2 NOT NULL, CrawledAt DATETIME2 NOT NULL, Url NVARCHAR(MAX) NULL, IsAiProcessed BIT NOT NULL CONSTRAINT DF_LocalSectorReports_IsAiProcessed DEFAULT 0, TranslatedTitle NVARCHAR(MAX) NULL, AiSentiment NVARCHAR(64) NOT NULL CONSTRAINT DF_LocalSectorReports_AiSentiment DEFAULT N'中性', AiTarget NVARCHAR(256) NULL, AiTags NVARCHAR(MAX) NULL); " +
            "CREATE INDEX IX_LocalSectorReports_Symbol_Level_PublishTime ON dbo.LocalSectorReports(Symbol, Level, PublishTime); " +
            "CREATE INDEX IX_LocalSectorReports_Level_PublishTime ON dbo.LocalSectorReports(Level, PublishTime); " +
            "CREATE INDEX IX_LocalSectorReports_IsAiProcessed_Level_Symbol_PublishTime ON dbo.LocalSectorReports(IsAiProcessed, Level, Symbol, PublishTime); END;",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync("IF COL_LENGTH('dbo.LocalStockNews','IsAiProcessed') IS NULL ALTER TABLE dbo.LocalStockNews ADD IsAiProcessed BIT NOT NULL CONSTRAINT DF_LocalStockNews_IsAiProcessed DEFAULT 0;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("IF COL_LENGTH('dbo.LocalStockNews','TranslatedTitle') IS NULL ALTER TABLE dbo.LocalStockNews ADD TranslatedTitle NVARCHAR(MAX) NULL;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("IF COL_LENGTH('dbo.LocalStockNews','AiSentiment') IS NULL ALTER TABLE dbo.LocalStockNews ADD AiSentiment NVARCHAR(64) NOT NULL CONSTRAINT DF_LocalStockNews_AiSentiment DEFAULT N'中性';", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("IF COL_LENGTH('dbo.LocalStockNews','AiTarget') IS NULL ALTER TABLE dbo.LocalStockNews ADD AiTarget NVARCHAR(256) NULL;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("IF COL_LENGTH('dbo.LocalStockNews','AiTags') IS NULL ALTER TABLE dbo.LocalStockNews ADD AiTags NVARCHAR(MAX) NULL;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LocalStockNews_IsAiProcessed_Symbol_PublishTime' AND object_id = OBJECT_ID('dbo.LocalStockNews')) CREATE INDEX IX_LocalStockNews_IsAiProcessed_Symbol_PublishTime ON dbo.LocalStockNews(IsAiProcessed, Symbol, PublishTime);", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync("IF COL_LENGTH('dbo.LocalSectorReports','IsAiProcessed') IS NULL ALTER TABLE dbo.LocalSectorReports ADD IsAiProcessed BIT NOT NULL CONSTRAINT DF_LocalSectorReports_IsAiProcessed DEFAULT 0;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("IF COL_LENGTH('dbo.LocalSectorReports','TranslatedTitle') IS NULL ALTER TABLE dbo.LocalSectorReports ADD TranslatedTitle NVARCHAR(MAX) NULL;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("IF COL_LENGTH('dbo.LocalSectorReports','AiSentiment') IS NULL ALTER TABLE dbo.LocalSectorReports ADD AiSentiment NVARCHAR(64) NOT NULL CONSTRAINT DF_LocalSectorReports_AiSentiment DEFAULT N'中性';", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("IF COL_LENGTH('dbo.LocalSectorReports','AiTarget') IS NULL ALTER TABLE dbo.LocalSectorReports ADD AiTarget NVARCHAR(256) NULL;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("IF COL_LENGTH('dbo.LocalSectorReports','AiTags') IS NULL ALTER TABLE dbo.LocalSectorReports ADD AiTags NVARCHAR(MAX) NULL;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LocalSectorReports_IsAiProcessed_Level_Symbol_PublishTime' AND object_id = OBJECT_ID('dbo.LocalSectorReports')) CREATE INDEX IX_LocalSectorReports_IsAiProcessed_Level_Symbol_PublishTime ON dbo.LocalSectorReports(IsAiProcessed, Level, Symbol, PublishTime);", cancellationToken);
    }
}