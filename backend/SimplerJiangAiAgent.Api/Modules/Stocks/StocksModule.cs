using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Modules.Stocks;

public sealed class StocksModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        // 爬虫配置（预留反爬/代理池）
        services.Configure<StockCrawlerOptions>(configuration.GetSection(StockCrawlerOptions.SectionName));

        // 来源爬虫（占位实现，后续替换为真实解析逻辑）
        services.AddHttpClient();
        services.AddTransient<IStockCrawlerSource, TencentStockCrawler>();
        services.AddTransient<IStockCrawlerSource, SinaStockCrawler>();
        services.AddTransient<IStockCrawlerSource, BaiduStockCrawler>();
        services.AddTransient<IStockCrawlerSource, EastmoneyStockCrawler>();
        services.AddSingleton<IStockCrawler, CompositeStockCrawler>();
        services.AddScoped<ILocalFactIngestionService, LocalFactIngestionService>();
        services.AddScoped<ILocalFactAiEnrichmentService, LocalFactAiEnrichmentService>();
        services.AddScoped<IQueryLocalFactDatabaseTool, QueryLocalFactDatabaseTool>();
        services.AddScoped<IStockDataService, StockDataService>();
        services.AddScoped<IStockHistoryService, StockHistoryService>();
        services.AddTransient<IStockSearchService, StockSearchService>();
        services.AddScoped<IStockAgentOrchestrator, StockAgentOrchestrator>();
        services.AddScoped<IStockAgentHistoryService, StockAgentHistoryService>();
        services.AddScoped<IStockChatHistoryService, StockChatHistoryService>();
        services.AddScoped<IStockNewsImpactService, StockNewsImpactService>();
        services.AddScoped<IStockSignalService, StockSignalService>();
        services.AddScoped<IStockPositionGuidanceService, StockPositionGuidanceService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stocks");

        // 查询单个股票信息（包含新闻与指标）
        group.MapGet("/quote", async (string symbol, string? source, IStockDataService dataService) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var result = await dataService.GetQuoteAsync(symbol.Trim(), source);
            return Results.Ok(result);
        })
        .WithName("GetStockQuote")
        .WithOpenApi();

        // 模糊搜索股票
        group.MapGet("/search", async (string q, int? limit, IStockSearchService searchService) =>
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return Results.Ok(Array.Empty<StockSearchResultDto>());
            }

            var result = await searchService.SearchAsync(q.Trim(), Math.Clamp(limit ?? 20, 1, 50));
            return Results.Ok(result);
        })
        .WithName("SearchStocks")
        .WithOpenApi();

        // 获取大盘指数信息
        group.MapGet("/market", async (string? symbol, string? source, IStockDataService dataService) =>
        {
            var target = string.IsNullOrWhiteSpace(symbol) ? "sh000001" : symbol.Trim();
            var result = await dataService.GetMarketIndexAsync(target, source);
            return Results.Ok(result);
        })
        .WithName("GetMarketIndex")
        .WithOpenApi();

        // 获取缓存的大盘指数
        group.MapGet("/market/cache", async (string? symbol, AppDbContext dbContext) =>
        {
            var target = string.IsNullOrWhiteSpace(symbol) ? "sh000001" : symbol.Trim().ToLowerInvariant();
            var latest = await dbContext.MarketIndexSnapshots
                .Where(x => x.Symbol == target)
                .OrderByDescending(x => x.Timestamp)
                .FirstOrDefaultAsync();

            return latest is null
                ? Results.NotFound()
                : Results.Ok(new MarketIndexDto(latest.Symbol, latest.Name, latest.Price, latest.Change, latest.ChangePercent, latest.Timestamp));
        })
        .WithName("GetMarketIndexCache")
        .WithOpenApi();

        // 获取K线数据
        group.MapGet("/kline", async (string symbol, string? interval, int? count, string? source, IStockDataService dataService) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var data = await dataService.GetKLineAsync(symbol.Trim(), interval ?? "day", count ?? 60, source);
            return Results.Ok(data);
        })
        .WithName("GetKLine")
        .WithOpenApi();

        // 获取分时数据
        group.MapGet("/minute", async (string symbol, string? source, IStockDataService dataService) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var data = await dataService.GetMinuteLineAsync(symbol.Trim(), source);
            return Results.Ok(data);
        })
        .WithName("GetMinuteLine")
        .WithOpenApi();

        // 获取盘中消息
        group.MapGet("/messages", async (string symbol, string? source, IStockDataService dataService) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var data = await dataService.GetIntradayMessagesAsync(symbol.Trim(), source);
            return Results.Ok(data);
        })
        .WithName("GetIntradayMessages")
        .WithOpenApi();

        // 新闻影响评估
        group.MapGet("/news/impact", async (string symbol, string? source, IStockDataService dataService, IStockNewsImpactService impactService) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var target = symbol.Trim();
            var quote = await dataService.GetQuoteAsync(target, source);
            var messages = await dataService.GetIntradayMessagesAsync(target, source);
            var impact = impactService.Evaluate(target, quote.Name, messages);
            return Results.Ok(impact);
        })
        .WithName("GetNewsImpact")
        .WithOpenApi();

        app.MapGet("/api/news", async (string? symbol, string? level, ILocalFactIngestionService ingestionService, IQueryLocalFactDatabaseTool queryTool) =>
        {
            var normalizedLevel = string.IsNullOrWhiteSpace(level) ? "stock" : level.Trim().ToLowerInvariant();
            if (normalizedLevel is not ("stock" or "sector" or "market"))
            {
                return Results.BadRequest(new { message = "level 仅支持 stock/sector/market" });
            }

            if (normalizedLevel == "market")
            {
                await ingestionService.EnsureMarketFreshAsync();
                var marketResult = await queryTool.QueryMarketAsync();
                return Results.Ok(marketResult);
            }

            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var target = symbol.Trim();
            await ingestionService.EnsureFreshAsync(target);
            var result = await queryTool.QueryLevelAsync(target, normalizedLevel);
            return Results.Ok(result);
        })
        .WithName("GetLocalNews")
        .WithOpenApi();

        // 事件驱动信号（含证据/反证与历史对齐）
        group.MapGet("/signals", async (string symbol, string? source, IStockDataService dataService, IStockNewsImpactService impactService, IStockSignalService signalService) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var target = symbol.Trim();
            var quote = await dataService.GetQuoteAsync(target, source);
            var kline = await dataService.GetKLineAsync(target, "day", 60, source);
            var minute = await dataService.GetMinuteLineAsync(target, source);
            var messages = await dataService.GetIntradayMessagesAsync(target, source);
            var detail = new StockDetailDto(quote, kline, minute, messages);
            var impact = impactService.Evaluate(target, quote.Name, messages);
            var signal = signalService.Evaluate(detail, impact);

            return Results.Ok(signal);
        })
        .WithName("GetStockSignals")
        .WithOpenApi();

        // 个性化风险 + 仓位建议
        group.MapPost("/position-guidance", async (StockPositionGuidanceRequestDto request, IStockDataService dataService, IStockNewsImpactService impactService, IStockSignalService signalService, IStockPositionGuidanceService guidanceService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            if (request.Capital <= 0)
            {
                return Results.BadRequest(new { message = "capital 必须大于0" });
            }

            var target = request.Symbol.Trim();
            var quote = await dataService.GetQuoteAsync(target, request.Source);
            var kline = await dataService.GetKLineAsync(target, "day", 60, request.Source);
            var minute = await dataService.GetMinuteLineAsync(target, request.Source);
            var messages = await dataService.GetIntradayMessagesAsync(target, request.Source);

            var detail = new StockDetailDto(quote, kline, minute, messages);
            var impact = impactService.Evaluate(target, quote.Name, messages);
            var signal = signalService.Evaluate(detail, impact);
            var guidance = guidanceService.Build(quote, signal, request.RiskLevel, request.CurrentPositionPercent);

            return Results.Ok(guidance);
        })
        .WithName("GetStockPositionGuidance")
        .WithOpenApi();

        // 手动触发一次同步
        group.MapPost("/sync", async (IStockSyncService syncService) =>
        {
            await syncService.SyncOnceAsync();
            return Results.Ok(new { status = "ok" });
        })
        .WithName("SyncStocks")
        .WithOpenApi();

        // 获取组合详情
        group.MapGet("/detail", async (string symbol, string? interval, int? count, string? source, bool? persist, IStockDataService dataService, IStockSyncService syncService, IStockHistoryService historyService) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var target = symbol.Trim();
            var selectedInterval = interval ?? "day";
            var quote = await dataService.GetQuoteAsync(target, source);
            var kline = await dataService.GetKLineAsync(target, selectedInterval, count ?? 60, source);
            var minute = await dataService.GetMinuteLineAsync(target, source);
            var messages = await dataService.GetIntradayMessagesAsync(target, source);

            var detail = new StockDetailDto(quote, kline, minute, messages);
            if (persist is null || persist.Value)
            {
                await syncService.SaveDetailAsync(detail, selectedInterval);
                await historyService.UpsertAsync(quote);
            }
            return Results.Ok(detail);
        })
        .WithName("GetStockDetail")
        .WithOpenApi();

        // 多Agent分析
        group.MapPost("/agents", async (StockAgentRequestDto request, IStockAgentOrchestrator orchestrator, HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(request.Symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            try
            {
                var result = await orchestrator.RunAsync(request, context.RequestAborted);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("RunStockAgents")
        .WithOpenApi();

        // 单Agent分析（依次调用）
        group.MapPost("/agents/single", async (StockAgentSingleRequestDto request, IStockAgentOrchestrator orchestrator, HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(request.Symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            if (string.IsNullOrWhiteSpace(request.AgentId))
            {
                return Results.BadRequest(new { message = "agentId 不能为空" });
            }

            try
            {
                var result = await orchestrator.RunSingleAsync(request, context.RequestAborted);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("RunStockAgentSingle")
        .WithOpenApi();

        group.MapGet("/agents/history", async (string symbol, IStockAgentHistoryService historyService) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var normalized = StockSymbolNormalizer.Normalize(symbol);
            var list = await historyService.GetListAsync(normalized);
            var result = list
                .Select(item => new StockAgentHistoryItemDto(item.Id, item.Symbol, item.Name, item.Summary, item.CreatedAt))
                .ToArray();
            return Results.Ok(result);
        })
        .WithName("GetStockAgentHistory")
        .WithOpenApi();

        group.MapGet("/agents/history/{id:long}", async (long id, IStockAgentHistoryService historyService) =>
        {
            var item = await historyService.GetByIdAsync(id);
            if (item is null)
            {
                return Results.NotFound();
            }

            var result = new StockAgentHistoryDetailDto(
                item.Id,
                item.Symbol,
                item.Name,
                item.Summary,
                item.CreatedAt,
                JsonDocument.Parse(item.ResultJson).RootElement.Clone());
            return Results.Ok(result);
        })
        .WithName("GetStockAgentHistoryDetail")
        .WithOpenApi();

        group.MapPost("/agents/history", async (StockAgentHistoryCreateDto request, IStockAgentHistoryService historyService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var normalized = StockSymbolNormalizer.Normalize(request.Symbol);
            var summary = ExtractAgentSummary(request.Result);
            var entry = new Data.Entities.StockAgentAnalysisHistory
            {
                Symbol = normalized,
                Name = request.Name ?? string.Empty,
                Interval = request.Interval ?? string.Empty,
                Source = request.Source,
                Provider = request.Provider,
                Model = request.Model,
                UseInternet = request.UseInternet,
                Summary = summary,
                ResultJson = JsonSerializer.Serialize(request.Result),
                CreatedAt = DateTime.UtcNow
            };

            var saved = await historyService.AddAsync(entry);
            return Results.Ok(new StockAgentHistoryItemDto(saved.Id, saved.Symbol, saved.Name, saved.Summary, saved.CreatedAt));
        })
        .WithName("CreateStockAgentHistory")
        .WithOpenApi();

        // 查询历史记录
        group.MapGet("/history", async (IStockHistoryService historyService) =>
        {
            var list = await historyService.GetAllAsync();
            return Results.Ok(list);
        })
        .WithName("GetStockHistory")
        .WithOpenApi();

        // 刷新历史记录行情
        group.MapPost("/history/refresh", async (string? source, IStockHistoryService historyService) =>
        {
            var list = await historyService.RefreshAsync(source);
            return Results.Ok(list);
        })
        .WithName("RefreshStockHistory")
        .WithOpenApi();

        // 删除历史记录
        group.MapDelete("/history/{id:long}", async (long id, IStockHistoryService historyService) =>
        {
            var removed = await historyService.DeleteAsync(id);
            return removed ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteStockHistory")
        .WithOpenApi();

        group.MapGet("/chat/sessions", async (string symbol, IStockChatHistoryService chatHistoryService) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var list = await chatHistoryService.GetSessionsAsync(symbol);
            var result = list
                .Select(item => new StockChatSessionDto(item.SessionKey, item.Title, item.CreatedAt, item.UpdatedAt))
                .ToArray();
            return Results.Ok(result);
        })
        .WithName("GetStockChatSessions")
        .WithOpenApi();

        group.MapPost("/chat/sessions", async (StockChatSessionCreateDto request, IStockChatHistoryService chatHistoryService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var session = await chatHistoryService.CreateSessionAsync(request.Symbol, request.Title, request.SessionKey);
            var result = new StockChatSessionDto(session.SessionKey, session.Title, session.CreatedAt, session.UpdatedAt);
            return Results.Ok(result);
        })
        .WithName("CreateStockChatSession")
        .WithOpenApi();

        group.MapGet("/chat/sessions/{sessionKey}/messages", async (string sessionKey, IStockChatHistoryService chatHistoryService) =>
        {
            if (string.IsNullOrWhiteSpace(sessionKey))
            {
                return Results.BadRequest(new { message = "sessionKey 不能为空" });
            }

            var list = await chatHistoryService.GetMessagesAsync(sessionKey);
            var result = list
                .Select(item => new StockChatMessageDto(item.Role, item.Content, item.CreatedAt))
                .ToArray();
            return Results.Ok(result);
        })
        .WithName("GetStockChatMessages")
        .WithOpenApi();

        group.MapPut("/chat/sessions/{sessionKey}/messages", async (string sessionKey, StockChatMessagesRequestDto request, IStockChatHistoryService chatHistoryService) =>
        {
            if (string.IsNullOrWhiteSpace(sessionKey))
            {
                return Results.BadRequest(new { message = "sessionKey 不能为空" });
            }

            await chatHistoryService.SaveMessagesAsync(sessionKey, request.Messages ?? Array.Empty<StockChatMessageDto>());
            return Results.Ok(new { status = "ok" });
        })
        .WithName("SaveStockChatMessages")
        .WithOpenApi();

        // 获取缓存详情
        group.MapGet("/detail/cache", async (string symbol, string? interval, int? count, AppDbContext dbContext) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var target = symbol.Trim().ToLowerInvariant();
            var quote = await dbContext.StockQuoteSnapshots
                .Where(x => x.Symbol == target)
                .OrderByDescending(x => x.Timestamp)
                .FirstOrDefaultAsync();

            if (quote is null)
            {
                return Results.NotFound();
            }

            var intervalValue = string.IsNullOrWhiteSpace(interval) ? "day" : interval.Trim().ToLowerInvariant();
            var take = Math.Max(10, count ?? 60);

            var kline = await dbContext.KLinePoints
                .Where(x => x.Symbol == target && x.Interval == intervalValue)
                .OrderBy(x => x.Date)
                .Take(take)
                .Select(x => new KLinePointDto(x.Date, x.Open, x.Close, x.High, x.Low, x.Volume))
                .ToListAsync();

            var minute = await dbContext.MinuteLinePoints
                .Where(x => x.Symbol == target)
                .OrderBy(x => x.Time)
                .Select(x => new MinuteLinePointDto(x.Date, x.Time, x.Price, x.AveragePrice, x.Volume))
                .ToListAsync();

            var messages = await dbContext.IntradayMessages
                .Where(x => x.Symbol == target)
                .OrderByDescending(x => x.PublishedAt)
                .Take(20)
                .Select(x => new IntradayMessageDto(x.Title, x.Source, x.PublishedAt, x.Url))
                .ToListAsync();

            var quoteDto = new StockQuoteDto(quote.Symbol, quote.Name, quote.Price, quote.Change, quote.ChangePercent,
                0m, 0m, 0m, 0m, 0m, quote.Timestamp, Array.Empty<StockNewsDto>(), Array.Empty<StockIndicatorDto>());

            return Results.Ok(new StockDetailDto(quoteDto, kline, minute, messages));
        })
        .WithName("GetStockDetailCache")
        .WithOpenApi();

        // 查看当前启用的数据来源
        group.MapGet("/sources", (IEnumerable<IStockCrawlerSource> crawlers) =>
        {
            var sources = crawlers
                .Select(c => c.SourceName)
                .Distinct()
                .ToArray();
            return Results.Ok(sources);
        })
        .WithName("GetStockSources")
        .WithOpenApi();
    }

    private static string? ExtractAgentSummary(JsonElement result)
    {
        if (!result.TryGetProperty("agents", out var agents) || agents.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var agent in agents.EnumerateArray())
        {
            if (!agent.TryGetProperty("agentId", out var agentId))
            {
                continue;
            }

            if (!string.Equals(agentId.GetString(), "commander", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!agent.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (data.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.String)
            {
                return summary.GetString();
            }
        }

        return null;
    }
}
