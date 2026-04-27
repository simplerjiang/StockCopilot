using SimplerJiangAiAgent.FinancialWorker;
using SimplerJiangAiAgent.FinancialWorker.Data;
using SimplerJiangAiAgent.FinancialWorker.Models;
using SimplerJiangAiAgent.FinancialWorker.Services;
using SimplerJiangAiAgent.FinancialWorker.Services.Announcement;
using SimplerJiangAiAgent.FinancialWorker.Services.Pdf;
using SimplerJiangAiAgent.FinancialWorker.Services.Rag;

var builder = WebApplication.CreateBuilder(args);

// Kestrel 监听 5120 端口（不与主 API 5119 冲突）
builder.WebHost.UseUrls("http://localhost:5120");

var dataRoot = FinancialWorkerRuntimePaths.ResolveDataRoot();
var appDataPath = FinancialWorkerRuntimePaths.ResolveAppDataPath();
var dbPath = FinancialWorkerRuntimePaths.ResolveFinancialDatabasePath();
Directory.CreateDirectory(appDataPath);

builder.Services.AddSingleton(new FinancialDbContext($"Filename={dbPath};Connection=shared"));

var ragDbPath = FinancialWorkerRuntimePaths.ResolveRagDatabasePath();
builder.Services.AddSingleton(new RagDbContext($"Data Source={ragDbPath}"));
builder.Services.AddSingleton<IChineseTokenizer, JiebaTokenizer>();
builder.Services.AddSingleton<IChunker, FinancialReportChunker>();
// v0.4.3 S2: OllamaEmbedder (falls back gracefully if Ollama unavailable)
builder.Services.AddHttpClient<OllamaEmbedder>(client =>
{
    client.BaseAddress = new Uri("http://localhost:11434");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<IEmbedder>(sp =>
{
    var ollamaEmbedder = sp.GetRequiredService<OllamaEmbedder>();
    return ollamaEmbedder;
});
builder.Services.AddSingleton<HybridRetriever>();
builder.Services.AddSingleton<IRetriever>(sp => sp.GetRequiredService<HybridRetriever>());

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
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
    client.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
    client.Timeout = TimeSpan.FromSeconds(60);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
});

builder.Services.AddSingleton<FinancialDataOrchestrator>();

builder.Services.AddHttpClient<AnnouncementPdfCollector>(client =>
{
    client.DefaultRequestHeaders.Add("User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Add("Accept", "*/*");
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddSingleton<AnnouncementPdfProcessor>();

builder.Services.AddSingleton<IPdfTextExtractor, DocnetExtractor>();
builder.Services.AddSingleton<IPdfTextExtractor, PdfPigExtractor>();
builder.Services.AddSingleton<IPdfTextExtractor, IText7Extractor>();
builder.Services.AddSingleton<PdfVotingEngine>();
builder.Services.AddSingleton<FinancialTableParser>();
builder.Services.AddSingleton<PdfProcessingPipeline>();
builder.Services.AddSingleton<IPdfProcessingPipeline>(sp => sp.GetRequiredService<PdfProcessingPipeline>());

var logStore = new InMemoryLogStore();
builder.Services.AddSingleton(logStore);
builder.Logging.AddProvider(new InMemoryLoggerProvider(logStore));
builder.Services.AddHostedService<Worker>();
builder.Services.AddCors();

var app = builder.Build();

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

// v0.4.7 S2+S3: 东方财富公告 PDF 采集 + RAG 入库
app.MapPost("/api/announcement-pdf-collect/{symbol}", async (string symbol, AnnouncementPdfCollector collector, AnnouncementPdfProcessor processor, CancellationToken ct) =>
{
    var downloaded = await collector.CollectAsync(symbol, 10, ct);
    var chunksIndexed = downloaded.Count > 0
        ? await processor.ProcessAsync(downloaded, ct)
        : 0;
    return Results.Ok(new { downloaded = downloaded.Count, chunksIndexed, files = downloaded });
})
.WithName("AnnouncementPdfCollect");

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

// v0.4.3 S8: Embedding status endpoint
app.MapGet("/api/embedding/status", (RagDbContext ragDb, IEmbedder embedder) =>
{
    var dimension = ragDb.GetEmbeddingDimension();
    var embeddingCount = ragDb.CountEmbeddings();
    var chunkCount = ragDb.CountChunks();

    return Results.Ok(new
    {
        available = embedder.IsAvailable,
        model = "bge-m3",
        dimension,
        embeddingCount,
        chunkCount,
        coverage = chunkCount > 0 ? (double)embeddingCount / chunkCount : 0
    });
});

// v0.4.7: Embedding backfill — fill missing embeddings for all chunks
app.MapPost("/api/embedding/backfill", async (RagDbContext ragDb, IEmbedder embedder, CancellationToken ct) =>
{
    if (!embedder.IsAvailable)
        return Results.Ok(new { filled = 0, message = "Embedder not available (Ollama offline or model missing)" });

    var missing = ragDb.GetChunkIdsWithoutEmbedding(500);
    if (missing.Count == 0)
        return Results.Ok(new { filled = 0, message = "All chunks already have embeddings" });

    var filled = 0;
    foreach (var (chunkId, text) in missing)
    {
        ct.ThrowIfCancellationRequested();
        var embedding = await embedder.EmbedAsync(text, ct);
        if (embedding != null)
        {
            ragDb.UpsertEmbedding(chunkId, embedding, "ollama");
            filled++;
        }
    }

    return Results.Ok(new { filled, total = missing.Count });
})
.WithName("EmbeddingBackfill");

// v0.4.2 S6: RAG search endpoint
app.MapPost("/api/rag/search", async (
    RagSearchRequest request,
    HybridRetriever retriever,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Query))
        return Results.BadRequest(new { error = "query is required" });

    var topK = request.TopK is > 0 and <= 50 ? request.TopK.Value : 5;
    var mode = (request.Mode?.ToLowerInvariant()) switch
    {
        "bm25" => SearchMode.Bm25,
        "vector" => SearchMode.Vector,
        "hybrid" => SearchMode.Hybrid,
        _ => SearchMode.Hybrid
    };

    var results = await retriever.RetrieveAsync(
        request.Query,
        mode,
        request.Symbol,
        request.ReportDate,
        request.ReportType,
        request.SourceType,
        topK,
        ct);

    return Results.Ok(new RagSearchResponse
    {
        Query = request.Query,
        TotalResults = results.Count,
        Mode = retriever.ActualMode.ToString().ToLowerInvariant(),
        Results = results.Select(r => new RagSearchResultItem
        {
            ChunkId = r.ChunkId,
            SourceId = r.SourceId,
            Symbol = r.Symbol,
            ReportDate = r.ReportDate,
            ReportType = r.ReportType,
            Section = r.Section,
            BlockKind = r.BlockKind,
            PageStart = r.PageStart,
            PageEnd = r.PageEnd,
            Text = r.Text,
            Score = r.Score
        }).ToList()
    });
});

app.Run();
