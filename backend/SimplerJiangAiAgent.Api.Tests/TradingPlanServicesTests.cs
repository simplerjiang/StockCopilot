using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class TradingPlanServicesTests
{
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
                  "agentId": "commander",
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
        await dbContext.SaveChangesAsync();

        var historyService = new StockAgentHistoryService(dbContext);
        var service = new TradingPlanDraftService(historyService);

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
                  "agentId": "commander",
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
                  "data": {
                    "metrics": {
                      "institutionTargetPrice": 13.8
                    }
                  }
                },
                {
                  "agentId": "trend_analysis",
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
        await dbContext.SaveChangesAsync();

        var service = new TradingPlanDraftService(new StockAgentHistoryService(dbContext));

        var draft = await service.BuildDraftAsync("sz000001", history.Id);

        Assert.Null(draft.TriggerPrice);
        Assert.Null(draft.InvalidPrice);
        Assert.Null(draft.StopLossPrice);
        Assert.Equal(13.8m, draft.TakeProfitPrice);
        Assert.Equal(13.8m, draft.TargetPrice);
        Assert.Equal("Long", draft.Direction);
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
        await dbContext.SaveChangesAsync();

        var watchlistService = new ActiveWatchlistService(dbContext);
        var service = new TradingPlanService(dbContext, watchlistService);

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
        Assert.Equal("sh600000", watchlist.Symbol);
        Assert.Equal("trading-plan", watchlist.SourceTag);
        Assert.Equal($"plan:{savedPlan.Id}", watchlist.Note);
        Assert.Equal(10.9m, savedPlan.TakeProfitPrice);
        Assert.Equal(11.5m, savedPlan.TargetPrice);
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

        var service = new TradingPlanService(dbContext, new ActiveWatchlistService(dbContext));

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

        Assert.Equal("仅 Pending 计划允许编辑", error.Message);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
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
        var service = new TradingPlanService(dbContext, new ActiveWatchlistService(dbContext));

        var removed = await service.DeleteAsync(planId);

        Assert.True(removed);
        Assert.Empty(dbContext.TradingPlans);
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

        var service = new TradingPlanService(dbContext, new ActiveWatchlistService(dbContext));

        var items = await service.GetListAsync(null, 20);

        Assert.Single(items);
        Assert.Equal(TradingPlanStatus.Draft, items[0].Status);
      }
}