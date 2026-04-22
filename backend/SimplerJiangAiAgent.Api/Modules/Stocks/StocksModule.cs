using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Contracts;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend.WebSearch;
using SimplerJiangAiAgent.Api.Modules.Market.Services;
using SimplerJiangAiAgent.Api.Infrastructure;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs.ForumScraping;

namespace SimplerJiangAiAgent.Api.Modules.Stocks;

public sealed class StocksModule : IModule
{
    private static readonly SemaphoreSlim _concurrentTurns = new(5, 5);
    private static readonly ConcurrentDictionary<long, CancellationTokenSource> _turnCancellationSources = new();
    private static readonly TimeSpan FinancialWorkerProxyTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FinancialWorkerCollectProxyTimeout = TimeSpan.FromSeconds(60);

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        // 爬虫配置（预留反爬/代理池）
        services.Configure<StockCrawlerOptions>(configuration.GetSection(StockCrawlerOptions.SectionName));
        var crawlerOptions = configuration.GetSection(StockCrawlerOptions.SectionName).Get<StockCrawlerOptions>() ?? new StockCrawlerOptions();
        var stockHttpTimeout = TimeSpan.FromSeconds(Math.Clamp(crawlerOptions.HttpTimeoutSeconds, 5, 60));

        // 来源爬虫（占位实现，后续替换为真实解析逻辑）
        services.AddHttpClient();
        services.AddHttpClient<EastmoneyStockCrawler>(client => ConfigureStockHttpClient(client, stockHttpTimeout));
        services.AddHttpClient<TencentStockCrawler>(client => ConfigureStockHttpClient(client, stockHttpTimeout));
        services.AddHttpClient<SinaStockCrawler>(client => ConfigureStockHttpClient(client, stockHttpTimeout));
        services.AddTransient<BaiduStockCrawler>();
        services.AddTransient<IStockCrawlerSource>(serviceProvider => serviceProvider.GetRequiredService<EastmoneyStockCrawler>());
        services.AddTransient<IStockCrawlerSource>(serviceProvider => serviceProvider.GetRequiredService<TencentStockCrawler>());
        services.AddTransient<IStockCrawlerSource>(serviceProvider => serviceProvider.GetRequiredService<SinaStockCrawler>());
        services.AddTransient<IStockCrawlerSource>(serviceProvider => serviceProvider.GetRequiredService<BaiduStockCrawler>());
        services.AddHttpClient<IStockFundamentalSnapshotService, EastmoneyFundamentalSnapshotService>(client => ConfigureStockHttpClient(client, stockHttpTimeout));
        services.AddSingleton<IStockCrawler, CompositeStockCrawler>();
        services.Configure<HighFrequencyQuoteOptions>(configuration.GetSection(HighFrequencyQuoteOptions.SectionName));
        services.Configure<TradingPlanTriggerOptions>(configuration.GetSection(TradingPlanTriggerOptions.SectionName));
        services.Configure<TradingPlanReviewOptions>(configuration.GetSection(TradingPlanReviewOptions.SectionName));
        services.Configure<StockCopilotSearchOptions>(configuration.GetSection(StockCopilotSearchOptions.SectionName));
        services.AddScoped<IActiveWatchlistService, ActiveWatchlistService>();
        services.AddHttpClient<ILocalFactIngestionService, LocalFactIngestionService>(client => ConfigureStockHttpClient(client, stockHttpTimeout));
        services.AddScoped<ILocalFactAiEnrichmentService, LocalFactAiEnrichmentService>();
        services.AddSingleton<ILocalFactArchiveJobCoordinator, LocalFactArchiveJobCoordinator>();
        services.AddHttpClient<ILocalFactArticleReadService, LocalFactArticleReadService>(client => ConfigureStockHttpClient(client, stockHttpTimeout));
        services.AddScoped<IQueryLocalFactDatabaseTool, QueryLocalFactDatabaseTool>();
        services.AddScoped<IStockDataService, StockDataService>();
        services.AddScoped<IStockHistoryService, StockHistoryService>();
        services.AddScoped<IFinancialDataReadService, FinancialDataReadService>();
        services.AddScoped<IPdfFileQueryService, PdfFileQueryService>();
        services.AddSingleton<IPdfReparseGateway, HttpPdfReparseGateway>();
        services.AddHttpClient<IStockSearchService, StockSearchService>(client => ConfigureStockHttpClient(client, stockHttpTimeout));
        services.AddScoped<IStockAgentHistoryService, StockAgentHistoryService>();
        services.AddScoped<IStockAgentFeatureEngineeringService, StockAgentFeatureEngineeringService>();
        services.AddScoped<IStockAgentOrchestrator, StockAgentOrchestrator>();
        services.AddScoped<ITradingPlanDraftService, TradingPlanDraftService>();
        services.AddScoped<ITradingPlanService, TradingPlanService>();
        services.AddScoped<ITradeExecutionInsightService, TradeExecutionInsightService>();
        services.AddScoped<IStockMarketContextService, StockMarketContextService>();
        services.AddScoped<IStockChatHistoryService, StockChatHistoryService>();
        services.AddScoped<IStockCopilotMcpService, StockCopilotMcpService>();
        services.AddScoped<IStockCopilotSessionService, StockCopilotSessionService>();
        services.AddScoped<IStockCopilotLiveGateService, StockCopilotLiveGateService>();
        services.AddScoped<IStockCopilotAcceptanceService, StockCopilotAcceptanceService>();
        services.AddScoped<IStockAgentReplayCalibrationService, StockAgentReplayCalibrationService>();
        services.AddScoped<IMcpToolGateway, McpToolGateway>();
        services.AddSingleton<IMcpServiceRegistry, McpServiceRegistry>();
        services.AddSingleton<IRoleToolPolicyService, RoleToolPolicyService>();
        services.AddSingleton<IStockAgentRoleContractRegistry, StockAgentRoleContractRegistry>();
        services.AddScoped<ITradingPlanTriggerService, TradingPlanTriggerService>();
        services.AddScoped<ITradingPlanReviewService, TradingPlanReviewService>();
        services.AddScoped<IStockNewsImpactService, StockNewsImpactService>();
        services.AddScoped<IStockSignalService, StockSignalService>();
        services.AddScoped<IStockPositionGuidanceService, StockPositionGuidanceService>();
        services.AddScoped<IResearchSessionService, ResearchSessionService>();
        services.AddScoped<IResearchFollowUpRoutingService, ResearchFollowUpRoutingService>();
        services.AddScoped<IResearchRoleExecutor, ResearchRoleExecutor>();
        services.AddScoped<IResearchRunner, ResearchRunner>();
        services.AddSingleton<IResearchEventBus, ResearchEventBus>();
        services.AddScoped<IResearchArtifactService, ResearchArtifactService>();
        services.AddScoped<IResearchReportService, ResearchReportService>();
        services.AddSingleton<Infrastructure.Logging.ISessionFileLogger, Infrastructure.Logging.SessionFileLogger>();
        services.AddSingleton<IRecommendEventBus, RecommendEventBus>();
        services.AddSingleton<IRecommendRoleContractRegistry, RecommendRoleContractRegistry>();
        services.AddScoped<IRecommendToolDispatcher, RecommendToolDispatcher>();
        services.AddScoped<IRecommendationRoleExecutor, RecommendationRoleExecutor>();
        services.AddScoped<IRecommendationRunner, RecommendationRunner>();
        services.AddScoped<IRecommendFollowUpRouter, RecommendFollowUpRouter>();
        services.AddScoped<IRecommendationSessionService, RecommendationSessionService>();
        services.AddSingleton<IJsonKeyTranslationService, JsonKeyTranslationService>();
        services.AddHttpClient("WebSearch");
        services.AddSingleton<TavilySearchClient>();
        services.AddSingleton<SearxngSearchClient>();
        services.AddSingleton<DuckDuckGoSearchClient>();
        services.AddSingleton<IWebSearchService, WebSearchService>();
        services.AddHostedService<TradingPlanTriggerWorker>();
        services.AddHostedService<TradingPlanReviewWorker>();

        // Forum scraping
        services.AddHttpClient("EastmoneyGuba", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            client.Timeout = TimeSpan.FromSeconds(15);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            UseProxy = false
        });
        services.AddHttpClient("SinaGuba", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            client.Timeout = TimeSpan.FromSeconds(15);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            UseProxy = false,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        });
        services.AddHttpClient("TaogubaGuba", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            client.Timeout = TimeSpan.FromSeconds(15);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            UseProxy = false,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        });
        services.AddTransient<IForumPostCountScraper, EastmoneyGubaScraper>();
        services.AddTransient<IForumPostCountScraper, SinaGubaScraper>();
        services.AddTransient<IForumPostCountScraper, TaogubaScraper>();
        services.AddScoped<IForumScrapingService, ForumScrapingService>();
        services.AddScoped<IRetailHeatIndexService, RetailHeatIndexService>();
        services.AddScoped<IHistoricalBackfillService, HistoricalBackfillService>();
        services.AddSingleton<IBackfillStatusTracker, BackfillStatusTracker>();
        services.AddHostedService<ForumScrapingWorker>();

        // FinancialWorker process supervisor
        services.AddSingleton<FinancialWorkerSupervisorService>();
        services.AddSingleton<IFinancialWorkerSupervisor>(sp => sp.GetRequiredService<FinancialWorkerSupervisorService>());
        services.AddHostedService(sp => sp.GetRequiredService<FinancialWorkerSupervisorService>());
    }

    private static void ConfigureStockHttpClient(HttpClient client, TimeSpan timeout)
    {
        client.Timeout = timeout;
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stocks");

        var financialGroup = group.MapGroup("/financial");

        financialGroup.MapGet("/trend/{symbol}", (string symbol, IFinancialDataReadService financialDataReadService) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var trimmedSymbol = symbol.Trim();
            var result = financialDataReadService.GetTrendSummary(trimmedSymbol) ?? new FinancialTrendSummary { Symbol = trimmedSymbol };
            return Results.Ok(result);
        })
        .WithName("GetStockFinancialTrend")
        .WithOpenApi();

        financialGroup.MapGet("/summary/{symbol}", (string symbol, IFinancialDataReadService financialDataReadService) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var trimmedSymbol = symbol.Trim();
            var result = financialDataReadService.GetReportSummary(trimmedSymbol) ?? new FinancialReportSummary { Symbol = trimmedSymbol };
            return Results.Ok(result);
        })
        .WithName("GetStockFinancialSummary")
        .WithOpenApi();

        financialGroup.MapGet("/reports", (
            [AsParameters] FinancialReportListQuery query,
            IFinancialDataReadService financialDataReadService) =>
        {
            return Results.Ok(financialDataReadService.ListReports(query));
        })
        .WithName("ListStockFinancialReports")
        .WithOpenApi();

        financialGroup.MapGet("/reports/{id}", (string id, IFinancialDataReadService financialDataReadService) =>
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return Results.BadRequest(new { message = "id 不能为空" });
            }

            var detail = financialDataReadService.GetReportById(id.Trim());
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        })
        .WithName("GetStockFinancialReportById")
        .WithOpenApi();

        // ── v0.4.1 §S2：PDF 文件 4 接口 ──
        financialGroup.MapGet("/pdf-files", (
            string? symbol,
            string? reportType,
            int? page,
            int? pageSize,
            IPdfFileQueryService pdfFileQuery) =>
        {
            var query = new PdfFileListQuery(
                Symbol: string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim(),
                ReportType: string.IsNullOrWhiteSpace(reportType) ? null : reportType.Trim(),
                Page: Math.Max(1, page ?? 1),
                PageSize: Math.Clamp(pageSize ?? 20, 1, 100));
            return Results.Ok(pdfFileQuery.List(query));
        })
        .WithName("ListPdfFiles")
        .WithOpenApi();

        financialGroup.MapGet("/pdf-files/{id}", (string id, IPdfFileQueryService pdfFileQuery) =>
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return Results.BadRequest(new { message = "id 不能为空" });
            }
            var detail = pdfFileQuery.GetById(id.Trim());
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        })
        .WithName("GetPdfFileById")
        .WithOpenApi();

        financialGroup.MapGet("/pdf-files/{id}/content", (string id, HttpContext httpContext, IPdfFileQueryService pdfFileQuery) =>
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return Results.BadRequest(new { message = "id 不能为空" });
            }
            var resolution = pdfFileQuery.ResolveContent(id.Trim());
            switch (resolution.Status)
            {
                case "found":
                    var safeName = string.IsNullOrWhiteSpace(resolution.AccessKey) ? "document.pdf" : resolution.AccessKey;
                    httpContext.Response.Headers["Content-Disposition"] = $"inline; filename=\"{safeName}\"";
                    return Results.File(
                        resolution.FullPath!,
                        contentType: "application/pdf",
                        fileDownloadName: null,
                        enableRangeProcessing: true);
                case "forbidden":
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                default:
                    return Results.NotFound();
            }
        })
        .WithName("GetPdfFileContent")
        .WithOpenApi();

        financialGroup.MapPost("/pdf-files/{id}/reparse", async (
            string id,
            IPdfReparseGateway gateway,
            IPdfFileQueryService pdfFileQuery,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return Results.BadRequest(new { message = "id 不能为空" });
            }

            var trimmed = id.Trim();
            var pre = pdfFileQuery.GetById(trimmed);
            if (pre is null)
            {
                return Results.NotFound();
            }

            var gatewayResult = await gateway.ReparseAsync(trimmed, ct);
            if (!gatewayResult.DocumentFound)
            {
                return Results.NotFound();
            }
            if (gatewayResult.PhysicalFileMissing)
            {
                return Results.NotFound();
            }

            // 重读详情，确保返回最新 stageLogs / parseUnits
            var detail = pdfFileQuery.GetById(trimmed) ?? pre;
            var response = new PdfFileReparseResponse(
                Success: gatewayResult.Success,
                Error: gatewayResult.Success ? null : gatewayResult.Error,
                Detail: detail);
            return Results.Ok(response);
        })
        .WithName("ReparsePdfFile")
        .WithOpenApi();

        financialGroup.MapPost("/collect/{symbol}", async (string symbol, IConfiguration configuration, IHttpClientFactory httpClientFactory, HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空", errorMessage = "symbol 不能为空" });
            }

            var workerSymbol = NormalizeFinancialWorkerSymbol(symbol);
            if (string.IsNullOrWhiteSpace(workerSymbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空", errorMessage = "symbol 不能为空" });
            }

            return await ProxyFinancialWorkerAsync(
                HttpMethod.Post,
                $"api/collect/{Uri.EscapeDataString(workerSymbol)}",
                configuration,
                httpClientFactory,
                httpContext.RequestAborted,
                includeCollectErrorFields: true);
        })
        .WithName("CollectStockFinancialData")
        .WithOpenApi();

        financialGroup.MapGet("/logs", (string? symbol, int? limit, IFinancialDataReadService financialDataReadService) =>
        {
            var trimmedSymbol = string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim();
            var take = Math.Clamp(limit ?? 50, 1, 200);
            return Results.Ok(financialDataReadService.GetCollectionLogs(trimmedSymbol, take));
        })
        .WithName("GetStockFinancialLogs")
        .WithOpenApi();

        financialGroup.MapGet("/worker/status", async (IConfiguration configuration, IHttpClientFactory httpClientFactory, HttpContext httpContext) =>
        {
            return await ProxyFinancialWorkerAsync(
                HttpMethod.Get,
                "health",
                configuration,
                httpClientFactory,
                httpContext.RequestAborted);
        })
        .WithName("GetStockFinancialWorkerStatus")
        .WithOpenApi();

        // FinancialWorker 进程管理 (supervisor)
        financialGroup.MapGet("/worker/supervisor-status", (IFinancialWorkerSupervisor supervisor) =>
        {
            var status = supervisor.GetStatus();
            return Results.Ok(status);
        })
        .WithName("GetFinancialWorkerSupervisorStatus")
        .WithOpenApi();

        financialGroup.MapPost("/worker/start", async (IFinancialWorkerSupervisor supervisor, CancellationToken ct) =>
        {
            await supervisor.StartWorkerAsync(ct);
            return Results.Ok(new { message = "启动指令已发送", status = supervisor.GetStatus() });
        })
        .WithName("StartFinancialWorker")
        .WithOpenApi();

        financialGroup.MapPost("/worker/stop", async (IFinancialWorkerSupervisor supervisor, CancellationToken ct) =>
        {
            await supervisor.StopWorkerAsync(ct);
            return Results.Ok(new { message = "停止指令已发送", status = supervisor.GetStatus() });
        })
        .WithName("StopFinancialWorker")
        .WithOpenApi();

        financialGroup.MapPost("/worker/restart", async (IFinancialWorkerSupervisor supervisor, CancellationToken ct) =>
        {
            await supervisor.RestartWorkerAsync(ct);
            return Results.Ok(new { message = "重启指令已发送", status = supervisor.GetStatus() });
        })
        .WithName("RestartFinancialWorker")
        .WithOpenApi();

        financialGroup.MapGet("/worker/runtime-logs", async (long? afterId, int? count, IConfiguration configuration, IHttpClientFactory httpClientFactory, HttpContext httpContext) =>
        {
            var qs = new List<string>();
            if (afterId.HasValue) qs.Add($"afterId={afterId.Value}");
            if (count.HasValue) qs.Add($"count={count.Value}");
            var queryString = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
            return await ProxyFinancialWorkerAsync(
                HttpMethod.Get,
                $"api/runtime-logs{queryString}",
                configuration,
                httpClientFactory,
                httpContext.RequestAborted);
        })
        .WithName("GetFinancialWorkerRuntimeLogs")
        .WithOpenApi();

        financialGroup.MapGet("/config", async (IConfiguration configuration, IHttpClientFactory httpClientFactory, HttpContext httpContext) =>
        {
            return await ProxyFinancialWorkerAsync(
                HttpMethod.Get,
                "api/config",
                configuration,
                httpClientFactory,
                httpContext.RequestAborted);
        })
        .WithName("GetStockFinancialConfig")
        .WithOpenApi();

        financialGroup.MapPut("/config", async (JsonElement payload, IConfiguration configuration, IHttpClientFactory httpClientFactory, HttpContext httpContext) =>
        {
            return await ProxyFinancialWorkerAsync(
                HttpMethod.Put,
                "api/config",
                configuration,
                httpClientFactory,
                httpContext.RequestAborted,
                payload);
        })
        .WithName("UpdateStockFinancialConfig")
        .WithOpenApi();

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

        // 散户热度指标
        group.MapGet("/{symbol}/retail-heat", async (
            string symbol,
            string? from,
            string? to,
            IRetailHeatIndexService retailHeatService,
            IBackfillStatusTracker backfillStatusTracker,
            IServiceProvider serviceProvider,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            DateOnly? fromDate = null, toDate = null;
            if (from is not null)
            {
                if (!DateOnly.TryParse(from, out var f)) return Results.BadRequest("Invalid 'from' date format.");
                fromDate = f;
            }
            if (to is not null)
            {
                if (!DateOnly.TryParse(to, out var t)) return Results.BadRequest("Invalid 'to' date format.");
                toDate = t;
            }
            var result = await retailHeatService.GetTimeSeriesAsync(symbol, fromDate, toDate, ct);

            // 数据不足30天时自动触发后台历史回填
            if (result.Data.Count < 30 && !backfillStatusTracker.IsBackfilling(result.Symbol))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = serviceProvider.CreateScope();
                        var backfillService = scope.ServiceProvider.GetRequiredService<IHistoricalBackfillService>();
                        await backfillService.BackfillAsync(symbol, 90, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger("RetailHeatAutoBackfill");
                        logger?.LogWarning(ex, "自动回填失败: {Symbol}", symbol);
                    }
                });
            }

            var isBackfilling = backfillStatusTracker.IsBackfilling(result.Symbol);
            return Results.Ok(new { result.Symbol, result.Data, result.Latest, result.Description, Backfilling = isBackfilling });
        })
        .WithName("GetRetailHeatIndex")
        .WithOpenApi();

        group.MapPost("/retail-heat/initial-collect", async (
            IForumScrapingService forumScrapingService,
            CancellationToken ct) =>
        {
            await forumScrapingService.InitialCollectAsync(ct);
            return Results.Ok(new { message = "Initial collect completed" });
        })
        .WithName("InitialCollectRetailHeat")
        .WithOpenApi();

        group.MapPost("/{symbol}/retail-heat/backfill", async (
            string symbol,
            int? days,
            IHistoricalBackfillService backfillService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return Results.BadRequest(new { message = "symbol 不能为空" });

            await backfillService.BackfillAsync(symbol, days ?? 90, ct);
            return Results.Ok(new { message = $"Backfill completed for {symbol}" });
        })
        .WithName("BackfillRetailHeat")
        .WithOpenApi();

        group.MapPost("/retail-heat/backfill-all", async (
            int? days,
            IHistoricalBackfillService backfillService,
            CancellationToken ct) =>
        {
            await backfillService.BackfillAllWatchlistAsync(days ?? 60, ct);
            return Results.Ok(new { message = "Backfill all completed" });
        })
        .WithName("BackfillAllRetailHeat")
        .WithOpenApi();

        group.MapGet("/mcp/kline", async (string symbol, string? interval, int? count, string? source, string? taskId, int? evidenceSkip, int? evidenceTake, IMcpToolGateway gateway, HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            return await StockMcpEndpointExecutor.ExecuteAsync(
                cancellationToken => gateway.GetKlineAsync(symbol.Trim(), interval ?? "day", count ?? 60, source, taskId, CreateMcpWindowOptions(evidenceSkip, evidenceTake), cancellationToken),
                httpContext.RequestAborted);
        })
        .WithName("GetStockKlineMcp")
        .WithOpenApi();

        group.MapGet("/mcp/company-overview", async (string symbol, string? taskId, int? evidenceSkip, int? evidenceTake, IMcpToolGateway gateway, HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            return await StockMcpEndpointExecutor.ExecuteAsync(
                cancellationToken => gateway.GetCompanyOverviewAsync(symbol.Trim(), taskId, CreateMcpWindowOptions(evidenceSkip, evidenceTake), cancellationToken),
                httpContext.RequestAborted);
        })
        .WithName("GetStockCompanyOverviewMcp")
        .WithOpenApi();

        group.MapGet("/mcp/product", async (string symbol, string? taskId, int? evidenceSkip, int? evidenceTake, IMcpToolGateway gateway, HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            return await StockMcpEndpointExecutor.ExecuteAsync(
                cancellationToken => gateway.GetProductAsync(symbol.Trim(), taskId, CreateMcpWindowOptions(evidenceSkip, evidenceTake), cancellationToken),
                httpContext.RequestAborted);
        })
        .WithName("GetStockProductMcp")
        .WithOpenApi();

        group.MapGet("/mcp/fundamentals", async (string symbol, string? taskId, int? evidenceSkip, int? evidenceTake, int? factSkip, int? factTake, IMcpToolGateway gateway, HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            return await StockMcpEndpointExecutor.ExecuteAsync(
                cancellationToken => gateway.GetFundamentalsAsync(symbol.Trim(), taskId, CreateMcpWindowOptions(evidenceSkip, evidenceTake, factSkip, factTake), cancellationToken),
                httpContext.RequestAborted);
        })
        .WithName("GetStockFundamentalsMcp")
        .WithOpenApi();

        group.MapGet("/mcp/shareholder", async (string symbol, string? taskId, int? evidenceSkip, int? evidenceTake, IMcpToolGateway gateway, HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            return await StockMcpEndpointExecutor.ExecuteAsync(
                cancellationToken => gateway.GetShareholderAsync(symbol.Trim(), taskId, CreateMcpWindowOptions(evidenceSkip, evidenceTake), cancellationToken),
                httpContext.RequestAborted);
        })
        .WithName("GetStockShareholderMcp")
        .WithOpenApi();

        group.MapGet("/mcp/market-context", async (string symbol, string? taskId, int? evidenceSkip, int? evidenceTake, IMcpToolGateway gateway, HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            return await StockMcpEndpointExecutor.ExecuteAsync(
                cancellationToken => gateway.GetMarketContextAsync(symbol.Trim(), taskId, CreateMcpWindowOptions(evidenceSkip, evidenceTake), cancellationToken),
                httpContext.RequestAborted);
        })
        .WithName("GetStockMarketContextMcp")
        .WithOpenApi();

        group.MapGet("/mcp/social-sentiment", async (string symbol, string? taskId, int? evidenceSkip, int? evidenceTake, IMcpToolGateway gateway, HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            return await StockMcpEndpointExecutor.ExecuteAsync(
                cancellationToken => gateway.GetSocialSentimentAsync(symbol.Trim(), taskId, CreateMcpWindowOptions(evidenceSkip, evidenceTake), cancellationToken),
                httpContext.RequestAborted);
        })
        .WithName("GetStockSocialSentimentMcp")
        .WithOpenApi();

        group.MapGet("/mcp/minute", async (string symbol, string? source, string? taskId, int? evidenceSkip, int? evidenceTake, IMcpToolGateway gateway, HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            return await StockMcpEndpointExecutor.ExecuteAsync(
                cancellationToken => gateway.GetMinuteAsync(symbol.Trim(), source, taskId, CreateMcpWindowOptions(evidenceSkip, evidenceTake), cancellationToken),
                httpContext.RequestAborted);
        })
        .WithName("GetStockMinuteMcp")
        .WithOpenApi();

        group.MapGet("/mcp/strategy", async (string symbol, string? interval, int? count, string? source, string[]? strategies, string? taskId, int? evidenceSkip, int? evidenceTake, IMcpToolGateway gateway, HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            return await StockMcpEndpointExecutor.ExecuteAsync(
                cancellationToken => gateway.GetStrategyAsync(symbol.Trim(), interval ?? "day", count ?? 60, source, strategies, taskId, CreateMcpWindowOptions(evidenceSkip, evidenceTake), cancellationToken),
                httpContext.RequestAborted);
        })
        .WithName("GetStockStrategyMcp")
        .WithOpenApi();

        group.MapGet("/mcp/news", async (string symbol, string? level, string? taskId, int? evidenceSkip, int? evidenceTake, IMcpToolGateway gateway, HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            return await StockMcpEndpointExecutor.ExecuteAsync(
                cancellationToken => gateway.GetNewsAsync(symbol.Trim(), level ?? "stock", taskId, CreateMcpWindowOptions(evidenceSkip, evidenceTake), cancellationToken),
                httpContext.RequestAborted);
        })
        .WithName("GetStockNewsMcp")
        .WithOpenApi();

        group.MapGet("/mcp/search", async (string query, bool? trustedOnly, string? taskId, IMcpToolGateway gateway, HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Results.BadRequest(new { message = "query 不能为空" });
            }

            return await StockMcpEndpointExecutor.ExecuteAsync(
                cancellationToken => gateway.SearchAsync(query.Trim(), trustedOnly ?? true, taskId, cancellationToken),
                httpContext.RequestAborted);
        })
        .WithName("SearchStockMcp")
        .WithOpenApi();

        group.MapPost("/copilot/turns/draft", async (StockCopilotTurnDraftRequestDto request, IStockCopilotSessionService sessionService, HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(request.Symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            if (string.IsNullOrWhiteSpace(request.Question))
            {
                return Results.BadRequest(new { message = "question 不能为空" });
            }

            return await StockMcpEndpointExecutor.ExecuteAsync(
                cancellationToken => sessionService.BuildDraftTurnAsync(request, cancellationToken),
                httpContext.RequestAborted);
        })
        .WithName("BuildStockCopilotDraftTurn")
        .WithOpenApi();

        group.MapPost("/copilot/live-gate", async (StockCopilotLiveGateRequestDto request, IStockCopilotLiveGateService liveGateService, HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(request.Symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            if (string.IsNullOrWhiteSpace(request.Question))
            {
                return Results.BadRequest(new { message = "question 不能为空" });
            }

            return await StockMcpEndpointExecutor.ExecuteAsync(
                cancellationToken => liveGateService.RunAsync(request, cancellationToken),
                httpContext.RequestAborted);
        })
        .WithName("RunStockCopilotLiveGate")
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

        app.MapPost("/api/news/archive/process-pending", async (ILocalFactArchiveJobCoordinator archiveJobCoordinator, CancellationToken cancellationToken) =>
        {
            var status = await archiveJobCoordinator.StartOrResumeAsync(cancellationToken);
            return Results.Ok(status);
        })
        .WithName("ProcessPendingNews")
        .WithOpenApi();

        app.MapPost("/api/news/archive/process-pending/pause", async (ILocalFactArchiveJobCoordinator archiveJobCoordinator, CancellationToken cancellationToken) =>
        {
            var status = await archiveJobCoordinator.PauseAsync(cancellationToken);
            return Results.Ok(status);
        })
        .WithName("PausePendingNews")
        .WithOpenApi();

        app.MapPost("/api/news/archive/process-pending/restart", async (ILocalFactArchiveJobCoordinator archiveJobCoordinator, CancellationToken cancellationToken) =>
        {
            var status = await archiveJobCoordinator.RestartAsync(cancellationToken);
            return Results.Ok(status);
        })
        .WithName("RestartPendingNews")
        .WithOpenApi();

        app.MapGet("/api/news/archive/process-pending/status", (ILocalFactArchiveJobCoordinator archiveJobCoordinator) =>
        {
            var status = archiveJobCoordinator.GetStatus();
            return Results.Ok(status);
        })
        .WithName("GetPendingNewsStatus")
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

        group.MapGet("/market-context", async (string? symbol, IStockMarketContextService marketContextService, HttpContext httpContext) =>
            await GetLatestMarketContextResultAsync(symbol, marketContextService, httpContext.RequestAborted))
        .WithName("GetStockMarketContext")
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

        group.MapGet("/plans", async (string? symbol, int? take, ITradingPlanService tradingPlanService, IStockMarketContextService marketContextService, ITradeExecutionInsightService tradeExecutionInsightService, ILogger<StocksModule> logger, HttpContext httpContext) =>
            await GetTradingPlansResultAsync(symbol, take, tradingPlanService, marketContextService, tradeExecutionInsightService, logger, httpContext.RequestAborted))
        .WithName("GetTradingPlans")
        .WithOpenApi();

        group.MapGet("/plans/alerts", async (string? symbol, long? planId, int? take, ITradingPlanTriggerService tradingPlanTriggerService) =>
        {
            var list = await tradingPlanTriggerService.GetEventsAsync(symbol, planId, take ?? 20);
            return Results.Ok(list.Select(item => MapTradingPlanEventDto(item)).ToArray());
        })
        .WithName("GetTradingPlanAlerts")
        .WithOpenApi();

        group.MapGet("/plans/{id:long}", async (long id, ITradingPlanService tradingPlanService, IStockMarketContextService marketContextService, ITradeExecutionInsightService tradeExecutionInsightService) =>
        {
            var item = await tradingPlanService.GetByIdAsync(id);
            if (item is null)
            {
                return Results.NotFound();
            }

            var currentContext = await marketContextService.GetLatestAsync(item.Symbol);
            var insight = await tradeExecutionInsightService.GetPlanInsightAsync(item);
            return Results.Ok(MapTradingPlanDto(item, null, currentContext, insight));
        })
        .WithName("GetTradingPlanById")
        .WithOpenApi();

        group.MapGet("/plans/{id:long}/execution-context", async (long id, ITradingPlanService tradingPlanService, IStockMarketContextService marketContextService, ITradeExecutionInsightService tradeExecutionInsightService) =>
        {
            var item = await tradingPlanService.GetByIdAsync(id);
            if (item is null)
            {
                return Results.NotFound();
            }

            var currentContext = await marketContextService.GetLatestAsync(item.Symbol);
            var insight = await tradeExecutionInsightService.GetPlanInsightAsync(item, useLiveQuote: true);
            var portfolioSummary = await tradeExecutionInsightService.GetPortfolioSummaryAsync();
            return Results.Ok(new TradingPlanExecutionContextDto(
                MapTradingPlanDto(item, null, currentContext, insight),
                insight?.CurrentScenarioStatus,
                insight?.CurrentPositionSnapshot,
                portfolioSummary,
                insight?.ExecutionSummary));
        })
        .WithName("GetTradingPlanExecutionContext")
        .WithOpenApi();

        group.MapPost("/plans/draft", async (
            TradingPlanDraftRequestDto request,
            ITradingPlanDraftService draftService,
            IStockAgentReplayCalibrationService calibrationService,
            ITradeAccountingService accountingService,
            AppDbContext db) =>
        {
            var validationError = ValidateTradingPlanDraftRequest(request);
            if (validationError is not null)
            {
                return validationError;
            }

            try
            {
                var draft = await draftService.BuildDraftAsync(request.Symbol, request.AnalysisHistoryId);

                SignalHistoryMetricsDto? signalMetrics = null;
                try
                {
                    var baseline = await calibrationService.BuildBaselineAsync(request.Symbol, 80);
                    var horizon5 = baseline.Horizons.FirstOrDefault(h => h.HorizonDays == 5);
                    if (horizon5 is not null && baseline.SampleCount > 0)
                    {
                        var directionText = draft.Direction;
                        var isBull = string.Equals(directionText, "Long", StringComparison.OrdinalIgnoreCase);
                        var hitRate = isBull ? horizon5.BullWinRate : horizon5.BearWinRate;
                        var caveat = baseline.SampleCount < 5 ? "样本不足，仅供参考" : null;
                        signalMetrics = new SignalHistoryMetricsDto(
                            isBull ? "偏多" : "偏空",
                            baseline.SampleCount,
                            hitRate,
                            horizon5.AverageReturnPercent,
                            caveat);
                    }
                }
                catch { /* non-critical */ }

                RealTradeMetricsDto? realTradeMetrics = null;
                try
                {
                    var winRate = await accountingService.GetWinRateAsync(null, null, request.Symbol);
                    if (winRate.TotalTrades > 0)
                    {
                        var caveat = winRate.TotalTrades < 5 ? "交易次数不足，仅供参考" : null;
                        realTradeMetrics = new RealTradeMetricsDto(
                            winRate.TotalTrades,
                            winRate.WinCount,
                            winRate.WinRate,
                            winRate.AveragePnL,
                            winRate.AverageReturnRate,
                            caveat);
                    }
                }
                catch { /* non-critical */ }

                MarketExecutionModeDto? executionMode = null;
                try
                {
                    var latestSentiment = await db.MarketSentimentSnapshots
                        .AsNoTracking()
                        .OrderByDescending(s => s.SnapshotTime)
                        .FirstOrDefaultAsync();
                    if (latestSentiment is not null)
                    {
                        executionMode = MarketExecutionModeMapper.GetMode(latestSentiment.StageLabel);
                    }
                }
                catch { /* non-critical */ }

                return Results.Ok(new TradingPlanDraftResponseDto(draft, signalMetrics, realTradeMetrics, executionMode));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("BuildTradingPlanDraft")
        .WithOpenApi();

        group.MapGet("/agents/signal-track-record", async (string symbol, string? direction, IStockAgentReplayCalibrationService calibrationService) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            try
            {
                var baseline = await calibrationService.BuildBaselineAsync(symbol, 80);
                var isBull = string.IsNullOrWhiteSpace(direction)
                    || string.Equals(direction, "Long", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(direction, "偏多", StringComparison.OrdinalIgnoreCase);

                var horizonMetrics = baseline.Horizons.Select(h => new
                {
                    h.HorizonDays,
                    h.SampleCount,
                    HitRate = isBull ? h.BullWinRate : h.BearWinRate,
                    h.AverageReturnPercent
                }).ToArray();

                return Results.Ok(new
                {
                    baseline.Scope,
                    baseline.SampleCount,
                    Direction = isBull ? "偏多" : "偏空",
                    Horizons = horizonMetrics,
                    Caveat = baseline.SampleCount < 5 ? "样本不足，仅供参考" : (string?)null
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("GetSignalTrackRecord")
        .WithOpenApi();

        group.MapPost("/plans", async (TradingPlanCreateDto request, ITradingPlanService tradingPlanService, IStockMarketContextService marketContextService, ITradeExecutionInsightService tradeExecutionInsightService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            try
            {
                var result = await tradingPlanService.CreateAsync(request);
                var currentContext = await marketContextService.GetLatestAsync(result.Plan.Symbol);
                var insight = await tradeExecutionInsightService.GetPlanInsightAsync(result.Plan);
                return Results.Ok(MapTradingPlanDto(result.Plan, result.WatchlistEnsured, currentContext, insight));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("CreateTradingPlan")
        .WithOpenApi();

        group.MapPut("/plans/{id:long}", async (long id, TradingPlanUpdateDto request, ITradingPlanService tradingPlanService, IStockMarketContextService marketContextService, ITradeExecutionInsightService tradeExecutionInsightService) =>
        {
            try
            {
                var item = await tradingPlanService.UpdateAsync(id, request);
                if (item is null)
                {
                    return Results.NotFound();
                }

                var currentContext = await marketContextService.GetLatestAsync(item.Symbol);
                var insight = await tradeExecutionInsightService.GetPlanInsightAsync(item);
                return Results.Ok(MapTradingPlanDto(item, null, currentContext, insight));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("UpdateTradingPlan")
        .WithOpenApi();

        group.MapPost("/plans/{id:long}/cancel", async (long id, ITradingPlanService tradingPlanService, IStockMarketContextService marketContextService, ITradeExecutionInsightService tradeExecutionInsightService) =>
        {
            var item = await tradingPlanService.CancelAsync(id);
            if (item is null)
            {
                return Results.NotFound();
            }

            var currentContext = await marketContextService.GetLatestAsync(item.Symbol);
            var insight = await tradeExecutionInsightService.GetPlanInsightAsync(item);
            return Results.Ok(MapTradingPlanDto(item, null, currentContext, insight));
        })
        .WithName("CancelTradingPlan")
        .WithOpenApi();

        group.MapPost("/plans/{id:long}/resume", async (long id, ITradingPlanService tradingPlanService, IStockMarketContextService marketContextService, ITradeExecutionInsightService tradeExecutionInsightService) =>
        {
            try
            {
                var item = await tradingPlanService.ResumeAsync(id);
                if (item is null)
                {
                    return Results.NotFound();
                }

                var currentContext = await marketContextService.GetLatestAsync(item.Symbol);
                var insight = await tradeExecutionInsightService.GetPlanInsightAsync(item);
                return Results.Ok(MapTradingPlanDto(item, null, currentContext, insight));
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

        group.MapPost("/history", async (StockHistoryRecordRequestDto request, IStockHistoryService historyService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Symbol))
            {
                return Results.BadRequest(new { message = "symbol 不能为空" });
            }

            var item = await historyService.RecordAsync(request);
            return Results.Ok(item);
        })
        .WithName("RecordStockHistory")
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
                return Results.NoContent();
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

        // ── Translation endpoints ────────────────────────────────────────

        group.MapGet("/translations/json-keys", (IJsonKeyTranslationService translationService) =>
        {
            return Results.Ok(translationService.GetCachedTranslations());
        })
        .WithName("GetJsonKeyTranslations")
        .WithOpenApi();

        group.MapPost("/translations/json-keys", async (string[] keys, IJsonKeyTranslationService translationService, CancellationToken ct) =>
        {
            if (keys is null || keys.Length == 0)
                return Results.BadRequest(new { message = "keys 不能为空" });
            if (keys.Length > 200)
                return Results.BadRequest(new { message = "单次最多翻译 200 个 key" });

            var translations = await translationService.TranslateKeysAsync(keys, ct);
            return Results.Ok(translations);
        })
        .WithName("TranslateJsonKeys")
        .WithOpenApi();

        // ── StockPosition endpoints ──────────────────────────────────────

        group.MapGet("/position", async (string symbol, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return Results.BadRequest(new { message = "symbol 不能为空" });

            var pos = await db.StockPositions
                .FirstOrDefaultAsync(p => p.Symbol == symbol, ct);
            if (pos is null)
                return Results.Ok(new { symbol, quantityLots = 0, averageCostPrice = 0m, notes = (string?)null });
            return Results.Ok(new { pos.Symbol, pos.QuantityLots, pos.AverageCostPrice, pos.Notes });
        })
        .WithName("GetStockPosition")
        .WithOpenApi();

        group.MapPut("/position", async (StockPositionUpsertDto request, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Symbol))
                return Results.BadRequest(new { message = "symbol 不能为空" });

            var pos = await db.StockPositions
                .FirstOrDefaultAsync(p => p.Symbol == request.Symbol, ct);
            if (pos is null)
            {
                pos = new Data.Entities.StockPosition
                {
                    Symbol = request.Symbol,
                    QuantityLots = request.QuantityLots,
                    AverageCostPrice = request.AverageCostPrice,
                    Notes = request.Notes,
                    UpdatedAt = DateTime.UtcNow
                };
                db.StockPositions.Add(pos);
            }
            else
            {
                pos.QuantityLots = request.QuantityLots;
                pos.AverageCostPrice = request.AverageCostPrice;
                pos.Notes = request.Notes;
                pos.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { pos.Symbol, pos.QuantityLots, pos.AverageCostPrice, pos.Notes });
        })
        .WithName("UpsertStockPosition")
        .WithOpenApi();

        // ── Research Session endpoints ──────────────────────────────────────

        group.MapGet("/research/active-session", async (string symbol, IResearchSessionService researchService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return Results.BadRequest(new { message = "symbol 不能为空" });

            var dto = await researchService.GetActiveSessionAsync(symbol.Trim(), ct);
            return Results.Ok(dto);
        })
        .WithName("GetActiveResearchSession")
        .WithOpenApi();

        group.MapGet("/research/sessions/{sessionId:long}", async (long sessionId, IResearchSessionService researchService, CancellationToken ct) =>
        {
            var dto = await researchService.GetSessionDetailAsync(sessionId, ct);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        })
        .WithName("GetResearchSessionDetail")
        .WithOpenApi();

        group.MapGet("/research/sessions", async (string symbol, int? limit, IResearchSessionService researchService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return Results.BadRequest(new { message = "symbol 不能为空" });

            var list = await researchService.ListSessionsAsync(symbol.Trim(), limit ?? 20, ct);
            return Results.Ok(list);
        })
        .WithName("ListResearchSessions")
        .WithOpenApi();

        group.MapPost("/research/turns", async (ResearchTurnSubmitRequestDto request, IResearchSessionService researchService, IResearchRunner runner, IServiceScopeFactory scopeFactory, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Symbol))
                return Results.BadRequest(new { message = "symbol 不能为空" });

            if (string.IsNullOrWhiteSpace(request.UserPrompt))
                return Results.BadRequest(new { message = "userPrompt 不能为空" });

            if (request.UserPrompt.Length > 2000)
                return Results.BadRequest(new { message = "userPrompt 长度不能超过 2000 字符" });

            var response = await researchService.SubmitTurnAsync(request, ct);

            // Fire-and-forget: run the research pipeline in a background scope
            var turnId = response.TurnId;
            if (!_concurrentTurns.Wait(0))
                return Results.StatusCode(429);

            var cts = new CancellationTokenSource();
            _turnCancellationSources.TryAdd(turnId, cts);

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var scopedRunner = scope.ServiceProvider.GetRequiredService<IResearchRunner>();
                    try
                    {
                        await scopedRunner.RunTurnAsync(turnId, cts.Token);
                    }
                    catch (Exception ex)
                    {
                        var logger = scope.ServiceProvider.GetService<ILogger<StocksModule>>();
                        logger?.LogError(ex, "Research turn {TurnId} pipeline failed", turnId);
                        // Publish TurnFailed so SSE clients see a terminal event
                        try
                        {
                            var eventBus = scope.ServiceProvider.GetService<IRecommendEventBus>();
                            eventBus?.Publish(new RecommendEvent(
                                RecommendEventType.TurnFailed, 0, turnId, null, null, null, null,
                                $"研究流水线启动失败: {ex.Message}", null, DateTime.UtcNow));
                            eventBus?.MarkTurnTerminal(turnId);
                        }
                        catch { /* best-effort */ }
                    }
                }
                finally
                {
                    _turnCancellationSources.TryRemove(turnId, out var removed);
                    removed?.Dispose();
                    _concurrentTurns.Release();
                }
            });

            return Results.Ok(response);
        })
        .WithName("SubmitResearchTurn")
        .WithOpenApi();

        group.MapPost("/research/sessions/{sessionId:long}/cancel", async (long sessionId, IResearchSessionService researchService, CancellationToken ct) =>
        {
            // Try to cancel the in-process runner via CTS
            var session = await researchService.GetSessionDetailAsync(sessionId, ct);
            if (session is not null)
            {
                var activeTurn = session.Turns.FirstOrDefault(t => t.Status == "Running" || t.Status == "Queued");
                if (activeTurn is not null)
                {
                    if (_turnCancellationSources.TryRemove(activeTurn.Id, out var cts))
                    {
                        cts.Cancel();
                        cts.Dispose();
                    }
                }
            }

            // Also mark as cancelled in DB (handles orphaned turns from previous process)
            var cancelled = await researchService.CancelActiveTurnAsync(sessionId, ct);
            return Results.Ok(new { cancelled });
        })
        .WithName("CancelResearchSession")
        .WithOpenApi();

        // ── R5: Structured artifact endpoints ──────────────────────────────

        group.MapGet("/research/turns/{turnId:long}/artifacts", async (long turnId, IResearchArtifactService artifactService, CancellationToken ct) =>
        {
            var dto = await artifactService.GetTurnArtifactsAsync(turnId, ct);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        })
        .WithName("GetResearchTurnArtifacts")
        .WithOpenApi();

        group.MapGet("/research/sessions/{sessionId:long}/debates", async (long sessionId, IResearchArtifactService artifactService, CancellationToken ct) =>
        {
            var list = await artifactService.GetDebateHistoryAsync(sessionId, ct);
            return Results.Ok(list);
        })
        .WithName("GetResearchDebateHistory")
        .WithOpenApi();

        group.MapGet("/research/sessions/{sessionId:long}/proposals", async (long sessionId, IResearchArtifactService artifactService, CancellationToken ct) =>
        {
            var list = await artifactService.GetProposalHistoryAsync(sessionId, ct);
            return Results.Ok(list);
        })
        .WithName("GetResearchProposalHistory")
        .WithOpenApi();

        // ── R6: Report & Decision endpoints ──────────────────────────
        group.MapGet("/research/turns/{turnId:long}/report", async (long turnId, IResearchReportService reportService, CancellationToken ct) =>
        {
            var report = await reportService.GetTurnReportAsync(turnId, ct);
            return report is null ? Results.NotFound() : Results.Ok(report);
        })
        .WithName("GetResearchTurnReport")
        .WithOpenApi();

        group.MapGet("/research/turns/{turnId:long}/decision", async (long turnId, IResearchReportService reportService, CancellationToken ct) =>
        {
            var decision = await reportService.GetFinalDecisionAsync(turnId, ct);
            return decision is null ? Results.NotFound() : Results.Ok(decision);
        })
        .WithName("GetResearchFinalDecision")
        .WithOpenApi();

        // ── Recommendation Session endpoints ────────────────────────────────

        var recGroup = app.MapGroup("/api/recommend");

        recGroup.MapPost("/sessions", async (RecommendCreateSessionRequestDto request,
            IRecommendationSessionService sessionService, IRecommendationRunner runner,
            IServiceScopeFactory scopeFactory, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.UserPrompt))
                return Results.BadRequest(new { message = "userPrompt 不能为空" });

            if (request.UserPrompt.Length > 2000)
                return Results.BadRequest(new { message = "userPrompt 长度不能超过 2000 字符" });

            var session = await sessionService.CreateSessionAsync(request.UserPrompt.Trim(), ct);
            var turnId = session.ActiveTurnId!.Value;
            var sessionId = session.Id;

            if (!_concurrentTurns.Wait(0))
                return Results.StatusCode(429);

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var scopedRunner = scope.ServiceProvider.GetRequiredService<IRecommendationRunner>();
                    try { await scopedRunner.RunTurnAsync(turnId, CancellationToken.None); }
                    catch (Exception ex)
                    {
                        var logger = scope.ServiceProvider.GetService<ILogger<StocksModule>>();
                        logger?.LogError(ex, "Recommend turn {TurnId} pipeline failed", turnId);
                        try
                        {
                            var eventBus = scope.ServiceProvider.GetService<IRecommendEventBus>();
                            eventBus?.Publish(new RecommendEvent(
                                RecommendEventType.TurnFailed, sessionId, turnId, null, null, null, null,
                                $"流水线启动失败: {ex.Message}", null, DateTime.UtcNow));
                            eventBus?.MarkTurnTerminal(turnId);
                        }
                        catch { /* best-effort */ }
                    }
                }
                finally
                {
                    _concurrentTurns.Release();
                }
            });

            return Results.Ok(new { session.Id, session.SessionKey, TurnId = turnId });
        })
        .WithName("CreateRecommendSession")
        .WithOpenApi();

        recGroup.MapGet("/sessions", async (int? page, int? pageSize,
            IRecommendationSessionService sessionService, CancellationToken ct) =>
        {
            var list = await sessionService.ListSessionsAsync(page ?? 1, pageSize ?? 20, ct);
            return Results.Ok(list);
        })
        .WithName("ListRecommendSessions")
        .WithOpenApi();

        recGroup.MapGet("/sessions/{id:long}", async (long id,
            IRecommendationSessionService sessionService, CancellationToken ct) =>
        {
            var detail = await sessionService.GetSessionDetailAsync(id, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        })
        .WithName("GetRecommendSessionDetail")
        .WithOpenApi();

        recGroup.MapPost("/sessions/{id:long}/follow-up", async (long id,
            RecommendFollowUpRequestDto request, IRecommendationSessionService sessionService,
            IRecommendationRunner runner, IServiceScopeFactory scopeFactory, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.UserPrompt))
                return Results.BadRequest(new { message = "userPrompt 不能为空" });

            if (request.UserPrompt.Length > 2000)
                return Results.BadRequest(new { message = "userPrompt 长度不能超过 2000 字符" });

            var (turn, plan) = await sessionService.SubmitFollowUpAsync(id, request.UserPrompt.Trim(), ct);
            var turnId = turn.Id;

            switch (plan.Strategy)
            {
                case FollowUpStrategy.DirectAnswer:
                {
                    // No agent execution; generate answer from existing debate records
                    var answer = await runner.GenerateDirectAnswerAsync(id, request.UserPrompt.Trim(), ct);
                    // Mark turn as completed immediately
                    turn.Status = RecommendTurnStatus.Completed;
                    turn.StartedAt = DateTime.UtcNow;
                    turn.CompletedAt = DateTime.UtcNow;
                    var session = await db.RecommendationSessions.FirstOrDefaultAsync(s => s.Id == id, ct);
                    if (session is not null)
                    {
                        session.Status = RecommendSessionStatus.Completed;
                        session.UpdatedAt = DateTime.UtcNow;
                    }
                    // Persist user's follow-up question as a FeedItem
                    db.RecommendationFeedItems.Add(new RecommendationFeedItem
                    {
                        TurnId = turnId,
                        ItemType = RecommendFeedItemType.UserFollowUp,
                        Content = request.UserPrompt.Trim(),
                        CreatedAt = DateTime.UtcNow
                    });

                    // Persist DirectAnswer as a FeedItem so it appears in the debate feed
                    db.RecommendationFeedItems.Add(new RecommendationFeedItem
                    {
                        TurnId = turnId,
                        RoleId = "direct_answer",
                        ItemType = RecommendFeedItemType.RoleMessage,
                        Content = answer ?? "无法生成回答。",
                        MetadataJson = System.Text.Json.JsonSerializer.Serialize(new { eventType = "DirectAnswer", directAnswer = true }),
                        CreatedAt = DateTime.UtcNow
                    });

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(new { TurnId = turnId, turn.TurnIndex, Strategy = plan.Strategy.ToString(),
                        plan.Reasoning, DirectAnswer = answer });
                }
                case FollowUpStrategy.WorkbenchHandoff:
                {
                    // Create a ResearchSession for workbench and return navigation info
                    var targetStock = plan.Overrides?.TargetStocks?.FirstOrDefault();
                    turn.Status = RecommendTurnStatus.Completed;
                    turn.StartedAt = DateTime.UtcNow;
                    turn.CompletedAt = DateTime.UtcNow;
                    var session = await db.RecommendationSessions.FirstOrDefaultAsync(s => s.Id == id, ct);
                    if (session is not null)
                    {
                        session.Status = RecommendSessionStatus.Completed;
                        session.UpdatedAt = DateTime.UtcNow;
                    }
                    await db.SaveChangesAsync(ct);
                    return Results.Ok(new { TurnId = turnId, turn.TurnIndex, Strategy = plan.Strategy.ToString(),
                        plan.Reasoning, HandoffSymbol = targetStock, NavigateTo = "workbench" });
                }
                case FollowUpStrategy.PartialRerun:
                {
                    var fromStage = plan.FromStageIndex ?? 0;
                    if (!_concurrentTurns.Wait(0))
                        return Results.StatusCode(429);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var scope = scopeFactory.CreateScope();
                            var scopedRunner = scope.ServiceProvider.GetRequiredService<IRecommendationRunner>();
                            try { await scopedRunner.RunPartialTurnAsync(turnId, fromStage, CancellationToken.None); }
                            catch (Exception ex)
                            {
                                var logger = scope.ServiceProvider.GetService<ILogger<StocksModule>>();
                                logger?.LogError(ex, "Recommend partial rerun turn {TurnId} from stage {Stage} failed", turnId, fromStage);
                                try
                                {
                                    var eventBus = scope.ServiceProvider.GetService<IRecommendEventBus>();
                                    eventBus?.Publish(new RecommendEvent(
                                        RecommendEventType.TurnFailed, id, turnId, null, null, null, null,
                                        $"部分重跑流水线启动失败: {ex.Message}", null, DateTime.UtcNow));
                                    eventBus?.MarkTurnTerminal(turnId);
                                }
                                catch { /* best-effort */ }
                            }
                        }
                        finally
                        {
                            _concurrentTurns.Release();
                        }
                    });
                    return Results.Ok(new { TurnId = turnId, turn.TurnIndex, Strategy = plan.Strategy.ToString(),
                        plan.Reasoning, FromStageIndex = fromStage });
                }
                default: // FullRerun
                {
                    if (!_concurrentTurns.Wait(0))
                        return Results.StatusCode(429);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var scope = scopeFactory.CreateScope();
                            var scopedRunner = scope.ServiceProvider.GetRequiredService<IRecommendationRunner>();
                            try { await scopedRunner.RunTurnAsync(turnId, CancellationToken.None); }
                            catch (Exception ex)
                            {
                                var logger = scope.ServiceProvider.GetService<ILogger<StocksModule>>();
                                logger?.LogError(ex, "Recommend follow-up turn {TurnId} pipeline failed", turnId);
                                try
                                {
                                    var eventBus = scope.ServiceProvider.GetService<IRecommendEventBus>();
                                    eventBus?.Publish(new RecommendEvent(
                                        RecommendEventType.TurnFailed, id, turnId, null, null, null, null,
                                        $"流水线启动失败: {ex.Message}", null, DateTime.UtcNow));
                                    eventBus?.MarkTurnTerminal(turnId);
                                }
                                catch { /* best-effort */ }
                            }
                        }
                        finally
                        {
                            _concurrentTurns.Release();
                        }
                    });
                    return Results.Ok(new { TurnId = turnId, turn.TurnIndex, Strategy = plan.Strategy.ToString(),
                        plan.Reasoning });
                }
            }
        })
        .WithName("SubmitRecommendFollowUp")
        .WithOpenApi();

        recGroup.MapPost("/sessions/{id:long}/retry-from-stage", async (long id,
            RecommendRetryFromStageRequestDto request,
            IRecommendationSessionService sessionService, IServiceScopeFactory scopeFactory,
            AppDbContext db, CancellationToken ct) =>
        {
            var session = await db.RecommendationSessions
                .Include(s => s.Turns)
                .FirstOrDefaultAsync(s => s.Id == id, ct);
            if (session is null)
                return Results.NotFound(new { message = "Session not found" });

            var latestTurn = session.Turns
                .OrderByDescending(t => t.TurnIndex)
                .FirstOrDefault();
            if (latestTurn is null)
                return Results.BadRequest(new { message = "No turns found in session" });

            if (latestTurn.Status == RecommendTurnStatus.Running)
                return Results.Conflict(new { message = "Turn is already running" });

            var fromStage = request.FromStageIndex;
            if (fromStage < 0 || fromStage > 4)
                return Results.BadRequest(new { message = "fromStageIndex must be between 0 and 4" });

            var turnId = latestTurn.Id;

            if (!_concurrentTurns.Wait(0))
                return Results.StatusCode(429);

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var scopedRunner = scope.ServiceProvider.GetRequiredService<IRecommendationRunner>();
                    try
                    {
                        await scopedRunner.RunPartialTurnAsync(turnId, fromStage, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        var logger = scope.ServiceProvider.GetService<ILogger<StocksModule>>();
                        logger?.LogError(ex, "Retry-from-stage turn {TurnId} from stage {Stage} failed", turnId, fromStage);
                        try
                        {
                            var eventBus = scope.ServiceProvider.GetService<IRecommendEventBus>();
                            eventBus?.Publish(new RecommendEvent(
                                RecommendEventType.TurnFailed, id, turnId, null, null, null, null,
                                $"重试流水线启动失败: {ex.Message}", null, DateTime.UtcNow));
                            eventBus?.MarkTurnTerminal(turnId);
                        }
                        catch { /* best-effort */ }
                    }
                }
                finally
                {
                    _concurrentTurns.Release();
                }
            });

            return Results.Ok(new { TurnId = turnId, FromStageIndex = fromStage });
        })
        .WithName("RetryFromStage")
        .WithOpenApi();

        recGroup.MapGet("/sessions/{id:long}/events", async (long id,
            IRecommendationSessionService sessionService, IRecommendEventBus eventBus,
            HttpContext httpContext, CancellationToken ct) =>
        {
            var session = await sessionService.GetSessionDetailAsync(id, ct);
            if (session is null)
            {
                httpContext.Response.StatusCode = 404;
                return;
            }

            var turnId = session.ActiveTurnId ?? 0;
            var cursorSequence = 0L;
            var lastEventId = httpContext.Request.Headers["Last-Event-ID"].ToString();
            var announceTurnSwitch = false;
            if (TryParseSseId(lastEventId, out var resumeTurnId, out var resumeSequence))
            {
                if (resumeTurnId == turnId)
                {
                    cursorSequence = resumeSequence;
                }
                else if (resumeTurnId != 0)
                {
                    announceTurnSwitch = true;
                }
            }

            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";
            httpContext.Response.Headers["X-Accel-Buffering"] = "no";
            httpContext.Response.ContentType = "text/event-stream";
            var emptyPollCount = 0;
            var nextHeartbeatAt = DateTime.UtcNow.AddSeconds(15);

            while (!ct.IsCancellationRequested)
            {
                if (announceTurnSwitch)
                {
                    announceTurnSwitch = false;
                    await WriteJsonEventAsync(BuildSseId(turnId, cursorSequence), new
                    {
                        eventType = "TurnSwitched",
                        turnId
                    }, ct);
                    nextHeartbeatAt = DateTime.UtcNow.AddSeconds(15);
                }

                var events = eventBus.SnapshotSince(turnId, cursorSequence)
                    .OrderBy(item => item.Sequence)
                    .ToArray();

                if (events.Length == 0)
                {
                    emptyPollCount++;

                    if (emptyPollCount % 5 == 0)
                    {
                        var refreshed = await sessionService.GetSessionDetailAsync(id, ct);
                        if (refreshed is null)
                        {
                            return;
                        }

                        var newTurnId = refreshed?.ActiveTurnId ?? 0;
                        if (newTurnId != 0 && newTurnId != turnId)
                        {
                            turnId = newTurnId;
                            cursorSequence = 0;
                            await WriteJsonEventAsync(BuildSseId(turnId, cursorSequence), new
                            {
                                eventType = "TurnSwitched",
                                turnId
                            }, ct);
                            nextHeartbeatAt = DateTime.UtcNow.AddSeconds(15);
                            continue;
                        }

                        var currentTurn = refreshed?.Turns?.FirstOrDefault(t => t.Id == turnId);
                        if (currentTurn is not null &&
                            currentTurn.Status is "Completed" or "Failed" or "Cancelled")
                        {
                            var hasTerminalEvent = eventBus.Snapshot(turnId)
                                .Any(item => item.EventType is RecommendEventType.TurnCompleted or RecommendEventType.TurnFailed);

                            if (!hasTerminalEvent)
                            {
                                var syntheticSequence = cursorSequence + 1;
                                await WriteJsonEventAsync(BuildSseId(turnId, syntheticSequence), new
                                {
                                    eventType = currentTurn.Status == "Completed" ? "TurnCompleted" : "TurnFailed",
                                    turnId,
                                    summary = $"Turn {currentTurn.Status}"
                                }, ct);
                                cursorSequence = syntheticSequence;
                            }

                            await WriteDoneAsync(ct);
                            return;
                        }
                    }
                }
                else
                {
                    emptyPollCount = 0;
                    nextHeartbeatAt = DateTime.UtcNow.AddSeconds(15);
                }

                foreach (var envelope in events)
                {
                    cursorSequence = envelope.Sequence;
                    var evt = envelope.Event;
                    await WriteJsonEventAsync(BuildSseId(evt.TurnId, envelope.Sequence), new
                    {
                        eventType = evt.EventType.ToString(),
                        sessionId = evt.SessionId,
                        turnId = evt.TurnId,
                        stageId = evt.StageId,
                        stageType = evt.StageType,
                        roleId = evt.RoleId,
                        traceId = evt.TraceId,
                        summary = evt.Summary,
                        detailJson = evt.DetailJson,
                        timestamp = evt.Timestamp
                    }, ct);

                    if (evt.EventType is RecommendEventType.TurnCompleted or RecommendEventType.TurnFailed)
                    {
                        await WriteDoneAsync(ct);
                        return;
                    }
                }

                if (DateTime.UtcNow >= nextHeartbeatAt)
                {
                    await httpContext.Response.WriteAsync($": keepalive {BuildSseId(turnId, cursorSequence)}\n\n", ct);
                    await httpContext.Response.Body.FlushAsync(ct);
                    nextHeartbeatAt = DateTime.UtcNow.AddSeconds(15);
                }

                await Task.Delay(500, ct);
            }

            async Task WriteJsonEventAsync(string eventId, object payload, CancellationToken cancellationToken)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                await httpContext.Response.WriteAsync($"id: {eventId}\n", cancellationToken);
                await httpContext.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(cancellationToken);
            }

            async Task WriteDoneAsync(CancellationToken cancellationToken)
            {
                await httpContext.Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(cancellationToken);
            }

            static string BuildSseId(long currentTurnId, long sequence) => $"{currentTurnId}:{sequence}";

            static bool TryParseSseId(string? value, out long parsedTurnId, out long parsedSequence)
            {
                parsedTurnId = 0;
                parsedSequence = 0;
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                var parts = value.Split(':', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                {
                    return false;
                }

                return long.TryParse(parts[0], out parsedTurnId)
                    && long.TryParse(parts[1], out parsedSequence);
            }
        })
        .WithName("StreamRecommendEvents")
        .WithOpenApi();

        // ── Trade Execution endpoints ────────────────────────────────

        var tradeGroup = app.MapGroup("/api/trades");

        tradeGroup.MapPost("/reset-all", async (ITradeAccountingService svc) =>
        {
            var (trades, positions, reviews) = await svc.ResetAllTradesAsync();
            return Results.Ok(new { success = true, deletedTradeCount = trades, deletedPositionCount = positions, deletedReviewCount = reviews });
        })
        .WithName("ResetAllTrades")
        .WithOpenApi();

        tradeGroup.MapPost("/", async (TradeExecutionCreateDto dto, ITradeAccountingService svc) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Symbol))
                return Results.BadRequest(new { message = "symbol 不能为空" });
            if (dto.Quantity <= 0)
                return Results.BadRequest(new { message = "quantity 必须大于0" });
            if (dto.ExecutedPrice <= 0)
                return Results.BadRequest(new { message = "executedPrice 必须大于0" });

            try
            {
                var trade = await svc.RecordTradeAsync(dto);
                var trades = await svc.GetTradesAsync(null, null, null, null);
                var created = trades.FirstOrDefault(t => t.Id == trade.Id);
                return Results.Ok(created ?? (object)new { trade.Id });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("RecordTradeExecution")
        .WithOpenApi();

        tradeGroup.MapPut("/{id:long}", async (long id, TradeExecutionUpdateDto dto, ITradeAccountingService svc) =>
        {
            try
            {
                var trade = await svc.UpdateTradeAsync(id, dto);
                var trades = await svc.GetTradesAsync(null, null, null, null);
                var updated = trades.FirstOrDefault(t => t.Id == trade.Id);
                return Results.Ok(updated ?? (object)new { trade.Id });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        })
        .WithName("UpdateTradeExecution")
        .WithOpenApi();

        tradeGroup.MapDelete("/{id:long}", async (long id, ITradeAccountingService svc) =>
        {
            try
            {
                await svc.DeleteTradeAsync(id);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        })
        .WithName("DeleteTradeExecution")
        .WithOpenApi();

        tradeGroup.MapGet("/", async ([AsParameters] TradeQueryParams query, ITradeAccountingService svc) =>
        {
            var items = await svc.GetTradesAsync(query.Symbol, query.From, query.To, query.Type);
            return Results.Ok(items);
        })
        .WithName("GetTradeExecutions")
        .WithOpenApi();

        tradeGroup.MapGet("/summary", async (string period, DateTime? from, DateTime? to, ITradeAccountingService svc) =>
        {
            var summary = await svc.GetTradeSummaryAsync(period, from, to);
            return Results.Ok(summary);
        })
        .WithName("GetTradeSummary")
        .WithOpenApi();

        tradeGroup.MapGet("/win-rate", async (DateTime? from, DateTime? to, string? symbol, ITradeAccountingService svc) =>
        {
            var result = await svc.GetWinRateAsync(from, to, symbol);
            return Results.Ok(result);
        })
        .WithName("GetTradeWinRate")
        .WithOpenApi();

        tradeGroup.MapGet("/plan-deviation", async (long planId, ITradeComplianceService svc) =>
        {
            var result = await svc.GetPlanDeviationAsync(planId);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetTradePlanDeviation")
        .WithOpenApi();

        tradeGroup.MapGet("/compliance-stats", async (DateTime? from, DateTime? to, ITradeComplianceService svc) =>
        {
            var result = await svc.GetComplianceStatsAsync(from, to);
            return Results.Ok(result);
        })
        .WithName("GetTradeComplianceStats")
        .WithOpenApi();

        tradeGroup.MapGet("/behavior-stats", async (ITradingBehaviorService svc) =>
        {
            var result = await svc.GetBehaviorStatsAsync();
            return Results.Ok(result);
        })
        .WithName("GetTradeBehaviorStats")
        .WithOpenApi();

        // ── Trade Review endpoints ───────────────────────────────────

        tradeGroup.MapPost("/reviews/generate", async (TradeReviewGenerateDto dto, ITradeReviewService svc, ILogger<StocksModule> logger, CancellationToken ct) =>
        {
            try
            {
                var result = await svc.GenerateReviewAsync(dto, ct);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to generate trade review");
                return Results.Problem("生成复盘失败，请稍后重试", statusCode: 500);
            }
        })
        .WithName("GenerateTradeReview")
        .WithOpenApi();

        tradeGroup.MapGet("/reviews", async (string? type, DateTime? from, DateTime? to, ITradeReviewService svc, CancellationToken ct) =>
        {
            var result = await svc.GetReviewsAsync(type, from, to, ct);
            return Results.Ok(result);
        })
        .WithName("GetTradeReviews")
        .WithOpenApi();

        tradeGroup.MapGet("/reviews/{id:long}", async (long id, ITradeReviewService svc, CancellationToken ct) =>
        {
            var result = await svc.GetReviewByIdAsync(id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetTradeReviewById")
        .WithOpenApi();

        // ── Portfolio endpoints ──────────────────────────────────────

        var portfolioGroup = app.MapGroup("/api/portfolio");

        portfolioGroup.MapGet("/settings", async (IPortfolioSnapshotService svc) =>
        {
            var result = await svc.GetSettingsAsync();
            return Results.Ok(result);
        })
        .WithName("GetPortfolioSettings")
        .WithOpenApi();

        portfolioGroup.MapPut("/settings", async (PortfolioSettingsUpdateDto dto, IPortfolioSnapshotService svc) =>
        {
            if (dto.TotalCapital < 0)
                return Results.BadRequest(new { message = "totalCapital 不能为负数" });
            var result = await svc.UpdateSettingsAsync(dto);
            return Results.Ok(result);
        })
        .WithName("UpdatePortfolioSettings")
        .WithOpenApi();

        portfolioGroup.MapGet("/positions", async (IPortfolioSnapshotService svc) =>
        {
            var result = await svc.GetPositionsAsync();
            return Results.Ok(result);
        })
        .WithName("GetPortfolioPositions")
        .WithOpenApi();

        portfolioGroup.MapGet("/positions/{symbol}", async (string symbol, IPortfolioSnapshotService svc) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return Results.BadRequest(new { message = "symbol 不能为空" });
            var result = await svc.GetPositionAsync(symbol.Trim());
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetPortfolioPosition")
        .WithOpenApi();

        portfolioGroup.MapGet("/snapshot", async (IPortfolioSnapshotService svc) =>
        {
            var result = await svc.GetSnapshotAsync();
            return Results.Ok(result);
        })
        .WithName("GetPortfolioSnapshot")
        .WithOpenApi();

        portfolioGroup.MapGet("/exposure", async (IPortfolioSnapshotService svc) =>
        {
            var result = await svc.GetExposureAsync();
            return Results.Ok(result);
        })
        .WithName("GetPortfolioExposure")
        .WithOpenApi();

        portfolioGroup.MapGet("/context", async (IPortfolioSnapshotService svc) =>
        {
            var result = await svc.GetPortfolioContextAsync();
            return Results.Ok(result);
        })
        .WithName("GetPortfolioContext")
        .WithOpenApi();
    }

    private static StockCopilotMcpWindowOptions? CreateMcpWindowOptions(int? evidenceSkip, int? evidenceTake, int? factSkip = null, int? factTake = null)
    {
        if (!evidenceSkip.HasValue && !evidenceTake.HasValue && !factSkip.HasValue && !factTake.HasValue)
        {
            return null;
        }

        return new StockCopilotMcpWindowOptions(
            evidenceSkip ?? 0,
            evidenceTake,
            factSkip ?? 0,
            factTake);
    }

    internal static string ResolveFinancialWorkerBaseUrl(IConfiguration configuration)
    {
        var configured = configuration["FinancialWorker:BaseUrl"]?.Trim();
        if (Uri.TryCreate(configured, UriKind.Absolute, out var configuredUri))
        {
            return configuredUri.ToString().TrimEnd('/');
        }

        return "http://localhost:5120";
    }

    internal static string NormalizeFinancialWorkerSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return string.Empty;
        }

        var normalized = StockSymbolNormalizer.Normalize(symbol);
        if (normalized.Length == 8
            && (normalized.StartsWith("sh", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("sz", StringComparison.OrdinalIgnoreCase)))
        {
            return normalized[2..];
        }

        return symbol.Trim();
    }

    internal static async Task<IResult> GetLatestMarketContextResultAsync(
        string? symbol,
        IStockMarketContextService marketContextService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return Results.BadRequest(new { message = "symbol 不能为空" });
        }

        var target = symbol.Trim();
        var marketContext = await marketContextService.GetLatestAsync(target, cancellationToken);
        return marketContext is null ? Results.NotFound() : Results.Ok(marketContext);
    }

    internal static async Task<IResult> GetTradingPlansResultAsync(
        string? symbol,
        int? take,
        ITradingPlanService tradingPlanService,
        IStockMarketContextService marketContextService,
        ITradeExecutionInsightService tradeExecutionInsightService,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var list = await tradingPlanService.GetListAsync(symbol, take ?? 20, cancellationToken);

        IReadOnlyDictionary<long, TradingPlanRuntimeInsightDto> insights;
        try
        {
            insights = await tradeExecutionInsightService.GetPlanInsightsAsync(list, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "加载交易计划执行洞察失败，将降级返回计划主体列表。symbol={Symbol}, count={Count}", symbol, list.Count);
            insights = new Dictionary<long, TradingPlanRuntimeInsightDto>();
        }

        var currentContexts = await LoadLatestMarketContextsSafelyAsync(list, marketContextService, logger, cancellationToken);
        var payload = list
            .Select((item, index) => MapTradingPlanDto(item, null, currentContexts[index], insights.GetValueOrDefault(item.Id)))
            .ToArray();

        return Results.Ok(payload);
    }

    private static async Task<StockMarketContextDto?[]> LoadLatestMarketContextsSafelyAsync(
        IReadOnlyList<Data.Entities.TradingPlan> plans,
        IStockMarketContextService marketContextService,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var contexts = new StockMarketContextDto?[plans.Count];
        var cache = new Dictionary<string, StockMarketContextDto?>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < plans.Count; index++)
        {
            var symbol = plans[index].Symbol;
            if (string.IsNullOrWhiteSpace(symbol))
            {
                continue;
            }

            if (!cache.TryGetValue(symbol, out var currentContext))
            {
                try
                {
                    currentContext = await marketContextService.GetLatestAsync(symbol, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "加载交易计划当前市场上下文失败，将跳过该计划的当前上下文。symbol={Symbol}", symbol);
                    currentContext = null;
                }

                cache[symbol] = currentContext;
            }

            contexts[index] = currentContext;
        }

        return contexts;
    }

    private static async Task<IResult> ProxyFinancialWorkerAsync(
        HttpMethod method,
        string relativePath,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken,
        object? payload = null,
        bool includeCollectErrorFields = false)
    {
        var baseUrl = ResolveFinancialWorkerBaseUrl(configuration);
        var requestTimeout = ResolveFinancialWorkerProxyTimeout(relativePath);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(requestTimeout);

        try
        {
            using var httpClient = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(method, BuildFinancialWorkerUri(baseUrl, relativePath));
            if (payload is not null)
            {
                request.Content = JsonContent.Create(payload);
            }

            using var response = await httpClient.SendAsync(request, timeoutCts.Token);
            var responseText = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(responseText))
                {
                    return Results.Json(new { success = true, baseUrl });
                }

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
                var bodyToReturn = includeCollectErrorFields
                    ? AugmentCollectResponseWithFriendlyAliases(responseText)
                    : responseText;
                return Results.Content(bodyToReturn, contentType);
            }

            return Results.Json(
                BuildFinancialWorkerErrorPayload(
                    baseUrl,
                    responseText,
                    response.StatusCode.ToString(),
                    includeCollectErrorFields,
                    $"财务 Worker 请求失败 ({(int)response.StatusCode})"),
                statusCode: (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.Json(
                BuildFinancialWorkerErrorPayload(
                    baseUrl,
                    null,
                    "timeout",
                    includeCollectErrorFields,
                    "财务 Worker 请求超时"),
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException ex)
        {
            return Results.Json(
                BuildFinancialWorkerErrorPayload(
                    baseUrl,
                    ex.Message,
                    "unreachable",
                    includeCollectErrorFields,
                    "财务 Worker 不可用"),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception ex)
        {
            return Results.Json(
                BuildFinancialWorkerErrorPayload(
                    baseUrl,
                    ex.Message,
                    "error",
                    includeCollectErrorFields,
                    "财务 Worker 代理失败"),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    internal static TimeSpan ResolveFinancialWorkerProxyTimeout(string relativePath)
    {
        var normalizedRelativePath = relativePath.TrimStart('/');
        return normalizedRelativePath.StartsWith("api/collect/", StringComparison.OrdinalIgnoreCase)
            ? FinancialWorkerCollectProxyTimeout
            : FinancialWorkerProxyTimeout;
    }

    internal static Uri BuildFinancialWorkerUri(string baseUrl, string relativePath)
    {
        var baseUri = new Uri($"{baseUrl.TrimEnd('/')}/", UriKind.Absolute);
        var normalizedRelativePath = relativePath.TrimStart('/');
        var basePath = baseUri.AbsolutePath.Trim('/');

        if (string.Equals(basePath, "api", StringComparison.OrdinalIgnoreCase)
            && normalizedRelativePath.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedRelativePath = normalizedRelativePath[4..];
        }

        return new Uri(baseUri, normalizedRelativePath);
    }

    private static Dictionary<string, object?> BuildFinancialWorkerErrorPayload(
        string baseUrl,
        string? responseText,
        string fallbackStatus,
        bool includeCollectErrorFields,
        string fallbackMessage)
    {
        var error = ExtractJsonString(responseText, "error")
            ?? ExtractJsonString(responseText, "message")
            ?? ExtractJsonString(responseText, "errorMessage")
            ?? fallbackMessage;
        var detail = ExtractJsonString(responseText, "detail");
        var errorMessage = ExtractJsonString(responseText, "errorMessage")
            ?? ExtractJsonString(responseText, "ErrorMessage")
            ?? error;
        var status = ExtractJsonString(responseText, "status") ?? fallbackStatus;

        if (string.IsNullOrWhiteSpace(detail) && !string.IsNullOrWhiteSpace(responseText) && !LooksLikeJson(responseText))
        {
            detail = responseText.Trim();
        }

        var payload = new Dictionary<string, object?>
        {
            ["success"] = false,
            ["reachable"] = false,
            ["status"] = status,
            ["baseUrl"] = baseUrl,
            ["error"] = error,
            ["message"] = error,
        };

        if (!string.IsNullOrWhiteSpace(detail))
        {
            payload["detail"] = detail;
        }

        if (includeCollectErrorFields)
        {
            payload["errorMessage"] = errorMessage;
        }

        return payload;
    }

    private static string? ExtractJsonString(string? json, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var propertyName in propertyNames)
            {
                if (!document.RootElement.TryGetProperty(propertyName, out var propertyValue))
                {
                    continue;
                }

                return propertyValue.ValueKind switch
                {
                    JsonValueKind.String => propertyValue.GetString(),
                    JsonValueKind.Number => propertyValue.GetRawText(),
                    JsonValueKind.True => bool.TrueString,
                    JsonValueKind.False => bool.FalseString,
                    _ => propertyValue.GetRawText(),
                };
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static bool LooksLikeJson(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    /// <summary>
    /// 在 /api/stocks/financial/collect 透传响应上叠加 5 个 camelCase 友好别名字段：
    /// reportPeriod / reportTitle / sourceChannel / fallbackReason / pdfSummary。
    /// 不删除/重命名任何已有字段；遇到非对象 JSON 或解析失败时原样返回。
    /// </summary>
    internal static string AugmentCollectResponseWithFriendlyAliases(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return json;
        }

        if (node is not JsonObject obj)
        {
            return json;
        }

        if (!obj.ContainsKey("reportPeriod"))
        {
            obj["reportPeriod"] = FirstStringOfArray(obj["reportPeriods"]);
        }

        if (!obj.ContainsKey("reportTitle"))
        {
            obj["reportTitle"] = FirstStringOfArray(obj["reportTitles"]);
        }

        if (!obj.ContainsKey("sourceChannel"))
        {
            obj["sourceChannel"] = CloneStringNode(obj["mainSourceChannel"]);
        }

        if (!obj.ContainsKey("fallbackReason"))
        {
            obj["fallbackReason"] = CloneStringNode(obj["degradeReason"]);
        }

        if (!obj.ContainsKey("pdfSummary"))
        {
            obj["pdfSummary"] = CloneStringNode(obj["pdfSummarySupplement"]);
        }

        return obj.ToJsonString();

        static JsonNode? FirstStringOfArray(JsonNode? source)
        {
            if (source is JsonArray arr && arr.Count > 0 && arr[0] is JsonNode first)
            {
                if (first is JsonValue value && value.TryGetValue<string>(out var s))
                {
                    return JsonValue.Create(s);
                }
            }
            return null;
        }

        static JsonNode? CloneStringNode(JsonNode? source)
        {
            if (source is JsonValue value && value.TryGetValue<string>(out var s))
            {
                return JsonValue.Create(s);
            }
            return null;
        }
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

    private static StockAgentHistoryItemDto MapStockAgentHistoryItemDto(Data.Entities.StockAgentAnalysisHistory item)
    {
        var validation = ValidateStockAgentHistory(item.ResultJson);
        return new StockAgentHistoryItemDto(
            item.Id,
            item.Symbol,
            item.Name,
            item.Summary,
            item.CreatedAt,
            validation.IsCommanderComplete,
            validation.BlockedReason);
    }

    private static StockAgentHistoryValidationResult ValidateStockAgentHistory(string resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            return new StockAgentHistoryValidationResult(
                false,
                false,
                false,
                false,
                new[] { "commander" },
                Array.Empty<string>(),
                "历史结果为空。");
        }

        try
        {
            using var document = JsonDocument.Parse(resultJson);
            return StockAgentHistoryValidation.Validate(document.RootElement);
        }
        catch (JsonException)
        {
            return new StockAgentHistoryValidationResult(
                false,
                false,
                false,
                false,
                new[] { "commander" },
                Array.Empty<string>(),
                "历史结果 JSON 无法解析。");
        }
    }

    private static TradingPlanItemDto MapTradingPlanDto(Data.Entities.TradingPlan item, bool? watchlistEnsured = null, StockMarketContextDto? currentMarketContext = null, TradingPlanRuntimeInsightDto? insight = null)
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
            currentMarketContext,
            insight?.ExecutionSummary,
            insight?.CurrentScenarioStatus,
            insight?.CurrentPositionSnapshot,
            item.ActiveScenario,
            item.PlanStartDate,
            item.PlanEndDate);
    }

    internal static IResult? ValidateTradingPlanDraftRequest(TradingPlanDraftRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Symbol))
        {
            return Results.BadRequest(new { message = "symbol 不能为空" });
        }

        if (request.AnalysisHistoryId <= 0)
        {
            return Results.BadRequest(new { message = "analysisHistoryId 无效" });
        }

        return null;
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
        return status;
    }
}
