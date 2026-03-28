using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public static class ResearchSessionSchemaInitializer
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

    private static async Task EnsureSqlServerAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'dbo.ResearchSessions', N'U') IS NULL " +
            "CREATE TABLE dbo.ResearchSessions(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ResearchSessions PRIMARY KEY, " +
            "SessionKey NVARCHAR(128) NOT NULL, " +
            "Symbol NVARCHAR(32) NOT NULL, " +
            "Name NVARCHAR(256) NOT NULL, " +
            "Status NVARCHAR(32) NOT NULL, " +
            "ActiveTurnId BIGINT NULL, " +
            "ActiveStage NVARCHAR(64) NULL, " +
            "LastUserIntent NVARCHAR(MAX) NULL, " +
            "DegradedFlagsJson NVARCHAR(MAX) NULL, " +
            "LatestRating NVARCHAR(32) NULL, " +
            "LatestDecisionHeadline NVARCHAR(512) NULL, " +
            "CreatedAt DATETIME2 NOT NULL, " +
            "UpdatedAt DATETIME2 NOT NULL);", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'dbo.ResearchTurns', N'U') IS NULL " +
            "CREATE TABLE dbo.ResearchTurns(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ResearchTurns PRIMARY KEY, " +
            "SessionId BIGINT NOT NULL, " +
            "TurnIndex INT NOT NULL, " +
            "UserPrompt NVARCHAR(MAX) NOT NULL, " +
            "Status NVARCHAR(32) NOT NULL, " +
            "ContinuationMode NVARCHAR(32) NOT NULL, " +
            "ReuseScope NVARCHAR(MAX) NULL, " +
            "RerunScope NVARCHAR(MAX) NULL, " +
            "ChangeSummary NVARCHAR(MAX) NULL, " +
            "StopReason NVARCHAR(MAX) NULL, " +
            "DegradedFlagsJson NVARCHAR(MAX) NULL, " +
            "RequestedAt DATETIME2 NOT NULL, " +
            "StartedAt DATETIME2 NULL, " +
            "CompletedAt DATETIME2 NULL);", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'dbo.ResearchStageSnapshots', N'U') IS NULL " +
            "CREATE TABLE dbo.ResearchStageSnapshots(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ResearchStageSnapshots PRIMARY KEY, " +
            "TurnId BIGINT NOT NULL, " +
            "StageType NVARCHAR(64) NOT NULL, " +
            "StageRunIndex INT NOT NULL, " +
            "ExecutionMode NVARCHAR(32) NOT NULL, " +
            "Status NVARCHAR(32) NOT NULL, " +
            "ActiveRoleIdsJson NVARCHAR(MAX) NULL, " +
            "Summary NVARCHAR(MAX) NULL, " +
            "DegradedFlagsJson NVARCHAR(MAX) NULL, " +
            "StopReason NVARCHAR(MAX) NULL, " +
            "StartedAt DATETIME2 NULL, " +
            "CompletedAt DATETIME2 NULL);", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'dbo.ResearchRoleStates', N'U') IS NULL " +
            "CREATE TABLE dbo.ResearchRoleStates(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ResearchRoleStates PRIMARY KEY, " +
            "StageId BIGINT NOT NULL, " +
            "RoleId NVARCHAR(64) NOT NULL, " +
            "RunIndex INT NOT NULL, " +
            "Status NVARCHAR(32) NOT NULL, " +
            "ToolPolicyClass NVARCHAR(64) NULL, " +
            "InputRefsJson NVARCHAR(MAX) NULL, " +
            "OutputRefsJson NVARCHAR(MAX) NULL, " +
            "OutputContentJson NVARCHAR(MAX) NULL, " +
            "DegradedFlagsJson NVARCHAR(MAX) NULL, " +
            "ErrorCode NVARCHAR(64) NULL, " +
            "ErrorMessage NVARCHAR(MAX) NULL, " +
            "LlmTraceId NVARCHAR(128) NULL, " +
            "StartedAt DATETIME2 NULL, " +
            "CompletedAt DATETIME2 NULL);", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'dbo.ResearchFeedItems', N'U') IS NULL " +
            "CREATE TABLE dbo.ResearchFeedItems(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ResearchFeedItems PRIMARY KEY, " +
            "TurnId BIGINT NOT NULL, " +
            "StageId BIGINT NULL, " +
            "RoleId NVARCHAR(64) NULL, " +
            "ItemType NVARCHAR(32) NOT NULL, " +
            "Content NVARCHAR(MAX) NOT NULL, " +
            "MetadataJson NVARCHAR(MAX) NULL, " +
            "TraceId NVARCHAR(128) NULL, " +
            "CreatedAt DATETIME2 NOT NULL);", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'dbo.ResearchReportSnapshots', N'U') IS NULL " +
            "CREATE TABLE dbo.ResearchReportSnapshots(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ResearchReportSnapshots PRIMARY KEY, " +
            "SessionId BIGINT NOT NULL, " +
            "TurnId BIGINT NOT NULL, " +
            "TriggeredByStageId BIGINT NULL, " +
            "VersionIndex INT NOT NULL, " +
            "IsFinal BIT NOT NULL, " +
            "ReportBlocksJson NVARCHAR(MAX) NULL, " +
            "CreatedAt DATETIME2 NOT NULL);", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'dbo.ResearchDecisionSnapshots', N'U') IS NULL " +
            "CREATE TABLE dbo.ResearchDecisionSnapshots(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ResearchDecisionSnapshots PRIMARY KEY, " +
            "SessionId BIGINT NOT NULL, " +
            "TurnId BIGINT NOT NULL, " +
            "SupersededByDecisionId BIGINT NULL, " +
            "Rating NVARCHAR(32) NULL, " +
            "Action NVARCHAR(64) NULL, " +
            "ExecutiveSummary NVARCHAR(MAX) NULL, " +
            "InvestmentThesis NVARCHAR(MAX) NULL, " +
            "FinalDecisionJson NVARCHAR(MAX) NULL, " +
            "RiskConsensus NVARCHAR(MAX) NULL, " +
            "DissentJson NVARCHAR(MAX) NULL, " +
            "NextActionsJson NVARCHAR(MAX) NULL, " +
            "InvalidationConditionsJson NVARCHAR(MAX) NULL, " +
            "Confidence DECIMAL(18,2) NULL, " +
            "CreatedAt DATETIME2 NOT NULL);", cancellationToken);

        // Indexes
        await EnsureIndexAsync(dbContext, "IX_ResearchSessions_SessionKey",
            "CREATE UNIQUE INDEX IX_ResearchSessions_SessionKey ON dbo.ResearchSessions(SessionKey);", cancellationToken);
        await EnsureIndexAsync(dbContext, "IX_ResearchSessions_Symbol_Status",
            "CREATE INDEX IX_ResearchSessions_Symbol_Status ON dbo.ResearchSessions(Symbol, Status);", cancellationToken);
        await EnsureIndexAsync(dbContext, "IX_ResearchSessions_Symbol_UpdatedAt",
            "CREATE INDEX IX_ResearchSessions_Symbol_UpdatedAt ON dbo.ResearchSessions(Symbol, UpdatedAt);", cancellationToken);
        await EnsureIndexAsync(dbContext, "IX_ResearchTurns_SessionId_TurnIndex",
            "CREATE UNIQUE INDEX IX_ResearchTurns_SessionId_TurnIndex ON dbo.ResearchTurns(SessionId, TurnIndex);", cancellationToken);
        await EnsureIndexAsync(dbContext, "IX_ResearchStageSnapshots_TurnId_StageType",
            "CREATE INDEX IX_ResearchStageSnapshots_TurnId_StageType ON dbo.ResearchStageSnapshots(TurnId, StageType, StageRunIndex);", cancellationToken);
        await EnsureIndexAsync(dbContext, "IX_ResearchRoleStates_StageId_RoleId",
            "CREATE INDEX IX_ResearchRoleStates_StageId_RoleId ON dbo.ResearchRoleStates(StageId, RoleId, RunIndex);", cancellationToken);
        await EnsureIndexAsync(dbContext, "IX_ResearchFeedItems_TurnId_CreatedAt",
            "CREATE INDEX IX_ResearchFeedItems_TurnId_CreatedAt ON dbo.ResearchFeedItems(TurnId, CreatedAt);", cancellationToken);
        await EnsureIndexAsync(dbContext, "IX_ResearchReportSnapshots_SessionId_TurnId",
            "CREATE INDEX IX_ResearchReportSnapshots_SessionId_TurnId ON dbo.ResearchReportSnapshots(SessionId, TurnId, VersionIndex);", cancellationToken);
        await EnsureIndexAsync(dbContext, "IX_ResearchDecisionSnapshots_SessionId_TurnId",
            "CREATE INDEX IX_ResearchDecisionSnapshots_SessionId_TurnId ON dbo.ResearchDecisionSnapshots(SessionId, TurnId);", cancellationToken);

        // R5 – Debate, Risk, Proposal tables
        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'dbo.ResearchDebateMessages', N'U') IS NULL " +
            "CREATE TABLE dbo.ResearchDebateMessages(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ResearchDebateMessages PRIMARY KEY, " +
            "SessionId BIGINT NOT NULL, " +
            "TurnId BIGINT NOT NULL, " +
            "StageId BIGINT NOT NULL, " +
            "Side NVARCHAR(20) NOT NULL, " +
            "RoleId NVARCHAR(64) NOT NULL, " +
            "RoundIndex INT NOT NULL, " +
            "Claim NVARCHAR(MAX) NOT NULL, " +
            "SupportingEvidenceRefsJson NVARCHAR(MAX) NULL, " +
            "CounterTargetRole NVARCHAR(64) NULL, " +
            "CounterPointsJson NVARCHAR(MAX) NULL, " +
            "OpenQuestionsJson NVARCHAR(MAX) NULL, " +
            "LlmTraceId NVARCHAR(256) NULL, " +
            "CreatedAt DATETIME2 NOT NULL);", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'dbo.ResearchManagerVerdicts', N'U') IS NULL " +
            "CREATE TABLE dbo.ResearchManagerVerdicts(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ResearchManagerVerdicts PRIMARY KEY, " +
            "SessionId BIGINT NOT NULL, " +
            "TurnId BIGINT NOT NULL, " +
            "StageId BIGINT NOT NULL, " +
            "RoundIndex INT NOT NULL, " +
            "AdoptedBullPointsJson NVARCHAR(MAX) NULL, " +
            "AdoptedBearPointsJson NVARCHAR(MAX) NULL, " +
            "ShelvedDisputesJson NVARCHAR(MAX) NULL, " +
            "ResearchConclusion NVARCHAR(MAX) NULL, " +
            "InvestmentPlanDraftJson NVARCHAR(MAX) NULL, " +
            "IsConverged BIT NOT NULL DEFAULT 0, " +
            "LlmTraceId NVARCHAR(256) NULL, " +
            "CreatedAt DATETIME2 NOT NULL);", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'dbo.ResearchTraderProposals', N'U') IS NULL " +
            "CREATE TABLE dbo.ResearchTraderProposals(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ResearchTraderProposals PRIMARY KEY, " +
            "SessionId BIGINT NOT NULL, " +
            "TurnId BIGINT NOT NULL, " +
            "StageId BIGINT NOT NULL, " +
            "Version INT NOT NULL, " +
            "Status NVARCHAR(20) NOT NULL, " +
            "Direction NVARCHAR(64) NULL, " +
            "EntryPlanJson NVARCHAR(MAX) NULL, " +
            "ExitPlanJson NVARCHAR(MAX) NULL, " +
            "PositionSizingJson NVARCHAR(MAX) NULL, " +
            "Rationale NVARCHAR(MAX) NULL, " +
            "SupersededByProposalId BIGINT NULL, " +
            "LlmTraceId NVARCHAR(256) NULL, " +
            "CreatedAt DATETIME2 NOT NULL);", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'dbo.ResearchRiskAssessments', N'U') IS NULL " +
            "CREATE TABLE dbo.ResearchRiskAssessments(" +
            "Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ResearchRiskAssessments PRIMARY KEY, " +
            "SessionId BIGINT NOT NULL, " +
            "TurnId BIGINT NOT NULL, " +
            "StageId BIGINT NOT NULL, " +
            "RoleId NVARCHAR(64) NOT NULL, " +
            "Tier NVARCHAR(20) NOT NULL, " +
            "RoundIndex INT NOT NULL, " +
            "RiskLimitsJson NVARCHAR(MAX) NULL, " +
            "InvalidationsJson NVARCHAR(MAX) NULL, " +
            "ProposalAssessment NVARCHAR(MAX) NULL, " +
            "AnalysisContent NVARCHAR(MAX) NULL, " +
            "ResponseToArtifactId BIGINT NULL, " +
            "LlmTraceId NVARCHAR(256) NULL, " +
            "CreatedAt DATETIME2 NOT NULL);", cancellationToken);

        await EnsureIndexAsync(dbContext, "IX_ResearchDebateMessages_Session_Turn_Stage_Round",
            "CREATE INDEX IX_ResearchDebateMessages_Session_Turn_Stage_Round ON dbo.ResearchDebateMessages(SessionId, TurnId, StageId, RoundIndex);", cancellationToken);
        await EnsureIndexAsync(dbContext, "IX_ResearchManagerVerdicts_Session_Turn_Stage",
            "CREATE INDEX IX_ResearchManagerVerdicts_Session_Turn_Stage ON dbo.ResearchManagerVerdicts(SessionId, TurnId, StageId);", cancellationToken);
        await EnsureIndexAsync(dbContext, "IX_ResearchTraderProposals_Session_Turn_Version",
            "CREATE INDEX IX_ResearchTraderProposals_Session_Turn_Version ON dbo.ResearchTraderProposals(SessionId, TurnId, Version);", cancellationToken);
        await EnsureIndexAsync(dbContext, "IX_ResearchRiskAssessments_Session_Turn_Role_Round",
            "CREATE INDEX IX_ResearchRiskAssessments_Session_Turn_Role_Round ON dbo.ResearchRiskAssessments(SessionId, TurnId, StageId, RoleId, RoundIndex);", cancellationToken);

        // ── R6: Report blocks ────────────────────────────────────────
        await dbContext.Database.ExecuteSqlRawAsync(@"
            IF OBJECT_ID(N'dbo.ResearchReportBlocks', N'U') IS NULL
            CREATE TABLE dbo.ResearchReportBlocks (
                Id              BIGINT          IDENTITY(1,1) PRIMARY KEY,
                SessionId       BIGINT          NOT NULL REFERENCES dbo.ResearchSessions(Id) ON DELETE CASCADE,
                TurnId          BIGINT          NOT NULL,
                BlockType       NVARCHAR(30)    NOT NULL,
                VersionIndex    INT             NOT NULL DEFAULT 0,
                Headline        NVARCHAR(500)   NULL,
                Summary         NVARCHAR(MAX)   NULL,
                KeyPointsJson           NVARCHAR(MAX) NULL,
                EvidenceRefsJson        NVARCHAR(MAX) NULL,
                CounterEvidenceRefsJson NVARCHAR(MAX) NULL,
                DisagreementsJson       NVARCHAR(MAX) NULL,
                RiskLimitsJson          NVARCHAR(MAX) NULL,
                InvalidationsJson       NVARCHAR(MAX) NULL,
                RecommendedActionsJson  NVARCHAR(MAX) NULL,
                Status          NVARCHAR(20)    NOT NULL DEFAULT 'Pending',
                DegradedFlagsJson       NVARCHAR(MAX) NULL,
                MissingEvidence         NVARCHAR(MAX) NULL,
                ConfidenceImpact        NVARCHAR(50)  NULL,
                SourceStageType         NVARCHAR(50)  NULL,
                SourceArtifactId        BIGINT        NULL,
                CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
                UpdatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
            );", cancellationToken);

        await EnsureIndexAsync(dbContext, "UQ_ResearchReportBlocks_Turn_Block_Version",
            "CREATE UNIQUE INDEX UQ_ResearchReportBlocks_Turn_Block_Version ON dbo.ResearchReportBlocks(TurnId, BlockType, VersionIndex);", cancellationToken);

        // R6: Add missing columns to ResearchDecisionSnapshots
        await dbContext.Database.ExecuteSqlRawAsync(@"
            IF COL_LENGTH('dbo.ResearchDecisionSnapshots','SupportingEvidenceJson') IS NULL
                ALTER TABLE dbo.ResearchDecisionSnapshots ADD SupportingEvidenceJson NVARCHAR(MAX) NULL;
            IF COL_LENGTH('dbo.ResearchDecisionSnapshots','CounterEvidenceJson') IS NULL
                ALTER TABLE dbo.ResearchDecisionSnapshots ADD CounterEvidenceJson NVARCHAR(MAX) NULL;
            IF COL_LENGTH('dbo.ResearchDecisionSnapshots','ConfidenceExplanation') IS NULL
                ALTER TABLE dbo.ResearchDecisionSnapshots ADD ConfidenceExplanation NVARCHAR(MAX) NULL;", cancellationToken);
    }

    private static async Task EnsureIndexAsync(AppDbContext dbContext, string indexName, string createSql, CancellationToken ct)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{indexName}') {createSql}", ct);
    }

    private static async Task EnsureSqliteAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        // ── R1-R4 core tables ────────────────────────────────────────
        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ResearchSessions (
                Id                      INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionKey              TEXT    NOT NULL,
                Symbol                  TEXT    NOT NULL,
                Name                    TEXT    NOT NULL,
                Status                  TEXT    NOT NULL,
                ActiveTurnId            INTEGER NULL,
                ActiveStage             TEXT    NULL,
                LastUserIntent          TEXT    NULL,
                DegradedFlagsJson       TEXT    NULL,
                LatestRating            TEXT    NULL,
                LatestDecisionHeadline  TEXT    NULL,
                CreatedAt               TEXT    NOT NULL,
                UpdatedAt               TEXT    NOT NULL
            );", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ResearchTurns (
                Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId           INTEGER NOT NULL,
                TurnIndex           INTEGER NOT NULL,
                UserPrompt          TEXT    NOT NULL,
                Status              TEXT    NOT NULL,
                ContinuationMode    TEXT    NOT NULL,
                ReuseScope          TEXT    NULL,
                RerunScope          TEXT    NULL,
                ChangeSummary       TEXT    NULL,
                StopReason          TEXT    NULL,
                DegradedFlagsJson   TEXT    NULL,
                RequestedAt         TEXT    NOT NULL,
                StartedAt           TEXT    NULL,
                CompletedAt         TEXT    NULL
            );", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ResearchStageSnapshots (
                Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                TurnId              INTEGER NOT NULL,
                StageType           TEXT    NOT NULL,
                StageRunIndex       INTEGER NOT NULL,
                ExecutionMode       TEXT    NOT NULL,
                Status              TEXT    NOT NULL,
                ActiveRoleIdsJson   TEXT    NULL,
                Summary             TEXT    NULL,
                DegradedFlagsJson   TEXT    NULL,
                StopReason          TEXT    NULL,
                StartedAt           TEXT    NULL,
                CompletedAt         TEXT    NULL
            );", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ResearchRoleStates (
                Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                StageId             INTEGER NOT NULL,
                RoleId              TEXT    NOT NULL,
                RunIndex            INTEGER NOT NULL,
                Status              TEXT    NOT NULL,
                ToolPolicyClass     TEXT    NULL,
                InputRefsJson       TEXT    NULL,
                OutputRefsJson      TEXT    NULL,
                OutputContentJson   TEXT    NULL,
                DegradedFlagsJson   TEXT    NULL,
                ErrorCode           TEXT    NULL,
                ErrorMessage        TEXT    NULL,
                LlmTraceId          TEXT    NULL,
                StartedAt           TEXT    NULL,
                CompletedAt         TEXT    NULL
            );", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ResearchFeedItems (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                TurnId          INTEGER NOT NULL,
                StageId         INTEGER NULL,
                RoleId          TEXT    NULL,
                ItemType        TEXT    NOT NULL,
                Content         TEXT    NOT NULL,
                MetadataJson    TEXT    NULL,
                TraceId         TEXT    NULL,
                CreatedAt       TEXT    NOT NULL
            );", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ResearchReportSnapshots (
                Id                      INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId               INTEGER NOT NULL,
                TurnId                  INTEGER NOT NULL,
                TriggeredByStageId      INTEGER NULL,
                VersionIndex            INTEGER NOT NULL,
                IsFinal                 INTEGER NOT NULL,
                ReportBlocksJson        TEXT    NULL,
                CreatedAt               TEXT    NOT NULL
            );", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ResearchDecisionSnapshots (
                Id                          INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId                   INTEGER NOT NULL,
                TurnId                      INTEGER NOT NULL,
                SupersededByDecisionId      INTEGER NULL,
                Rating                      TEXT    NULL,
                Action                      TEXT    NULL,
                ExecutiveSummary            TEXT    NULL,
                InvestmentThesis            TEXT    NULL,
                FinalDecisionJson           TEXT    NULL,
                RiskConsensus               TEXT    NULL,
                DissentJson                 TEXT    NULL,
                NextActionsJson             TEXT    NULL,
                InvalidationConditionsJson  TEXT    NULL,
                SupportingEvidenceJson      TEXT    NULL,
                CounterEvidenceJson         TEXT    NULL,
                ConfidenceExplanation       TEXT    NULL,
                Confidence                  REAL    NULL,
                CreatedAt                   TEXT    NOT NULL
            );", cancellationToken);

        // Indexes for R1-R4 tables
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_ResearchSessions_SessionKey ON ResearchSessions(SessionKey);", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ResearchSessions_Symbol_Status ON ResearchSessions(Symbol, Status);", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ResearchSessions_Symbol_UpdatedAt ON ResearchSessions(Symbol, UpdatedAt);", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_ResearchTurns_SessionId_TurnIndex ON ResearchTurns(SessionId, TurnIndex);", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ResearchStageSnapshots_TurnId_StageType ON ResearchStageSnapshots(TurnId, StageType, StageRunIndex);", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ResearchRoleStates_StageId_RoleId ON ResearchRoleStates(StageId, RoleId, RunIndex);", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ResearchFeedItems_TurnId_CreatedAt ON ResearchFeedItems(TurnId, CreatedAt);", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ResearchReportSnapshots_SessionId_TurnId ON ResearchReportSnapshots(SessionId, TurnId, VersionIndex);", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ResearchDecisionSnapshots_SessionId_TurnId ON ResearchDecisionSnapshots(SessionId, TurnId);", cancellationToken);

        // ── R5: Debate, Risk, Proposal tables ────────────────────────
        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ResearchDebateMessages (
                Id                          INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId                   INTEGER NOT NULL,
                TurnId                      INTEGER NOT NULL,
                StageId                     INTEGER NOT NULL,
                Side                        TEXT    NOT NULL,
                RoleId                      TEXT    NOT NULL,
                RoundIndex                  INTEGER NOT NULL,
                Claim                       TEXT    NOT NULL,
                SupportingEvidenceRefsJson  TEXT    NULL,
                CounterTargetRole           TEXT    NULL,
                CounterPointsJson           TEXT    NULL,
                OpenQuestionsJson           TEXT    NULL,
                LlmTraceId                  TEXT    NULL,
                CreatedAt                   TEXT    NOT NULL
            );", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ResearchManagerVerdicts (
                Id                      INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId               INTEGER NOT NULL,
                TurnId                  INTEGER NOT NULL,
                StageId                 INTEGER NOT NULL,
                RoundIndex              INTEGER NOT NULL,
                AdoptedBullPointsJson   TEXT    NULL,
                AdoptedBearPointsJson   TEXT    NULL,
                ShelvedDisputesJson     TEXT    NULL,
                ResearchConclusion      TEXT    NULL,
                InvestmentPlanDraftJson TEXT    NULL,
                IsConverged             INTEGER NOT NULL DEFAULT 0,
                LlmTraceId              TEXT    NULL,
                CreatedAt               TEXT    NOT NULL
            );", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ResearchTraderProposals (
                Id                          INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId                   INTEGER NOT NULL,
                TurnId                      INTEGER NOT NULL,
                StageId                     INTEGER NOT NULL,
                Version                     INTEGER NOT NULL,
                Status                      TEXT    NOT NULL,
                Direction                   TEXT    NULL,
                EntryPlanJson               TEXT    NULL,
                ExitPlanJson                TEXT    NULL,
                PositionSizingJson          TEXT    NULL,
                Rationale                   TEXT    NULL,
                SupersededByProposalId      INTEGER NULL,
                LlmTraceId                  TEXT    NULL,
                CreatedAt                   TEXT    NOT NULL
            );", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ResearchRiskAssessments (
                Id                      INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId               INTEGER NOT NULL,
                TurnId                  INTEGER NOT NULL,
                StageId                 INTEGER NOT NULL,
                RoleId                  TEXT    NOT NULL,
                Tier                    TEXT    NOT NULL,
                RoundIndex              INTEGER NOT NULL,
                RiskLimitsJson          TEXT    NULL,
                InvalidationsJson       TEXT    NULL,
                ProposalAssessment      TEXT    NULL,
                AnalysisContent         TEXT    NULL,
                ResponseToArtifactId    INTEGER NULL,
                LlmTraceId              TEXT    NULL,
                CreatedAt               TEXT    NOT NULL
            );", cancellationToken);

        // R5 indexes
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ResearchDebateMessages_Session_Turn_Stage_Round ON ResearchDebateMessages(SessionId, TurnId, StageId, RoundIndex);", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ResearchManagerVerdicts_Session_Turn_Stage ON ResearchManagerVerdicts(SessionId, TurnId, StageId);", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ResearchTraderProposals_Session_Turn_Version ON ResearchTraderProposals(SessionId, TurnId, Version);", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ResearchRiskAssessments_Session_Turn_Role_Round ON ResearchRiskAssessments(SessionId, TurnId, StageId, RoleId, RoundIndex);", cancellationToken);

        // ── R6: Report blocks ────────────────────────────────────────
        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ResearchReportBlocks (
                Id                          INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId                   INTEGER NOT NULL,
                TurnId                      INTEGER NOT NULL,
                BlockType                   TEXT    NOT NULL,
                VersionIndex                INTEGER NOT NULL DEFAULT 0,
                Headline                    TEXT    NULL,
                Summary                     TEXT    NULL,
                KeyPointsJson               TEXT    NULL,
                EvidenceRefsJson            TEXT    NULL,
                CounterEvidenceRefsJson     TEXT    NULL,
                DisagreementsJson           TEXT    NULL,
                RiskLimitsJson              TEXT    NULL,
                InvalidationsJson           TEXT    NULL,
                RecommendedActionsJson      TEXT    NULL,
                Status                      TEXT    NOT NULL DEFAULT 'Pending',
                DegradedFlagsJson           TEXT    NULL,
                MissingEvidence             TEXT    NULL,
                ConfidenceImpact            TEXT    NULL,
                SourceStageType             TEXT    NULL,
                SourceArtifactId            INTEGER NULL,
                CreatedAt                   TEXT    NOT NULL,
                UpdatedAt                   TEXT    NOT NULL
            );", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS UQ_ResearchReportBlocks_Turn_Block_Version ON ResearchReportBlocks(TurnId, BlockType, VersionIndex);", cancellationToken);

        // R6: Ensure columns added after initial schema exist on ResearchDecisionSnapshots
        await EnsureSqliteColumnAsync(dbContext, "ResearchDecisionSnapshots", "SupportingEvidenceJson", "TEXT", cancellationToken);
        await EnsureSqliteColumnAsync(dbContext, "ResearchDecisionSnapshots", "CounterEvidenceJson", "TEXT", cancellationToken);
        await EnsureSqliteColumnAsync(dbContext, "ResearchDecisionSnapshots", "ConfidenceExplanation", "TEXT", cancellationToken);
    }

    private static async Task EnsureSqliteColumnAsync(AppDbContext dbContext, string table, string column, string sqlType, CancellationToken ct)
    {
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {sqlType} NULL;", ct);
        }
        catch
        {
            // Column already exists — safe to ignore
        }
    }
}
