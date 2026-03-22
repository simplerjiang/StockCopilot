using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;
using SimplerJiangAiAgent.Api.Modules.Market.Services;

namespace SimplerJiangAiAgent.Api.Modules.Stocks;

public sealed class StocksModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        // 爬虫配置（预留反爬/代理池）
        services.Configure<StockCrawlerOptions>(configuration.GetSection(StockCrawlerOptions.SectionName));

        // 来源爬虫（占位实现，后续替换为真实解析逻辑）
        services.AddHttpClient();
        services.AddTransient<IStockCrawlerSource, EastmoneyStockCrawler>();
        services.AddTransient<IStockCrawlerSource, TencentStockCrawler>();
        services.AddTransient<IStockCrawlerSource, SinaStockCrawler>();
        services.AddTransient<IStockCrawlerSource, BaiduStockCrawler>();
        services.AddTransient<IStockFundamentalSnapshotService, EastmoneyFundamentalSnapshotService>();
        services.AddSingleton<IStockCrawler, CompositeStockCrawler>();
        services.Configure<HighFrequencyQuoteOptions>(configuration.GetSection(HighFrequencyQuoteOptions.SectionName));
        services.Configure<TradingPlanTriggerOptions>(configuration.GetSection(TradingPlanTriggerOptions.SectionName));
        services.Configure<TradingPlanReviewOptions>(configuration.GetSection(TradingPlanReviewOptions.SectionName));
        services.AddScoped<IActiveWatchlistService, ActiveWatchlistService>();
        services.AddScoped<ILocalFactIngestionService, LocalFactIngestionService>();
        services.AddScoped<ILocalFactAiEnrichmentService, LocalFactAiEnrichmentService>();
        services.AddScoped<ILocalFactArticleReadService, LocalFactArticleReadService>();
        services.AddScoped<IQueryLocalFactDatabaseTool, QueryLocalFactDatabaseTool>();
        services.AddScoped<IStockDataService, StockDataService>();
        services.AddScoped<IStockHistoryService, StockHistoryService>();
        services.AddTransient<IStockSearchService, StockSearchService>();
        services.Configure<StockCopilotSearchOptions>(configuration.GetSection(StockCopilotSearchOptions.SectionName));
        services.AddScoped<IStockAgentOrchestrator, StockAgentOrchestrator>();
        services.AddScoped<IStockAgentHistoryService, StockAgentHistoryService>();
        services.AddScoped<IStockAgentFeatureEngineeringService, StockAgentFeatureEngineeringService>();
        services.AddScoped<IStockAgentReplayCalibrationService, StockAgentReplayCalibrationService>();
        services.AddScoped<IStockCopilotMcpService, StockCopilotMcpService>();
        services.AddScoped<IStockCopilotSessionService, StockCopilotSessionService>();
        services.AddScoped<ITradingPlanDraftService, TradingPlanDraftService>();
        services.AddScoped<ITradingPlanService, TradingPlanService>();
        services.AddScoped<IStockMarketContextService, StockMarketContextService>();
        services.AddScoped<ITradingPlanTriggerService, TradingPlanTriggerService>();
        services.AddScoped<ITradingPlanReviewService, TradingPlanReviewService>();
        services.AddScoped<IStockChatHistoryService, StockChatHistoryService>();
        services.AddScoped<IStockNewsImpactService, StockNewsImpactService>();
        services.AddScoped<IStockSignalService, StockSignalService>();
        services.AddScoped<IStockPositionGuidanceService, StockPositionGuidanceService>();
        services.AddHostedService<TradingPlanTriggerWorker>();
        services.AddHostedService<TradingPlanReviewWorker>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stocks");

        group.MapGet("/watchlist", async (IActiveWatchlistService watchlistService) =>
        {
            var items = await watchlistService.GetAllAsync();
            var result = items
                .Select(item => new ActiveWatchlistItemDto(
                    item.Id,
                    item.Symbol,
                    item.Name,
                    item.SourceTag,
                    item.Note,
                    item.IsEnabled,
                    item.CreatedAt,
                    item.UpdatedAt,
                    item.LastQuoteSyncAt))
                .ToArray();
            return Results.Ok(result);
        })
        .WithName("GetActiveWatchlist")
        .WithOpenApi();

        group.MapPost("/watchlist", async (ActiveWatchlistUpsertDto request, IActiveWatchlistService watchlistService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var item = await watchlistService.UpsertAsync(
                request.Symbol,
                request.Name,
                request.SourceTag,
                request.Note,
                request.IsEnabled ?? true);

            return Results.Ok(new ActiveWatchlistItemDto(
                item.Id,
                item.Symbol,
                item.Name,
                item.SourceTag,
                item.Note,
                item.IsEnabled,
                item.CreatedAt,
                item.UpdatedAt,
                item.LastQuoteSyncAt));
        })
        .WithName("UpsertActiveWatchlist")
        .WithOpenApi();

        group.MapPost("/watchlist/{symbol}/touch", async (string symbol, ActiveWatchlistTouchDto? request, IActiveWatchlistService watchlistService) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var item = await watchlistService.TouchAsync(symbol, request?.Name, request?.SourceTag, request?.Note);
            return Results.Ok(new ActiveWatchlistItemDto(
                item.Id,
                item.Symbol,
                item.Name,
                item.SourceTag,
                item.Note,
                item.IsEnabled,
                item.CreatedAt,
                item.UpdatedAt,
                item.LastQuoteSyncAt));
        })
        .WithName("TouchActiveWatchlist")
        .WithOpenApi();

        group.MapDelete("/watchlist/{symbol}", async (string symbol, IActiveWatchlistService watchlistService) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var removed = await watchlistService.RemoveAsync(symbol);
            return removed ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteActiveWatchlist")
        .WithOpenApi();

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

        group.MapGet("/fundamental-snapshot", async (string symbol, IStockFundamentalSnapshotService fundamentalSnapshotService, HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var result = await fundamentalSnapshotService.GetSnapshotAsync(symbol.Trim(), httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetStockFundamentalSnapshot")
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

        group.MapGet("/quotes/batch", async (string symbols, IRealtimeMarketOverviewService realtimeService, HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(symbols))
            {
                return Results.BadRequest(new { message = "symbols 不能为空" });
            }

            var items = symbols
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(20)
                .ToArray();
            if (items.Length == 0)
            {
                return Results.BadRequest(new { message = "symbols 不能为空" });
            }

            var result = await realtimeService.GetBatchQuotesAsync(items, httpContext.RequestAborted);
            return Results.Ok(result);
        })
        .WithName("GetBatchStockQuotes")
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

        group.MapGet("/chart", async (string symbol, string? interval, int? count, string? source, bool? includeQuote, bool? includeMinute, IStockDataService dataService, HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var target = symbol.Trim();
            var selectedInterval = interval ?? "day";
            var take = count ?? 60;
            var cancellationToken = httpContext.RequestAborted;
            var shouldIncludeQuote = includeQuote ?? true;
            var shouldIncludeMinute = includeMinute ?? true;

            var klineTask = dataService.GetKLineAsync(target, selectedInterval, take, source, cancellationToken);
            Task<StockQuoteDto>? quoteTask = shouldIncludeQuote
                ? dataService.GetQuoteAsync(target, source, cancellationToken)
                : null;
            Task<IReadOnlyList<MinuteLinePointDto>>? minuteTask = shouldIncludeMinute
                ? dataService.GetMinuteLineAsync(target, source, cancellationToken)
                : null;

            var tasks = new List<Task> { klineTask };
            if (quoteTask is not null)
            {
                tasks.Add(quoteTask);
            }
            if (minuteTask is not null)
            {
                tasks.Add(minuteTask);
            }

            await Task.WhenAll(tasks);

            var kline = await klineTask;
            var quote = quoteTask is null ? null : await quoteTask;
            var minute = minuteTask is null ? null : await minuteTask;
            var mergedKLine = string.Equals(selectedInterval, "day", StringComparison.OrdinalIgnoreCase)
                ? StockRealtimeKLineMerge.MergeDailyFromMinuteLines(kline, minute ?? Array.Empty<MinuteLinePointDto>(), take)
                : kline;

            return Results.Ok(new StockChartDto(quote, mergedKLine, minute));
        })
        .WithName("GetStockChart")
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

        app.MapGet("/api/news/archive", async (
            string? keyword,
            string? level,
            string? sentiment,
            int? page,
            int? pageSize,
            IQueryLocalFactDatabaseTool queryTool) =>
        {
            try
            {
                var archive = await queryTool.QueryArchiveAsync(
                    keyword,
                    level,
                    sentiment,
                    Math.Max(page ?? 1, 1),
                    Math.Clamp(pageSize ?? 20, 1, 50));

                return Results.Ok(archive);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("GetLocalNewsArchive")
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
            var mergedKLine = StockRealtimeKLineMerge.MergeDailyFromMinuteLines(kline, minute, 60);
            var detail = new StockDetailDto(quote, mergedKLine, minute, messages);
            var impact = impactService.Evaluate(target, quote.Name, messages);
            var signal = signalService.Evaluate(detail, impact);

            return Results.Ok(signal);
        })
        .WithName("GetStockSignals")
        .WithOpenApi();

        // 个性化风险 + 仓位建议
        group.MapPost("/position-guidance", async (StockPositionGuidanceRequestDto request, IStockDataService dataService, IStockNewsImpactService impactService, IStockSignalService signalService, IStockPositionGuidanceService guidanceService, IStockMarketContextService marketContextService) =>
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
            var mergedKLine = StockRealtimeKLineMerge.MergeDailyFromMinuteLines(kline, minute, 60);

            var detail = new StockDetailDto(quote, mergedKLine, minute, messages);
            var impact = impactService.Evaluate(target, quote.Name, messages);
            var signal = signalService.Evaluate(detail, impact);
            var marketContext = await marketContextService.GetLatestAsync(target);
            var guidance = guidanceService.Build(quote, signal, request.RiskLevel, request.CurrentPositionPercent, marketContext);

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
        group.MapGet("/detail", async (string symbol, string? source, bool? persist, bool? includeFundamentalSnapshot, IStockDataService dataService, IStockFundamentalSnapshotService fundamentalSnapshotService, IStockSyncService syncService, IStockHistoryService historyService, HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var target = symbol.Trim();
            var cancellationToken = httpContext.RequestAborted;
            var quoteTask = dataService.GetQuoteAsync(target, source, cancellationToken);
            var messagesTask = dataService.GetIntradayMessagesAsync(target, source, cancellationToken);
            var shouldIncludeFundamentalSnapshot = includeFundamentalSnapshot ?? true;
            Task<StockFundamentalSnapshotDto?>? fundamentalSnapshotTask = shouldIncludeFundamentalSnapshot
                ? fundamentalSnapshotService.GetSnapshotAsync(target, cancellationToken)
                : null;

            if (fundamentalSnapshotTask is null)
            {
                await Task.WhenAll(quoteTask, messagesTask);
            }
            else
            {
                await Task.WhenAll(quoteTask, messagesTask, fundamentalSnapshotTask);
            }

            var quote = await quoteTask;
            var messages = await messagesTask;
            var fundamentalSnapshot = fundamentalSnapshotTask is null ? null : await fundamentalSnapshotTask;
            var detail = new StockDetailSummaryDto(quote, messages, fundamentalSnapshot);
            if (persist is null || persist.Value)
            {
                await syncService.SaveDetailAsync(
                    new StockDetailDto(
                        quote,
                        Array.Empty<KLinePointDto>(),
                        Array.Empty<MinuteLinePointDto>(),
                        messages,
                        fundamentalSnapshot),
                    "day",
                    cancellationToken);
                await historyService.UpsertAsync(quote, cancellationToken);
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

        group.MapGet("/agents/replay/baseline", async (string? symbol, int? take, IStockAgentReplayCalibrationService replayService, HttpContext context) =>
        {
            var result = await replayService.BuildBaselineAsync(symbol, take ?? 80, context.RequestAborted);
            return Results.Ok(result);
        })
        .WithName("GetStockAgentReplayBaseline")
        .WithOpenApi();

        group.MapGet("/mcp/kline", async (string symbol, string? interval, int? count, string? source, string? taskId, IStockCopilotMcpService mcpService, HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            return await StockMcpEndpointExecutor.ExecuteAsync(
                token => mcpService.GetKlineAsync(symbol, interval ?? "day", count ?? 60, source, taskId, token),
                context.RequestAborted);
        })
        .WithName("RunStockKlineMcp")
        .WithOpenApi();

        group.MapGet("/mcp/minute", async (string symbol, string? source, string? taskId, IStockCopilotMcpService mcpService, HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            return await StockMcpEndpointExecutor.ExecuteAsync(
                token => mcpService.GetMinuteAsync(symbol, source, taskId, token),
                context.RequestAborted);
        })
        .WithName("RunStockMinuteMcp")
        .WithOpenApi();

        group.MapGet("/mcp/strategy", async (string symbol, string? interval, int? count, string? source, string? strategies, string? taskId, IStockCopilotMcpService mcpService, HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var strategyList = string.IsNullOrWhiteSpace(strategies)
                ? Array.Empty<string>()
                : strategies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return await StockMcpEndpointExecutor.ExecuteAsync(
                token => mcpService.GetStrategyAsync(symbol, interval ?? "day", count ?? 90, source, strategyList, taskId, token),
                context.RequestAborted);
        })
        .WithName("RunStockStrategyMcp")
        .WithOpenApi();

        group.MapGet("/mcp/news", async (string symbol, string? level, string? taskId, IStockCopilotMcpService mcpService, HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(symbol) && !string.Equals(level, "market", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            return await StockMcpEndpointExecutor.ExecuteAsync(
                token => mcpService.GetNewsAsync(string.IsNullOrWhiteSpace(symbol) ? "market" : symbol, level ?? "stock", taskId, token),
                context.RequestAborted);
        })
        .WithName("RunStockNewsMcp")
        .WithOpenApi();

        group.MapGet("/mcp/search", async (string q, bool? trustedOnly, string? taskId, IStockCopilotMcpService mcpService, HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return Results.BadRequest(new { message = "q 不能为空" });
            }

            return await StockMcpEndpointExecutor.ExecuteAsync(
                token => mcpService.SearchAsync(q.Trim(), trustedOnly ?? true, taskId, token),
                context.RequestAborted);
        })
        .WithName("RunStockSearchMcp")
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

        group.MapGet("/plans", async (string? symbol, int? take, ITradingPlanService tradingPlanService, IStockMarketContextService marketContextService) =>
        {
            var list = await tradingPlanService.GetListAsync(symbol, take ?? 20);
            var currentContexts = await Task.WhenAll(list.Select(item => marketContextService.GetLatestAsync(item.Symbol)));
            return Results.Ok(list.Select((item, index) => MapTradingPlanDto(item, null, currentContexts[index])).ToArray());
        })
        .WithName("GetTradingPlans")
        .WithOpenApi();

        group.MapGet("/plans/alerts", async (string? symbol, long? planId, int? take, ITradingPlanTriggerService tradingPlanTriggerService) =>
        {
            var list = await tradingPlanTriggerService.GetEventsAsync(symbol, planId, take ?? 20);
            return Results.Ok(list.Select(item => MapTradingPlanEventDto(item)).ToArray());
        })
        .WithName("GetTradingPlanAlerts")
        .WithOpenApi();

        group.MapGet("/plans/{id:long}", async (long id, ITradingPlanService tradingPlanService, IStockMarketContextService marketContextService) =>
        {
            var item = await tradingPlanService.GetByIdAsync(id);
            if (item is null)
            {
                return Results.NotFound();
            }

            var currentContext = await marketContextService.GetLatestAsync(item.Symbol);
            return Results.Ok(MapTradingPlanDto(item, null, currentContext));
        })
        .WithName("GetTradingPlanById")
        .WithOpenApi();

        group.MapPost("/plans/draft", async (TradingPlanDraftRequestDto request, ITradingPlanDraftService draftService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            if (request.AnalysisHistoryId <= 0)
            {
                return Results.BadRequest(new { message = "analysisHistoryId 无效" });
            }

            try
            {
                var draft = await draftService.BuildDraftAsync(request.Symbol, request.AnalysisHistoryId);
                return Results.Ok(draft);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("BuildTradingPlanDraft")
        .WithOpenApi();

        group.MapPost("/plans", async (TradingPlanCreateDto request, ITradingPlanService tradingPlanService, IStockMarketContextService marketContextService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            if (request.AnalysisHistoryId <= 0)
            {
                return Results.BadRequest(new { message = "analysisHistoryId 无效" });
            }

            try
            {
                var result = await tradingPlanService.CreateAsync(request);
                var currentContext = await marketContextService.GetLatestAsync(result.Plan.Symbol);
                return Results.Ok(MapTradingPlanDto(result.Plan, result.WatchlistEnsured, currentContext));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("CreateTradingPlan")
        .WithOpenApi();

        group.MapPut("/plans/{id:long}", async (long id, TradingPlanUpdateDto request, ITradingPlanService tradingPlanService, IStockMarketContextService marketContextService) =>
        {
            try
            {
                var item = await tradingPlanService.UpdateAsync(id, request);
                if (item is null)
                {
                    return Results.NotFound();
                }

                var currentContext = await marketContextService.GetLatestAsync(item.Symbol);
                return Results.Ok(MapTradingPlanDto(item, null, currentContext));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("UpdateTradingPlan")
        .WithOpenApi();

        group.MapPost("/plans/{id:long}/cancel", async (long id, ITradingPlanService tradingPlanService, IStockMarketContextService marketContextService) =>
        {
            var item = await tradingPlanService.CancelAsync(id);
            if (item is null)
            {
                return Results.NotFound();
            }

            var currentContext = await marketContextService.GetLatestAsync(item.Symbol);
            return Results.Ok(MapTradingPlanDto(item, null, currentContext));
        })
        .WithName("CancelTradingPlan")
        .WithOpenApi();

        group.MapPost("/plans/{id:long}/resume", async (long id, ITradingPlanService tradingPlanService, IStockMarketContextService marketContextService) =>
        {
            try
            {
                var item = await tradingPlanService.ResumeAsync(id);
                if (item is null)
                {
                    return Results.NotFound();
                }

                var currentContext = await marketContextService.GetLatestAsync(item.Symbol);
                return Results.Ok(MapTradingPlanDto(item, null, currentContext));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("ResumeTradingPlan")
        .WithOpenApi();

        group.MapDelete("/plans/{id:long}", async (long id, ITradingPlanService tradingPlanService) =>
        {
            var removed = await tradingPlanService.DeleteAsync(id);
            return removed ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteTradingPlan")
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

        group.MapPost("/copilot/turns/draft", async (StockCopilotTurnDraftRequestDto request, IStockCopilotSessionService copilotSessionService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            if (string.IsNullOrWhiteSpace(request.Question))
            {
                return Results.BadRequest(new { message = "question 不能为空" });
            }

            try
            {
                var result = await copilotSessionService.BuildDraftTurnAsync(request);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("BuildStockCopilotDraftTurn")
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
        group.MapGet("/detail/cache", async (string symbol, string? interval, int? count, bool? includeLegacyCharts, AppDbContext dbContext) =>
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

            var companyProfile = await dbContext.StockCompanyProfiles
                .Where(x => x.Symbol == target)
                .FirstOrDefaultAsync();

            if (quote is null)
            {
                return Results.NotFound();
            }

            var messages = await dbContext.IntradayMessages
                .AsNoTracking()
                .Where(x => x.Symbol == target)
                .OrderByDescending(x => x.PublishedAt)
                .Take(20)
                .Select(x => new IntradayMessageDto(x.Title, x.Source, x.PublishedAt, x.Url))
                .ToListAsync();

            var quoteDto = new StockQuoteDto(quote.Symbol, quote.Name, quote.Price, quote.Change, quote.ChangePercent,
                0m, quote.PeRatio, 0m, 0m, 0m, quote.Timestamp, Array.Empty<StockNewsDto>(), Array.Empty<StockIndicatorDto>(),
                quote.FloatMarketCap, quote.VolumeRatio, quote.ShareholderCount ?? companyProfile?.ShareholderCount, quote.SectorName ?? companyProfile?.SectorName);
            var fundamentalSnapshot = StockFundamentalSnapshotMapper.FromProfile(companyProfile);

            if (includeLegacyCharts is not true)
            {
                return Results.Ok(new StockDetailSummaryDto(quoteDto, messages, fundamentalSnapshot));
            }

            var intervalValue = string.IsNullOrWhiteSpace(interval) ? "day" : interval.Trim().ToLowerInvariant();
            var take = Math.Max(10, count ?? 60);

            var kline = await StockDetailCacheQueries.GetRecentKLinesAsync(dbContext, target, intervalValue, take);
            var minute = await StockDetailCacheQueries.GetLatestMinuteLinesAsync(dbContext, target);
            var mergedKLine = string.Equals(intervalValue, "day", StringComparison.OrdinalIgnoreCase)
                ? StockRealtimeKLineMerge.MergeDailyFromMinuteLines(kline, minute, take)
                : kline;

            return Results.Ok(new StockDetailDto(quoteDto, mergedKLine, minute, messages, fundamentalSnapshot));
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

    private static TradingPlanItemDto MapTradingPlanDto(Data.Entities.TradingPlan item, bool? watchlistEnsured = null, StockMarketContextDto? currentMarketContext = null)
    {
        return new TradingPlanItemDto(
            item.Id,
            item.Symbol,
            item.Name,
            item.Direction.ToString(),
            NormalizeTradingPlanStatus(item.Status).ToString(),
            item.TriggerPrice,
            item.InvalidPrice,
            item.StopLossPrice,
            item.TakeProfitPrice,
            item.TargetPrice,
            item.ExpectedCatalyst,
            item.InvalidConditions,
            item.RiskLimits,
            item.AnalysisSummary,
            item.AnalysisHistoryId,
            item.SourceAgent,
            item.UserNote,
            item.CreatedAt,
            item.UpdatedAt,
            item.TriggeredAt,
            item.InvalidatedAt,
            item.CancelledAt,
            watchlistEnsured,
            BuildCreationMarketContext(item),
            currentMarketContext);
    }

    private static StockMarketContextDto? BuildCreationMarketContext(Data.Entities.TradingPlan item)
    {
        if (string.IsNullOrWhiteSpace(item.MarketStageLabelAtCreation)
            && item.StageConfidenceAtCreation is null
            && item.SuggestedPositionScale is null
            && string.IsNullOrWhiteSpace(item.ExecutionFrequencyLabel)
            && string.IsNullOrWhiteSpace(item.MainlineSectorName)
            && item.MainlineScoreAtCreation is null)
        {
            return null;
        }

        return new StockMarketContextDto(
            item.MarketStageLabelAtCreation ?? "混沌",
            item.StageConfidenceAtCreation ?? 0m,
            item.SectorNameAtCreation,
            item.MainlineSectorName,
            item.SectorCodeAtCreation,
            item.MainlineScoreAtCreation ?? 0m,
            item.SuggestedPositionScale ?? 0m,
            item.ExecutionFrequencyLabel ?? string.Empty,
            false,
            !string.IsNullOrWhiteSpace(item.MainlineSectorName)
                && !string.IsNullOrWhiteSpace(item.SectorNameAtCreation)
                && (item.MainlineSectorName.Contains(item.SectorNameAtCreation, StringComparison.OrdinalIgnoreCase)
                    || item.SectorNameAtCreation.Contains(item.MainlineSectorName, StringComparison.OrdinalIgnoreCase)));
    }

    private static TradingPlanEventItemDto MapTradingPlanEventDto(Data.Entities.TradingPlanEvent item)
    {
        return new TradingPlanEventItemDto(
            item.Id,
            item.PlanId,
            item.Symbol,
            item.EventType.ToString(),
            item.Severity.ToString(),
            item.Message,
            item.SnapshotPrice,
            item.MetadataJson,
            item.OccurredAt);
    }

    private static Data.Entities.TradingPlanStatus NormalizeTradingPlanStatus(Data.Entities.TradingPlanStatus status)
    {
        return status == Data.Entities.TradingPlanStatus.Draft
            ? Data.Entities.TradingPlanStatus.Pending
            : status;
    }
}
