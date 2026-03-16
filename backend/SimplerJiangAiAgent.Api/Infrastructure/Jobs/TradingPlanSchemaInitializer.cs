using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

public static class TradingPlanSchemaInitializer
{
    public static async Task EnsureAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID('dbo.TradingPlans', 'U') IS NULL " +
            "BEGIN " +
            "CREATE TABLE dbo.TradingPlans(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TradingPlans PRIMARY KEY, " +
            "Symbol NVARCHAR(32) NOT NULL, " +
            "Name NVARCHAR(128) NOT NULL, " +
            "Direction NVARCHAR(16) NOT NULL, " +
            "Status NVARCHAR(16) NOT NULL, " +
            "TriggerPrice DECIMAL(18,2) NULL, " +
            "InvalidPrice DECIMAL(18,2) NULL, " +
            "StopLossPrice DECIMAL(18,2) NULL, " +
            "TakeProfitPrice DECIMAL(18,2) NULL, " +
            "TargetPrice DECIMAL(18,2) NULL, " +
            "ExpectedCatalyst NVARCHAR(MAX) NULL, " +
            "InvalidConditions NVARCHAR(MAX) NULL, " +
            "RiskLimits NVARCHAR(MAX) NULL, " +
            "AnalysisSummary NVARCHAR(MAX) NULL, " +
            "AnalysisHistoryId BIGINT NOT NULL, " +
            "SourceAgent NVARCHAR(64) NOT NULL CONSTRAINT DF_TradingPlans_SourceAgent DEFAULT('commander'), " +
            "UserNote NVARCHAR(MAX) NULL, " +
            "MarketStageLabelAtCreation NVARCHAR(16) NULL, " +
            "StageConfidenceAtCreation DECIMAL(18,2) NULL, " +
            "SuggestedPositionScale DECIMAL(18,4) NULL, " +
            "ExecutionFrequencyLabel NVARCHAR(32) NULL, " +
            "MainlineSectorName NVARCHAR(128) NULL, " +
            "MainlineScoreAtCreation DECIMAL(18,2) NULL, " +
            "SectorNameAtCreation NVARCHAR(128) NULL, " +
            "SectorCodeAtCreation NVARCHAR(32) NULL, " +
            "CreatedAt DATETIME2 NOT NULL, " +
            "UpdatedAt DATETIME2 NOT NULL, " +
            "TriggeredAt DATETIME2 NULL, " +
            "InvalidatedAt DATETIME2 NULL, " +
            "CancelledAt DATETIME2 NULL" +
            "); " +
            "END; " +
            "IF COL_LENGTH('dbo.TradingPlans','Symbol') IS NULL ALTER TABLE dbo.TradingPlans ADD Symbol NVARCHAR(32) NOT NULL CONSTRAINT DF_TradingPlans_Symbol DEFAULT(''); " +
            "IF COL_LENGTH('dbo.TradingPlans','Name') IS NULL ALTER TABLE dbo.TradingPlans ADD Name NVARCHAR(128) NOT NULL CONSTRAINT DF_TradingPlans_Name DEFAULT(''); " +
            "IF COL_LENGTH('dbo.TradingPlans','Direction') IS NULL ALTER TABLE dbo.TradingPlans ADD Direction NVARCHAR(16) NOT NULL CONSTRAINT DF_TradingPlans_Direction DEFAULT('Long'); " +
            "IF COL_LENGTH('dbo.TradingPlans','TriggerPrice') IS NULL ALTER TABLE dbo.TradingPlans ADD TriggerPrice DECIMAL(18,2) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','InvalidPrice') IS NULL ALTER TABLE dbo.TradingPlans ADD InvalidPrice DECIMAL(18,2) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','StopLossPrice') IS NULL ALTER TABLE dbo.TradingPlans ADD StopLossPrice DECIMAL(18,2) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','TakeProfitPrice') IS NULL ALTER TABLE dbo.TradingPlans ADD TakeProfitPrice DECIMAL(18,2) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','TargetPrice') IS NULL ALTER TABLE dbo.TradingPlans ADD TargetPrice DECIMAL(18,2) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','ExpectedCatalyst') IS NULL ALTER TABLE dbo.TradingPlans ADD ExpectedCatalyst NVARCHAR(MAX) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','InvalidConditions') IS NULL ALTER TABLE dbo.TradingPlans ADD InvalidConditions NVARCHAR(MAX) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','RiskLimits') IS NULL ALTER TABLE dbo.TradingPlans ADD RiskLimits NVARCHAR(MAX) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','AnalysisSummary') IS NULL ALTER TABLE dbo.TradingPlans ADD AnalysisSummary NVARCHAR(MAX) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','AnalysisHistoryId') IS NULL ALTER TABLE dbo.TradingPlans ADD AnalysisHistoryId BIGINT NOT NULL CONSTRAINT DF_TradingPlans_AnalysisHistoryId DEFAULT(0); " +
            "IF COL_LENGTH('dbo.TradingPlans','SourceAgent') IS NULL ALTER TABLE dbo.TradingPlans ADD SourceAgent NVARCHAR(64) NOT NULL CONSTRAINT DF_TradingPlans_SourceAgent DEFAULT('commander'); " +
            "IF COL_LENGTH('dbo.TradingPlans','UserNote') IS NULL ALTER TABLE dbo.TradingPlans ADD UserNote NVARCHAR(MAX) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','MarketStageLabelAtCreation') IS NULL ALTER TABLE dbo.TradingPlans ADD MarketStageLabelAtCreation NVARCHAR(16) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','StageConfidenceAtCreation') IS NULL ALTER TABLE dbo.TradingPlans ADD StageConfidenceAtCreation DECIMAL(18,2) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','SuggestedPositionScale') IS NULL ALTER TABLE dbo.TradingPlans ADD SuggestedPositionScale DECIMAL(18,4) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','ExecutionFrequencyLabel') IS NULL ALTER TABLE dbo.TradingPlans ADD ExecutionFrequencyLabel NVARCHAR(32) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','MainlineSectorName') IS NULL ALTER TABLE dbo.TradingPlans ADD MainlineSectorName NVARCHAR(128) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','MainlineScoreAtCreation') IS NULL ALTER TABLE dbo.TradingPlans ADD MainlineScoreAtCreation DECIMAL(18,2) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','SectorNameAtCreation') IS NULL ALTER TABLE dbo.TradingPlans ADD SectorNameAtCreation NVARCHAR(128) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','SectorCodeAtCreation') IS NULL ALTER TABLE dbo.TradingPlans ADD SectorCodeAtCreation NVARCHAR(32) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','TriggeredAt') IS NULL ALTER TABLE dbo.TradingPlans ADD TriggeredAt DATETIME2 NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','InvalidatedAt') IS NULL ALTER TABLE dbo.TradingPlans ADD InvalidatedAt DATETIME2 NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','CancelledAt') IS NULL ALTER TABLE dbo.TradingPlans ADD CancelledAt DATETIME2 NULL; " +
            "IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.TradingPlans') AND name = 'Status' AND max_length < 28) ALTER TABLE dbo.TradingPlans ALTER COLUMN Status NVARCHAR(16) NOT NULL; " +
            "IF COL_LENGTH('dbo.TradingPlans','PlanKey') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id WHERE dc.parent_object_id = OBJECT_ID('dbo.TradingPlans') AND c.name = 'PlanKey') ALTER TABLE dbo.TradingPlans ADD CONSTRAINT DF_TradingPlans_LegacyPlanKey DEFAULT('') FOR PlanKey; " +
            "IF COL_LENGTH('dbo.TradingPlans','Title') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id WHERE dc.parent_object_id = OBJECT_ID('dbo.TradingPlans') AND c.name = 'Title') ALTER TABLE dbo.TradingPlans ADD CONSTRAINT DF_TradingPlans_LegacyTitle DEFAULT('') FOR Title; " +
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TradingPlans_Symbol_CreatedAt' AND object_id = OBJECT_ID('dbo.TradingPlans')) " +
            "CREATE INDEX IX_TradingPlans_Symbol_CreatedAt ON dbo.TradingPlans(Symbol, CreatedAt); " +
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TradingPlans_AnalysisHistoryId' AND object_id = OBJECT_ID('dbo.TradingPlans')) " +
            "CREATE INDEX IX_TradingPlans_AnalysisHistoryId ON dbo.TradingPlans(AnalysisHistoryId); " +
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TradingPlans_SectorCodeAtCreation' AND object_id = OBJECT_ID('dbo.TradingPlans')) " +
            "CREATE INDEX IX_TradingPlans_SectorCodeAtCreation ON dbo.TradingPlans(SectorCodeAtCreation); " +
            "IF OBJECT_ID('dbo.TradingPlanEvents', 'U') IS NULL " +
            "BEGIN " +
            "CREATE TABLE dbo.TradingPlanEvents(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TradingPlanEvents PRIMARY KEY, " +
            "PlanId BIGINT NOT NULL, " +
            "Symbol NVARCHAR(32) NOT NULL, " +
            "EventType NVARCHAR(32) NOT NULL, " +
            "Severity NVARCHAR(16) NOT NULL, " +
            "Message NVARCHAR(MAX) NOT NULL, " +
            "SnapshotPrice DECIMAL(18,2) NULL, " +
            "MetadataJson NVARCHAR(MAX) NULL, " +
            "OccurredAt DATETIME2 NOT NULL" +
            "); " +
            "END; " +
            "IF COL_LENGTH('dbo.TradingPlanEvents','PlanId') IS NULL ALTER TABLE dbo.TradingPlanEvents ADD PlanId BIGINT NOT NULL CONSTRAINT DF_TradingPlanEvents_PlanId DEFAULT(0); " +
            "IF COL_LENGTH('dbo.TradingPlanEvents','VersionId') IS NULL ALTER TABLE dbo.TradingPlanEvents ADD VersionId BIGINT NULL; " +
            "IF COL_LENGTH('dbo.TradingPlanEvents','Symbol') IS NULL ALTER TABLE dbo.TradingPlanEvents ADD Symbol NVARCHAR(32) NOT NULL CONSTRAINT DF_TradingPlanEvents_Symbol DEFAULT(''); " +
            "IF COL_LENGTH('dbo.TradingPlanEvents','EventType') IS NULL ALTER TABLE dbo.TradingPlanEvents ADD EventType NVARCHAR(32) NOT NULL CONSTRAINT DF_TradingPlanEvents_EventType DEFAULT('VolumeDivergenceWarning'); " +
            "IF COL_LENGTH('dbo.TradingPlanEvents','Strategy') IS NULL ALTER TABLE dbo.TradingPlanEvents ADD Strategy NVARCHAR(64) NOT NULL CONSTRAINT DF_TradingPlanEvents_Strategy DEFAULT('runtime'); " +
            "IF COL_LENGTH('dbo.TradingPlanEvents','Reason') IS NULL ALTER TABLE dbo.TradingPlanEvents ADD Reason NVARCHAR(MAX) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlanEvents','CreatedAt') IS NULL ALTER TABLE dbo.TradingPlanEvents ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_TradingPlanEvents_CreatedAt DEFAULT(SYSUTCDATETIME()); " +
            "IF COL_LENGTH('dbo.TradingPlanEvents','Severity') IS NULL ALTER TABLE dbo.TradingPlanEvents ADD Severity NVARCHAR(16) NOT NULL CONSTRAINT DF_TradingPlanEvents_Severity DEFAULT('Warning'); " +
            "IF COL_LENGTH('dbo.TradingPlanEvents','Message') IS NULL ALTER TABLE dbo.TradingPlanEvents ADD Message NVARCHAR(MAX) NOT NULL CONSTRAINT DF_TradingPlanEvents_Message DEFAULT(''); " +
            "IF COL_LENGTH('dbo.TradingPlanEvents','SnapshotPrice') IS NULL ALTER TABLE dbo.TradingPlanEvents ADD SnapshotPrice DECIMAL(18,2) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlanEvents','MetadataJson') IS NULL ALTER TABLE dbo.TradingPlanEvents ADD MetadataJson NVARCHAR(MAX) NULL; " +
            "IF COL_LENGTH('dbo.TradingPlanEvents','OccurredAt') IS NULL ALTER TABLE dbo.TradingPlanEvents ADD OccurredAt DATETIME2 NOT NULL CONSTRAINT DF_TradingPlanEvents_OccurredAt DEFAULT(SYSUTCDATETIME()); " +
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TradingPlanEvents_PlanId_OccurredAt' AND object_id = OBJECT_ID('dbo.TradingPlanEvents')) CREATE INDEX IX_TradingPlanEvents_PlanId_OccurredAt ON dbo.TradingPlanEvents(PlanId, OccurredAt); " +
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TradingPlanEvents_Symbol_OccurredAt' AND object_id = OBJECT_ID('dbo.TradingPlanEvents')) CREATE INDEX IX_TradingPlanEvents_Symbol_OccurredAt ON dbo.TradingPlanEvents(Symbol, OccurredAt);",
            cancellationToken);
    }
}