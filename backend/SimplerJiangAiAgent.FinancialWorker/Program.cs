using SimplerJiangAiAgent.FinancialWorker;
using SimplerJiangAiAgent.FinancialWorker.Data;
using SimplerJiangAiAgent.FinancialWorker.Models;
using SimplerJiangAiAgent.FinancialWorker.Services;
using SimplerJiangAiAgent.FinancialWorker.Services.Pdf;

var builder = WebApplication.CreateBuilder(args);

// Kestrel 监听 5120 端口（不与主 API 5119 冲突）
builder.WebHost.UseUrls("http://localhost:5120");

var dataRoot = FinancialWorkerRuntimePaths.ResolveDataRoot();
var appDataPath = FinancialWorkerRuntimePaths.ResolveAppDataPath();
var dbPath = FinancialWorkerRuntimePaths.ResolveFinancialDatabasePath();
Directory.CreateDirectory(appDataPath);

builder.Services.AddSingleton(new FinancialDbContext($"Filename={dbPath};Connection=shared"));

builder.Services.AddHttpClient<IEastmoneyFinanceClient, EastmoneyFinanceClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Add("User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    client.DefaultRequestHeaders.Add("Accept",
        "application/json, text/javascript, */*");
    client.DefaultRequestHeaders.Add("Referer",
        "https://emweb.securities.eastmoney.com/");
});

builder.Services.AddHttpClient<IEastmoneyDatacenterClient, EastmoneyDatacenterClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Add("User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
});

builder.Services.AddHttpClient<IThsFinanceClient, ThsFinanceClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Add("User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
});

builder.Services.AddHttpClient<CninfoClient>(client =>
{
    client.DefaultRequestHeaders.Add("User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    client.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddSingleton<FinancialDataOrchestrator>();

builder.Services.AddSingleton<IPdfTextExtractor, DocnetExtractor>();
builder.Services.AddSingleton<IPdfTextExtractor, PdfPigExtractor>();
builder.Services.AddSingleton<IPdfTextExtractor, IText7Extractor>();
builder.Services.AddSingleton<PdfVotingEngine>();
builder.Services.AddSingleton<FinancialTableParser>();
builder.Services.AddSingleton<PdfProcessingPipeline>();
builder.Services.AddSingleton<IPdfProcessingPipeline>(sp => sp.GetRequiredService<PdfProcessingPipeline>());

builder.Services.AddSingleton<InMemoryLogStore>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddCors();

var app = builder.Build();

// 注册内存日志 Provider，捕获运行时 ILogger 输出
var logStore = app.Services.GetRequiredService<InMemoryLogStore>();
app.Services.GetRequiredService<ILoggerFactory>().AddProvider(new InMemoryLoggerProvider(logStore));

app.UseCors(c => c.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

// === 健康检查 ===
app.MapGet("/health", (FinancialDataOrchestrator orchestrator) => Results.Ok(new
{
    service = "financial-worker",
    status = "healthy",
    reachable = true,
    baseUrl = "http://localhost:5120",
    dataRoot,
    dbPath,
    timestamp = DateTime.UtcNow,
    currentActivity = orchestrator.CurrentActivity ?? "空闲",
    lastCollectionTime = orchestrator.LastCollectionTime,
    lastCollectionResult = orchestrator.LastCollectionResult
}));

// === 配置 API ===
app.MapGet("/api/config", (FinancialDbContext db) =>
{
    var config = db.Config.FindById(1);
    return Results.Ok(config ?? new FinancialCollectionConfig());
});

app.MapPut("/api/config", (FinancialCollectionConfig config, FinancialDbContext db) =>
{
    config.Id = 1;
    config.UpdatedAt = DateTime.UtcNow;
    db.Config.Upsert(config);
    return Results.Ok(config);
});

// === 采集触发 API ===
app.MapPost("/api/collect/{symbol}", async (string symbol, FinancialDataOrchestrator orchestrator, CancellationToken ct) =>
{
    var result = await orchestrator.CollectAsync(symbol, ct);
    return Results.Ok(result);
});

app.MapPost("/api/collect-batch", async (string[] symbols, FinancialDataOrchestrator orchestrator, CancellationToken ct) =>
{
    var results = await orchestrator.CollectBatchAsync(symbols, ct);
    return Results.Ok(results);
});

app.MapPost("/api/pdf-collect/{symbol}", async (string symbol, PdfProcessingPipeline pipeline, CancellationToken ct) =>
{
    var result = await pipeline.ProcessAsync(symbol, 3, ct);
    return Results.Ok(result);
})
.WithName("PdfCollect");

// v0.4.1 §S2：单文件重新解析（同步，覆盖 stageLogs，更新 LastReparsedAt）
app.MapPost("/api/pdf-reparse/{id}", async (string id, IPdfProcessingPipeline pipeline, CancellationToken ct) =>
{
    var outcome = await pipeline.ReparseAsync(id, ct);
    return Results.Ok(outcome);
})
.WithName("PdfReparse");

// === 数据查询 API ===
app.MapGet("/api/reports/{symbol}", (string symbol, FinancialDbContext db) =>
{
    var reports = db.Reports.Find(r => r.Symbol == symbol)
        .OrderByDescending(r => r.ReportDate)
        .ToList();
    return Results.Ok(reports);
});

app.MapGet("/api/indicators/{symbol}", (string symbol, FinancialDbContext db) =>
{
    var indicators = db.Indicators.Find(i => i.Symbol == symbol)
        .OrderByDescending(i => i.ReportDate)
        .ToList();
    return Results.Ok(indicators);
});

app.MapGet("/api/dividends/{symbol}", (string symbol, FinancialDbContext db) =>
{
    var dividends = db.Dividends.Find(d => d.Symbol == symbol)
        .OrderByDescending(d => d.RecordDate)
        .ToList();
    return Results.Ok(dividends);
});

app.MapGet("/api/margin/{symbol}", (string symbol, FinancialDbContext db) =>
{
    var margin = db.MarginTrading.Find(m => m.Symbol == symbol)
        .OrderByDescending(m => m.TradeDate)
        .Take(100)
        .ToList();
    return Results.Ok(margin);
});

app.MapGet("/api/logs", (string? symbol, int? limit, FinancialDbContext db) =>
{
    var take = limit is > 0 ? limit.Value : 50;
    var query = symbol != null
        ? db.Logs.Find(l => l.Symbol == symbol)
        : db.Logs.FindAll();
    return Results.Ok(query.OrderByDescending(l => l.Timestamp).Take(take).ToList());
});

// === 运行时日志 API ===
app.MapGet("/api/runtime-logs", (long? afterId, int? count, InMemoryLogStore store) =>
{
    var take = count is > 0 and <= 500 ? count.Value : 200;
    var entries = afterId.HasValue
        ? store.GetEntries(afterId.Value, take)
        : store.GetLatest(take);
    return Results.Ok(entries);
});

app.Run();
