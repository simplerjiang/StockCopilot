using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using System.Runtime.CompilerServices;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class TradingPlanServicesTests
{
    private static readonly ConditionalWeakTable<AppDbContext, DbContextOptions<AppDbContext>> ContextOptionsMap = new();

    [Fact]
    public async Task BuildDraftAsync_MapsCommanderFieldsAndDeterministicPrices()
    {
        await using var dbContext = CreateDbContext();
        var history = new StockAgentAnalysisHistory
        {
            Symbol = "sz000021",
            Name = "深科技",
            Interval = "day",
            ResultJson = """
            {
              "symbol": "sz000021",
              "name": "深科技",
              "agents": [
                {
                  "agentId": "stock_news",
                  "success": true,
                  "data": {
                    "summary": "公告与个股新闻已核对。"
                  }
                },
                {
                  "agentId": "sector_news",
                  "success": true,
                  "data": {
                    "summary": "板块消息中性偏多。"
                  }
                },
                {
                  "agentId": "financial_analysis",
                  "success": true,
                  "data": {
                    "summary": "财务结构稳定。"
                  }
                },
                {
                  "agentId": "trend_analysis",
                  "success": true,
                  "data": {
                    "forecast": [
                      { "label": "T+5", "price": 13.8 }
                    ]
                  }
                },
                {
                  "agentId": "commander",
                  "success": true,
                  "data": {
                    "summary": "偏多",
                    "analysis_opinion": "等待放量突破后再执行。",
                    "trigger_conditions": "突破前高并放量确认",
                    "invalid_conditions": "跌破支撑位",
                    "risk_warning": "单笔亏损不超过 2%",
                    "riskLimits": ["单笔亏损不超过 2%", "总仓位不超过 50%"],
                    "direction": "Long",
                    "chart": {
                      "breakoutPrice": 12.6,
                      "supportPrice": 11.9,
                      "stopLossPrice": 11.5,
                      "takeProfitPrice": 13.4,
                      "targetPrice": 14.2
                    }
                  }
                }
              ]
            }
            """,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.StockAgentAnalysisHistories.Add(history);
        dbContext.StockQuoteSnapshots.Add(new StockQuoteSnapshot
        {
          Symbol = "sz000021",
          Name = "深科技",
          Price = 12.5m,
          Change = 0.1m,
          ChangePercent = 0.8m,
          SectorName = "机器人",
          Timestamp = DateTime.UtcNow
        });
        dbContext.MarketSentimentSnapshots.Add(new MarketSentimentSnapshot
        {
          TradingDate = new DateTime(2026, 3, 15),
          SnapshotTime = new DateTime(2026, 3, 15, 7, 0, 0, DateTimeKind.Utc),
          SessionPhase = "盘后",
          StageLabel = "主升",
          StageLabelV2 = "主升",
          StageScore = 78m,
          StageConfidence = 82m,
          CreatedAt = DateTime.UtcNow,
          SourceTag = "test"
        });
        dbContext.SectorRotationSnapshots.Add(new SectorRotationSnapshot
        {
          TradingDate = new DateTime(2026, 3, 15),
          SnapshotTime = new DateTime(2026, 3, 15, 7, 0, 0, DateTimeKind.Utc),
          BoardType = "concept",
          SectorCode = "BK001",
          SectorName = "机器人",
          RankNo = 1,
          StrengthScore = 82m,
          StrengthAvg5d = 76m,
          StrengthAvg10d = 72m,
          DiffusionRate = 80m,
          MainlineScore = 78m,
          IsMainline = true,
          NewsSentiment = "利好",
          SourceTag = "test",
          CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var historyService = new StockAgentHistoryService(dbContext);
        var service = new TradingPlanDraftService(historyService, CreateMarketContextService(dbContext));

        var draft = await service.BuildDraftAsync("sz000021", history.Id);

        Assert.Equal("sz000021", draft.Symbol);
        Assert.Equal("深科技", draft.Name);
        Assert.Equal("Long", draft.Direction);
        Assert.Equal("Pending", draft.Status);
        Assert.Equal(12.6m, draft.TriggerPrice);
        Assert.Equal(11.9m, draft.InvalidPrice);
        Assert.Equal(11.5m, draft.StopLossPrice);
        Assert.Equal(13.4m, draft.TakeProfitPrice);
        Assert.Equal(14.2m, draft.TargetPrice);
        Assert.Equal("等待放量突破后再执行。", draft.AnalysisSummary);
        Assert.Equal("突破前高并放量确认", draft.ExpectedCatalyst);
        Assert.Equal("跌破支撑位", draft.InvalidConditions);
        Assert.Equal("单笔亏损不超过 2%；总仓位不超过 50%", draft.RiskLimits);
        Assert.Equal(history.Id, draft.AnalysisHistoryId);
        Assert.NotNull(draft.MarketContext);
        Assert.Equal("主升", draft.MarketContext!.StageLabel);
        Assert.True(draft.MarketContext.IsMainlineAligned);
    }

    [Fact]
    public async Task GetLatestAsync_ReturnsAlignedMainlineContext()
    {
        await using var dbContext = CreateDbContext();
        dbContext.StockQuoteSnapshots.Add(new StockQuoteSnapshot
        {
          Symbol = "sz000021",
          Name = "深科技",
          Price = 12.5m,
          Change = 0.1m,
          ChangePercent = 0.8m,
          SectorName = "机器人",
          Timestamp = DateTime.UtcNow
        });
        dbContext.MarketSentimentSnapshots.Add(new MarketSentimentSnapshot
        {
          TradingDate = new DateTime(2026, 3, 15),
          SnapshotTime = new DateTime(2026, 3, 15, 7, 0, 0, DateTimeKind.Utc),
          SessionPhase = "盘后",
          StageLabel = "主升",
          StageLabelV2 = "主升",
          StageScore = 78m,
          StageConfidence = 82m,
          CreatedAt = DateTime.UtcNow,
          SourceTag = "test"
        });
        dbContext.SectorRotationSnapshots.AddRange(
            new SectorRotationSnapshot
            {
              TradingDate = new DateTime(2026, 3, 15),
              SnapshotTime = new DateTime(2026, 3, 15, 7, 0, 0, DateTimeKind.Utc),
              BoardType = "concept",
              SectorCode = "BK001",
              SectorName = "机器人",
              RankNo = 1,
              StrengthScore = 82m,
              StrengthAvg5d = 76m,
              StrengthAvg10d = 72m,
              DiffusionRate = 80m,
              MainlineScore = 78m,
              IsMainline = true,
              NewsSentiment = "利好",
              SourceTag = "test",
              CreatedAt = DateTime.UtcNow
            },
            new SectorRotationSnapshot
            {
              TradingDate = new DateTime(2026, 3, 15),
              SnapshotTime = new DateTime(2026, 3, 15, 7, 0, 0, DateTimeKind.Utc),
              BoardType = "concept",
              SectorCode = "BK002",
              SectorName = "半导体",
              RankNo = 2,
              StrengthScore = 75m,
              StrengthAvg5d = 70m,
              StrengthAvg10d = 68m,
              DiffusionRate = 66m,
              MainlineScore = 70m,
              IsMainline = false,
              NewsSentiment = "中性",
              SourceTag = "test",
              CreatedAt = DateTime.UtcNow
            });
        await dbContext.SaveChangesAsync();

        var result = await CreateMarketContextService(dbContext).GetLatestAsync("000021");

        Assert.NotNull(result);
        Assert.Equal("主升", result!.StageLabel);
        Assert.Equal(82m, result.StageConfidence);
        Assert.Equal("机器人", result.StockSectorName);
        Assert.Equal("机器人", result.MainlineSectorName);
        Assert.Equal("BK001", result.SectorCode);
        Assert.Equal(78m, result.MainlineScore);
        Assert.Equal("积极执行", result.ExecutionFrequencyLabel);
        Assert.False(result.CounterTrendWarning);
        Assert.True(result.IsMainlineAligned);
    }

    [Fact]
    public async Task BuildDraftAsync_DoesNotInferPricesFromNaturalLanguageOnly()
    {
        await using var dbContext = CreateDbContext();
        var history = new StockAgentAnalysisHistory
        {
            Symbol = "sz000001",
            Name = "平安银行",
            Interval = "day",
            ResultJson = """
            {
              "symbol": "sz000001",
              "name": "平安银行",
              "agents": [
                {
                  "agentId": "stock_news",
                  "success": true,
                  "data": {
                    "summary": "公告未见新增风险。"
                  }
                },
                {
                  "agentId": "sector_news",
                  "success": true,
                  "data": {
                    "summary": "银行板块消息中性。"
                  }
                },
                {
                  "agentId": "commander",
                  "success": true,
                  "data": {
                    "summary": "观察",
                    "analysis_opinion": "若站上 12.60 可继续观察。",
                    "trigger_conditions": "放量突破 12.60",
                    "invalid_conditions": "跌破 11.90",
                    "risk_warning": "跌破 11.90 即离场"
                    }
                  },
                {
                  "agentId": "financial_analysis",
                  "success": true,
                  "data": {
                    "metrics": {
                      "institutionTargetPrice": 13.8
                    }
                  }
                },
                {
                  "agentId": "trend_analysis",
                  "success": true,
                  "data": {
                    "forecast": [
                      { "label": "T+1", "price": 13.2 },
                      { "label": "T+5", "price": 13.6 }
                    ]
                  }
                }
              ]
            }
            """,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.StockAgentAnalysisHistories.Add(history);
        dbContext.StockCompanyProfiles.Add(new StockCompanyProfile
        {
          Symbol = "sz000001",
          Name = "平安银行",
          SectorName = "银行",
          UpdatedAt = DateTime.UtcNow
        });
        dbContext.MarketSentimentSnapshots.Add(new MarketSentimentSnapshot
        {
          TradingDate = new DateTime(2026, 3, 15),
          SnapshotTime = new DateTime(2026, 3, 15, 7, 0, 0, DateTimeKind.Utc),
          SessionPhase = "盘后",
          StageLabel = "混沌",
          StageLabelV2 = "混沌",
          StageScore = 48m,
          StageConfidence = 58m,
          SourceTag = "test",
          CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = new TradingPlanDraftService(new StockAgentHistoryService(dbContext), CreateMarketContextService(dbContext));

        var draft = await service.BuildDraftAsync("sz000001", history.Id);

        Assert.Null(draft.TriggerPrice);
        Assert.Null(draft.InvalidPrice);
        Assert.Null(draft.StopLossPrice);
        Assert.Equal(13.8m, draft.TakeProfitPrice);
        Assert.Equal(13.8m, draft.TargetPrice);
        Assert.Equal("Long", draft.Direction);
        Assert.NotNull(draft.MarketContext);
    }

    [Fact]
    public async Task BuildDraftAsync_WhenCurrentPriceExists_SkipsIllogicalLongTakeProfit()
    {
        await using var dbContext = CreateDbContext();
        var history = new StockAgentAnalysisHistory
        {
            Symbol = "sz000021",
            Name = "深科技",
            Interval = "day",
            ResultJson = """
            {
              "symbol": "sz000021",
              "name": "深科技",
              "agents": [
                {
                  "agentId": "stock_news",
                  "success": true,
                  "data": {
                    "summary": "公告正常。"
                  }
                },
                {
                  "agentId": "sector_news",
                  "success": true,
                  "data": {
                    "summary": "半导体板块偏强。"
                  }
                },
                {
                  "agentId": "financial_analysis",
                  "success": true,
                  "data": {
                    "summary": "估值合理。"
                  }
                },
                {
                  "agentId": "commander",
                  "success": true,
                  "data": {
                    "summary": "偏多",
                    "direction": "Long",
                    "metrics": {
                      "price": 31.1
                    },
                    "chart": {
                      "takeProfitPrice": 13.4,
                      "targetPrice": 14.2
                    }
                  }
                },
                {
                  "agentId": "trend_analysis",
                  "success": true,
                  "data": {
                    "forecast": [
                      { "label": "T+5", "price": 34.8 }
                    ]
                  }
                }
              ]
            }
            """,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.StockAgentAnalysisHistories.Add(history);
        dbContext.StockQuoteSnapshots.Add(new StockQuoteSnapshot
        {
          Symbol = "sz000021",
          Name = "深科技",
          Price = 31.1m,
          Change = 0.1m,
          ChangePercent = 0.3m,
          SectorName = "半导体",
          Timestamp = DateTime.UtcNow
        });
        dbContext.MarketSentimentSnapshots.Add(new MarketSentimentSnapshot
        {
          TradingDate = new DateTime(2026, 3, 17),
          SnapshotTime = new DateTime(2026, 3, 17, 7, 0, 0, DateTimeKind.Utc),
          SessionPhase = "盘后",
          StageLabel = "主升",
          StageLabelV2 = "主升",
          StageScore = 75m,
          StageConfidence = 80m,
          SourceTag = "test",
          CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = new TradingPlanDraftService(new StockAgentHistoryService(dbContext), CreateMarketContextService(dbContext));

        var draft = await service.BuildDraftAsync("sz000021", history.Id);

        Assert.Equal(34.8m, draft.TargetPrice);
        Assert.Equal(34.8m, draft.TakeProfitPrice);
    }

    [Fact]
    public async Task BuildDraftAsync_RejectsIncompleteCommanderHistory()
    {
        await using var dbContext = CreateDbContext();
        var history = new StockAgentAnalysisHistory
        {
            Symbol = "sh600000",
            Name = "浦发银行",
            Interval = "day",
            ResultJson = """
            {
              "symbol": "sh600000",
              "agents": [
                {
                  "agentId": "stock_news",
                  "success": true,
                  "data": {
                    "summary": "只有个股新闻。"
                  }
                }
              ]
            }
            """,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.StockAgentAnalysisHistories.Add(history);
        await dbContext.SaveChangesAsync();

        var service = new TradingPlanDraftService(new StockAgentHistoryService(dbContext), CreateMarketContextService(dbContext));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.BuildDraftAsync("sh600000", history.Id));

        Assert.Contains("分析历史不完整", error.Message);
        Assert.Contains("指挥Agent", error.Message);
    }

    [Fact]
    public async Task CreateAsync_SavesPendingPlanAndEnsuresWatchlist()
    {
        await using var dbContext = CreateDbContext();
        var history = new StockAgentAnalysisHistory
        {
            Symbol = "sh600000",
            Name = "浦发银行",
            Interval = "day",
            ResultJson = "{" +
                         "\"symbol\":\"sh600000\",\"agents\":[{\"agentId\":\"commander\",\"data\":{\"summary\":\"ok\"}}]}"
                         ,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.StockAgentAnalysisHistories.Add(history);
        dbContext.StockQuoteSnapshots.Add(new StockQuoteSnapshot
        {
          Symbol = "sh600000",
          Name = "浦发银行",
          Price = 10.2m,
          Change = 0.2m,
          ChangePercent = 1.5m,
          SectorName = "银行",
          Timestamp = DateTime.UtcNow
        });
        dbContext.MarketSentimentSnapshots.Add(new MarketSentimentSnapshot
        {
          TradingDate = new DateTime(2026, 3, 15),
          SnapshotTime = new DateTime(2026, 3, 15, 7, 0, 0, DateTimeKind.Utc),
          SessionPhase = "盘后",
          StageLabel = "分歧",
          StageLabelV2 = "主升",
          StageScore = 66m,
          StageConfidence = 76m,
          SourceTag = "test",
          CreatedAt = DateTime.UtcNow
        });
        dbContext.SectorRotationSnapshots.Add(new SectorRotationSnapshot
        {
          TradingDate = new DateTime(2026, 3, 15),
          SnapshotTime = new DateTime(2026, 3, 15, 7, 0, 0, DateTimeKind.Utc),
          BoardType = "industry",
          SectorCode = "BKYH",
          SectorName = "银行",
          RankNo = 1,
          StrengthScore = 70m,
          StrengthAvg5d = 68m,
          StrengthAvg10d = 65m,
          DiffusionRate = 72m,
          MainlineScore = 74m,
          IsMainline = true,
          NewsSentiment = "利好",
          SourceTag = "test",
          CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var watchlistService = new ActiveWatchlistService(dbContext);
        var service = new TradingPlanService(dbContext, watchlistService, CreateMarketContextService(dbContext));

        var result = await service.CreateAsync(new TradingPlanCreateDto(
            "sh600000",
            "浦发银行",
            "Long",
            10.2m,
            9.6m,
            9.4m,
            10.9m,
            11.5m,
            "等待放量突破",
            "跌破 9.6",
            "单笔亏损不超过 2%",
            "偏多",
            history.Id,
            "commander",
            "手工确认"));

        Assert.True(result.WatchlistEnsured);
        Assert.Equal(TradingPlanStatus.Pending, result.Plan.Status);

        var savedPlan = await dbContext.TradingPlans.FirstAsync();
        var watchlist = await dbContext.ActiveWatchlists.FirstAsync();

        Assert.Equal(savedPlan.Id, result.Plan.Id);
        Assert.False(string.IsNullOrWhiteSpace(savedPlan.PlanKey));
        Assert.Equal("浦发银行", savedPlan.Title);
        Assert.Equal("sh600000", watchlist.Symbol);
        Assert.Equal("trading-plan", watchlist.SourceTag);
        Assert.Equal($"plan:{savedPlan.Id}", watchlist.Note);
        Assert.Equal(history.Id, savedPlan.AnalysisHistoryId);
        Assert.Equal("commander", savedPlan.SourceAgent);
        Assert.Equal(10.9m, savedPlan.TakeProfitPrice);
        Assert.Equal(11.5m, savedPlan.TargetPrice);
        Assert.Equal("主升", savedPlan.MarketStageLabelAtCreation);
        Assert.Equal("银行", savedPlan.SectorNameAtCreation);
        Assert.Equal("BKYH", savedPlan.SectorCodeAtCreation);
        Assert.Equal("积极执行", savedPlan.ExecutionFrequencyLabel);
    }

    [Fact]
    public async Task CreateAsync_PersistsScenarioStatusAndDateRange()
    {
        await using var dbContext = CreateDbContext();
        var service = new TradingPlanService(dbContext, new ActiveWatchlistService(dbContext), CreateMarketContextService(dbContext));

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var startDate = today.AddDays(5);
        var endDate = today.AddDays(10);

        var result = await service.CreateAsync(new TradingPlanCreateDto(
          "sz000021",
          "深科技",
          "Long",
          31.2m,
          29.8m,
          29.4m,
          34.5m,
          36.2m,
          "等待突破前高",
          "跌回箱体",
          "仓位不超过三成",
          "手动策略测试",
          null,
          null,
          "观察执行窗口",
          "Draft",
          "Backup",
          startDate,
          endDate));

        var savedPlan = await dbContext.TradingPlans.SingleAsync();

        Assert.True(result.WatchlistEnsured);
        Assert.Equal(TradingPlanStatus.Draft, savedPlan.Status);
        Assert.Equal("Backup", savedPlan.ActiveScenario);
        Assert.Equal(startDate, savedPlan.PlanStartDate);
        Assert.Equal(endDate, savedPlan.PlanEndDate);
      }

      [Fact]
      public async Task CreateAsync_WhenAnalysisHistoryIdIsNull_CreatesManualPlanWithoutHistoryReference()
      {
        await using var dbContext = CreateDbContext();
        var service = new TradingPlanService(dbContext, new ActiveWatchlistService(dbContext), CreateMarketContextService(dbContext));

        var result = await service.CreateAsync(new TradingPlanCreateDto(
          "sh600519",
          "贵州茅台",
          "Long",
          1680m,
          1600m,
          1580m,
          1750m,
          1800m,
          "等待放量确认",
          "跌破关键支撑",
          "严格控制回撤",
          "手动计划",
          null,
          null,
          "人工录入"));

        var savedPlan = await dbContext.TradingPlans.SingleAsync();

        Assert.True(result.WatchlistEnsured);
        Assert.Null(savedPlan.AnalysisHistoryId);
        Assert.Equal("manual", savedPlan.SourceAgent);
        Assert.Equal(TradingPlanStatus.Pending, savedPlan.Status);
      }

      [Fact]
      public async Task CreateAsync_WhenAnalysisHistoryIdIsZero_TreatsPlanAsManual()
      {
        await using var dbContext = CreateDbContext();
        var service = new TradingPlanService(dbContext, new ActiveWatchlistService(dbContext), CreateMarketContextService(dbContext));

        var result = await service.CreateAsync(new TradingPlanCreateDto(
          "sz000858",
          "五粮液",
          "Long",
          145m,
          138m,
          136m,
          155m,
          160m,
          "等待站稳均线",
          "失守均线",
          "轻仓试错",
          "兼容旧前端传 0",
          0,
          null,
          null));

        Assert.Null(result.Plan.AnalysisHistoryId);
        Assert.Equal("manual", result.Plan.SourceAgent);
      }

      [Fact]
      public async Task CreateAsync_OnLegacySqliteTradingPlansSchema_AllowsManualPlanAfterInitializerFix()
      {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateSqliteDbContext(connection);

        await dbContext.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"TradingPlans\";");
        await dbContext.Database.ExecuteSqlRawAsync(@"
          CREATE TABLE TradingPlans (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            PlanKey TEXT NOT NULL DEFAULT '',
            Title TEXT NOT NULL DEFAULT '',
            Symbol TEXT NOT NULL DEFAULT '',
            Name TEXT NOT NULL DEFAULT '',
            Direction TEXT NOT NULL DEFAULT 'Long',
            Status TEXT NOT NULL DEFAULT 'Pending',
            TriggerPrice REAL NULL,
            InvalidPrice REAL NULL,
            StopLossPrice REAL NULL,
            TakeProfitPrice REAL NULL,
            TargetPrice REAL NULL,
            ExpectedCatalyst TEXT NULL,
            InvalidConditions TEXT NULL,
            RiskLimits TEXT NULL,
            AnalysisSummary TEXT NULL,
            AnalysisHistoryId INTEGER NOT NULL DEFAULT 0,
            SourceAgent TEXT NOT NULL DEFAULT 'commander',
            UserNote TEXT NULL,
            MarketStageLabelAtCreation TEXT NULL,
            StageConfidenceAtCreation REAL NULL,
            SuggestedPositionScale REAL NULL,
            ExecutionFrequencyLabel TEXT NULL,
            MainlineSectorName TEXT NULL,
            MainlineScoreAtCreation REAL NULL,
            SectorNameAtCreation TEXT NULL,
            SectorCodeAtCreation TEXT NULL,
            CreatedAt TEXT NOT NULL DEFAULT '2026-01-01T00:00:00Z',
            UpdatedAt TEXT NOT NULL DEFAULT '2026-01-01T00:00:00Z',
            TriggeredAt TEXT NULL,
            InvalidatedAt TEXT NULL,
            CancelledAt TEXT NULL
          );");
        await dbContext.Database.ExecuteSqlRawAsync(@"
          INSERT INTO TradingPlans (PlanKey, Title, Symbol, Name, Direction, Status, AnalysisHistoryId, SourceAgent, CreatedAt, UpdatedAt)
          VALUES ('legacy-plan', '旧计划', 'sh600000', '浦发银行', 'Long', 'Pending', 0, 'commander', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');");

        await TradingPlanSchemaInitializer.EnsureAsync(dbContext);

        var migratedLegacyPlan = await dbContext.TradingPlans
          .AsNoTracking()
          .SingleAsync(item => item.PlanKey == "legacy-plan");

        Assert.Null(migratedLegacyPlan.AnalysisHistoryId);

        var service = new TradingPlanService(dbContext, new ActiveWatchlistService(dbContext), CreateMarketContextService(dbContext));
        var result = await service.CreateAsync(new TradingPlanCreateDto(
          "sh600000",
          "浦发银行",
          "Long",
          10.2m,
          9.8m,
          9.6m,
          10.9m,
          11.3m,
          "手动观察",
          "跌破 9.8",
          "轻仓",
          "legacy-sqlite-fix",
          null,
          null,
          null));

        Assert.NotEqual(0, result.Plan.Id);
        Assert.Null(result.Plan.AnalysisHistoryId);
        Assert.Equal("manual", result.Plan.SourceAgent);
      }

    [Fact]
    public async Task UpdateAsync_RejectsNonPendingPlan()
    {
        await using var dbContext = CreateDbContext();
        var plan = new TradingPlan
        {
            Symbol = "sh600519",
            Name = "贵州茅台",
            Direction = TradingPlanDirection.Long,
            Status = TradingPlanStatus.Cancelled,
            AnalysisHistoryId = 1,
            SourceAgent = "commander",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        dbContext.TradingPlans.Add(plan);
        await dbContext.SaveChangesAsync();

        var service = new TradingPlanService(dbContext, new ActiveWatchlistService(dbContext), CreateMarketContextService(dbContext));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(
            plan.Id,
            new TradingPlanUpdateDto(
                "贵州茅台",
                "Long",
                1800m,
                1700m,
                1680m,
                1880m,
                1920m,
                "等待确认",
                "跌破离场",
                "严格止损",
                "摘要",
                "commander",
                null)));

        Assert.Equal("仅 Pending / Draft / ReviewRequired 计划允许编辑", error.Message);
    }

      [Fact]
      public async Task UpdateAsync_AllowsReviewRequiredPlan()
      {
        await using var dbContext = CreateDbContext();
        var plan = new TradingPlan
        {
          PlanKey = string.Empty,
          Title = string.Empty,
          Symbol = "sh600519",
          Name = "贵州茅台",
          Direction = TradingPlanDirection.Long,
          Status = TradingPlanStatus.ReviewRequired,
          AnalysisHistoryId = 1,
          SourceAgent = "commander",
          CreatedAt = DateTime.UtcNow,
          UpdatedAt = DateTime.UtcNow
        };
        dbContext.TradingPlans.Add(plan);
        await dbContext.SaveChangesAsync();

        var service = new TradingPlanService(dbContext, new ActiveWatchlistService(dbContext), CreateMarketContextService(dbContext));

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var startDate = today.AddDays(3);
        var endDate = today.AddDays(15);

        var updated = await service.UpdateAsync(
          plan.Id,
          new TradingPlanUpdateDto(
            "贵州茅台",
            "Long",
            1800m,
            1700m,
            1680m,
            1880m,
            1920m,
            "等待确认",
            "跌破离场",
            "严格止损",
            "摘要",
            "commander",
            null,
            "Triggered",
            "Backup",
            startDate,
            endDate));

        Assert.NotNull(updated);
        Assert.Equal(TradingPlanStatus.Triggered, updated!.Status);
        Assert.False(string.IsNullOrWhiteSpace(updated.PlanKey));
        Assert.Equal("贵州茅台", updated.Title);
        Assert.Equal(1800m, updated.TriggerPrice);
        Assert.Equal("Backup", updated.ActiveScenario);
        Assert.Equal(startDate, updated.PlanStartDate);
        Assert.Equal(endDate, updated.PlanEndDate);
      }

      [Fact]
      public async Task GetListAsync_AutoInvalidatesExpiredNonTerminalPlans()
      {
        await using var dbContext = CreateDbContext();
        var chinaToday = GetChinaToday();
        dbContext.TradingPlans.Add(new TradingPlan
        {
          Symbol = "sz000021",
          Name = "深科技",
          Direction = TradingPlanDirection.Long,
          Status = TradingPlanStatus.Pending,
          ActiveScenario = "Primary",
          PlanStartDate = chinaToday.AddDays(-5),
          PlanEndDate = chinaToday.AddDays(-1),
          SourceAgent = "manual",
          CreatedAt = DateTime.UtcNow.AddDays(-5),
          UpdatedAt = DateTime.UtcNow.AddDays(-5)
        });
        await dbContext.SaveChangesAsync();

        var service = new TradingPlanService(dbContext, new ActiveWatchlistService(dbContext), CreateMarketContextService(dbContext));

        var items = await service.GetListAsync("sz000021", 20);
        var savedPlan = await dbContext.TradingPlans.SingleAsync();

        Assert.Single(items);
        Assert.Equal(TradingPlanStatus.Invalid, items[0].Status);
        Assert.Equal(TradingPlanStatus.Invalid, savedPlan.Status);
        Assert.NotNull(savedPlan.InvalidatedAt);
      }

      [Fact]
      public async Task GetListAsync_OnSqliteProvider_UsesOnlyRelationallyTranslatablePredicates()
      {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        var chinaToday = GetChinaToday();
        var now = DateTime.UtcNow;

        var expiredPlan = new TradingPlan
        {
          PlanKey = "sqlite-expired-plan",
          Title = "长白山",
          Symbol = "sh603099",
          Name = "长白山",
          Direction = TradingPlanDirection.Long,
          Status = TradingPlanStatus.Pending,
          ActiveScenario = "Primary",
          PlanStartDate = chinaToday.AddDays(-4),
          PlanEndDate = chinaToday.AddDays(-1),
          SourceAgent = "manual",
          CreatedAt = now.AddMinutes(-5),
          UpdatedAt = now.AddMinutes(-5)
        };
        var activePlan = new TradingPlan
        {
          PlanKey = "sqlite-active-plan",
          Title = "深科技",
          Symbol = "sz000021",
          Name = "深科技",
          Direction = TradingPlanDirection.Long,
          Status = TradingPlanStatus.Pending,
          ActiveScenario = "Primary",
          PlanStartDate = chinaToday,
          PlanEndDate = chinaToday.AddDays(5),
          SourceAgent = "manual",
          CreatedAt = now,
          UpdatedAt = now
        };

        dbContext.TradingPlans.AddRange(
          expiredPlan,
          activePlan,
          new TradingPlan
          {
            PlanKey = "sqlite-placeholder-plan",
            Title = string.Empty,
            Symbol = string.Empty,
            Name = string.Empty,
            Direction = TradingPlanDirection.Long,
            Status = TradingPlanStatus.Draft,
            SourceAgent = "manual",
            CreatedAt = now.AddMinutes(-10),
            UpdatedAt = now.AddMinutes(-10)
          });
        await dbContext.SaveChangesAsync();

        var service = new TradingPlanService(dbContext, new ActiveWatchlistService(dbContext), CreateMarketContextService(dbContext));

        var bySymbol = await service.GetListAsync("SH603099", 20);
        var byTake = await service.GetListAsync(null, 20);
        var byId = await service.GetByIdAsync(expiredPlan.Id);
        var persistedExpiredPlan = await dbContext.TradingPlans.SingleAsync(item => item.Id == expiredPlan.Id);

        Assert.Single(bySymbol);
        Assert.Equal(expiredPlan.Id, bySymbol[0].Id);
        Assert.Equal(TradingPlanStatus.Invalid, bySymbol[0].Status);
        Assert.NotNull(bySymbol[0].InvalidatedAt);

        Assert.NotNull(byId);
        Assert.Equal(expiredPlan.Id, byId!.Id);
        Assert.Equal(TradingPlanStatus.Invalid, byId.Status);

        Assert.Equal(2, byTake.Count);
        Assert.DoesNotContain(byTake, item => string.IsNullOrWhiteSpace(item.Symbol) || string.IsNullOrWhiteSpace(item.Name));
        Assert.Contains(byTake, item => item.Id == expiredPlan.Id && item.Status == TradingPlanStatus.Invalid);
        Assert.Contains(byTake, item => item.Id == activePlan.Id && item.Status == TradingPlanStatus.Pending);

        Assert.Equal(TradingPlanStatus.Invalid, persistedExpiredPlan.Status);
        Assert.NotNull(persistedExpiredPlan.InvalidatedAt);
      }

      [Fact]
      public async Task ResumeAsync_MarksReviewRequiredPlanBackToPendingAndAddsEvent()
      {
        await using var dbContext = CreateDbContext();
        var plan = new TradingPlan
        {
          Symbol = "sz000021",
          Name = "深科技",
          Direction = TradingPlanDirection.Long,
          Status = TradingPlanStatus.ReviewRequired,
          AnalysisHistoryId = 1,
          SourceAgent = "commander",
          CreatedAt = DateTime.UtcNow,
          UpdatedAt = DateTime.UtcNow
        };
        dbContext.TradingPlans.Add(plan);
        await dbContext.SaveChangesAsync();

        var service = new TradingPlanService(dbContext, new ActiveWatchlistService(dbContext), CreateMarketContextService(dbContext));

        var resumed = await service.ResumeAsync(plan.Id);

        Assert.NotNull(resumed);
        Assert.Equal(TradingPlanStatus.Pending, resumed!.Status);
        var reviewEvent = await dbContext.TradingPlanEvents.SingleAsync();
        Assert.Equal(TradingPlanEventType.ReviewCleared, reviewEvent.EventType);
      }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

      var context = new AppDbContext(options);
      ContextOptionsMap.Add(context, options);
      return context;
    }

      private static AppDbContext CreateSqliteDbContext(SqliteConnection connection)
      {
        var options = new DbContextOptionsBuilder<AppDbContext>()
          .UseSqlite(connection)
          .Options;

        var dbContext = new AppDbContext(options);
        ContextOptionsMap.Add(dbContext, options);
        dbContext.Database.EnsureCreated();
        return dbContext;
      }

      private static StockMarketContextService CreateMarketContextService(AppDbContext dbContext)
      {
      return new StockMarketContextService(GetOptions(dbContext));
    }

    private static DbContextOptions<AppDbContext> GetOptions(AppDbContext dbContext)
    {
      return ContextOptionsMap.TryGetValue(dbContext, out var options)
        ? options
        : throw new InvalidOperationException("Missing AppDbContext options for test instance.");
      }

    private static DateOnly GetChinaToday()
    {
      TimeZoneInfo chinaTimeZone;
      try
      {
        chinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
      }
      catch (TimeZoneNotFoundException)
      {
        chinaTimeZone = TimeZoneInfo.CreateCustomTimeZone("China Standard Time", TimeSpan.FromHours(8), "China Standard Time", "China Standard Time");
      }

      return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, chinaTimeZone));
    }

    [Fact]
    public async Task DeleteAsync_RemovesPlan()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TradingPlans.Add(new TradingPlan
        {
            Symbol = "sz000021",
            Name = "深科技",
            Direction = TradingPlanDirection.Long,
            Status = TradingPlanStatus.Pending,
            AnalysisHistoryId = 1,
            SourceAgent = "commander",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var planId = await dbContext.TradingPlans.Select(item => item.Id).SingleAsync();
        var service = new TradingPlanService(dbContext, new ActiveWatchlistService(dbContext), CreateMarketContextService(dbContext));

        var removed = await service.DeleteAsync(planId);

        Assert.True(removed);
        Assert.Empty(dbContext.TradingPlans);
    }

      [Theory]
      [InlineData("Draft", TradingPlanStatus.Draft)]
      [InlineData("ReviewRequired", TradingPlanStatus.ReviewRequired)]
      [InlineData("NeedsReview", TradingPlanStatus.ReviewRequired)]
      [InlineData("Archived", TradingPlanStatus.Cancelled)]
      [InlineData("UnexpectedLegacyStatus", TradingPlanStatus.Cancelled)]
      public void ParseTradingPlanStatus_MapsLegacyAndReviewValues(string raw, TradingPlanStatus expected)
      {
        Assert.Equal(expected, AppDbContext.ParseTradingPlanStatus(raw));
      }

      [Theory]
      [InlineData("Draft", TradingPlanStatus.Draft)]
      [InlineData("Archived", TradingPlanStatus.Cancelled)]
      [InlineData("UnexpectedLegacyStatus", TradingPlanStatus.Cancelled)]
      public void ParseTradingPlanStatus_NormalizesLegacyValues(string rawValue, TradingPlanStatus expected)
      {
        var result = AppDbContext.ParseTradingPlanStatus(rawValue);

        Assert.Equal(expected, result);
      }

      [Fact]
      public async Task GetListAsync_AllowsLegacyDraftStatusRows()
      {
        await using var dbContext = CreateDbContext();
        dbContext.TradingPlans.Add(new TradingPlan
        {
          Symbol = "sz000021",
          Name = "深科技",
          Direction = TradingPlanDirection.Long,
          Status = TradingPlanStatus.Draft,
          AnalysisHistoryId = 1,
          SourceAgent = "commander",
          CreatedAt = DateTime.UtcNow,
          UpdatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = new TradingPlanService(dbContext, new ActiveWatchlistService(dbContext), CreateMarketContextService(dbContext));

        var items = await service.GetListAsync(null, 20);

        Assert.Single(items);
        Assert.Equal(TradingPlanStatus.Draft, items[0].Status);
      }

      [Fact]
      public async Task GetListAsync_FiltersLegacyPlaceholderRowsMissingIdentityFields()
      {
        await using var dbContext = CreateDbContext();
        dbContext.TradingPlans.AddRange(
          new TradingPlan
          {
            Symbol = "sz000021",
            Name = "深科技",
            Direction = TradingPlanDirection.Long,
            Status = TradingPlanStatus.Pending,
            AnalysisHistoryId = 7,
            SourceAgent = "commander",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
          },
          new TradingPlan
          {
            Symbol = string.Empty,
            Name = string.Empty,
            Direction = TradingPlanDirection.Long,
            Status = TradingPlanStatus.Draft,
            AnalysisHistoryId = 0,
            SourceAgent = "commander",
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-1)
          },
          new TradingPlan
          {
            Symbol = string.Empty,
            Name = string.Empty,
            Direction = TradingPlanDirection.Long,
            Status = TradingPlanStatus.Cancelled,
            AnalysisHistoryId = 0,
            SourceAgent = "commander",
            CreatedAt = DateTime.UtcNow.AddMinutes(-2),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-2)
          });
        await dbContext.SaveChangesAsync();

          var service = new TradingPlanService(dbContext, new ActiveWatchlistService(dbContext), CreateMarketContextService(dbContext));

        var items = await service.GetListAsync(null, 20);

        Assert.Single(items);
        Assert.Equal("sz000021", items[0].Symbol);
        Assert.Equal("深科技", items[0].Name);
      }
}