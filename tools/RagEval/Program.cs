using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var mode = args.FirstOrDefault(a => a.StartsWith("--mode="))?.Split('=')[1] ?? "bm25";
var workerUrl = args.FirstOrDefault(a => a.StartsWith("--url="))?.Split('=', 2)[1] ?? "http://localhost:5120";
var outputDir = args.FirstOrDefault(a => a.StartsWith("--output="))?.Split('=', 2)[1] ?? "docs";

Console.WriteLine($"RAG Evaluation — mode: {mode}, worker: {workerUrl}");
Console.WriteLine("=".PadRight(60, '='));

// Load eval set
var evalSetPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "eval-set.json");
if (!File.Exists(evalSetPath))
    evalSetPath = Path.Combine(Directory.GetCurrentDirectory(), "tools", "RagEval", "eval-set.json");
if (!File.Exists(evalSetPath))
{
    Console.Error.WriteLine($"ERROR: eval-set.json not found at {evalSetPath}");
    return 1;
}

var evalSetJson = File.ReadAllText(evalSetPath);
var evalSet = JsonSerializer.Deserialize<EvalSet>(evalSetJson)!;
Console.WriteLine($"Loaded {evalSet.Queries.Count} queries from eval set v{evalSet.Version}");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

// Check worker health
try
{
    var healthResp = await http.GetAsync($"{workerUrl}/health");
    if (!healthResp.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"ERROR: Worker health check failed: {healthResp.StatusCode}");
        return 1;
    }
    Console.WriteLine("Worker health: OK");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: Cannot reach worker at {workerUrl}: {ex.Message}");
    return 1;
}

var results = new List<QueryResult>();
int hit = 0, miss = 0;

foreach (var q in evalSet.Queries)
{
    var requestBody = new
    {
        query = q.Query,
        topK = 10
    };

    try
    {
        var resp = await http.PostAsJsonAsync($"{workerUrl}/api/rag/search", requestBody);
        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine($"  [{q.Id}] ERROR: {resp.StatusCode}");
            results.Add(new QueryResult { Id = q.Id, Category = q.Category, Query = q.Query, TopResults = new(), HitCount = 0 });
            miss++;
            continue;
        }

        var searchResult = await resp.Content.ReadFromJsonAsync<SearchResponse>();
        var topResults = searchResult?.Results ?? new();

        // Evaluate: check if any of the top-K results contain expected keywords
        var queryResults = new List<ResultItem>();
        foreach (var r in topResults)
        {
            var relevant = q.ExpectedKeywords.Any(kw =>
                r.Text.Contains(kw, StringComparison.OrdinalIgnoreCase));
            queryResults.Add(new ResultItem
            {
                ChunkId = r.ChunkId,
                Score = r.Score,
                Section = r.Section,
                Relevant = relevant,
                TextPreview = r.Text.Length > 80 ? r.Text[..80] + "..." : r.Text
            });
        }

        var hitCount = queryResults.Count(r => r.Relevant);
        if (hitCount > 0) hit++; else miss++;

        results.Add(new QueryResult
        {
            Id = q.Id,
            Category = q.Category,
            Query = q.Query,
            TopResults = queryResults,
            HitCount = hitCount
        });

        var status = hitCount > 0 ? "✅" : "❌";
        Console.WriteLine($"  [{q.Id}] {status} {q.Query} → {hitCount}/{topResults.Count} relevant (top score: {topResults.FirstOrDefault()?.Score:F4})");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  [{q.Id}] ERROR: {ex.Message}");
        results.Add(new QueryResult { Id = q.Id, Category = q.Category, Query = q.Query, TopResults = new(), HitCount = 0 });
        miss++;
    }
}

// Compute metrics
var ndcg5 = ComputeNdcg(results, 5);
var recall10 = ComputeRecall(results, 10);
var mrr = ComputeMrr(results);

Console.WriteLine();
Console.WriteLine("=".PadRight(60, '='));
Console.WriteLine($"Results: {hit}/{evalSet.Queries.Count} queries hit ({100.0 * hit / evalSet.Queries.Count:F1}%)");
Console.WriteLine($"nDCG@5:    {ndcg5:F4}");
Console.WriteLine($"Recall@10: {recall10:F4}");
Console.WriteLine($"MRR:       {mrr:F4}");

// Generate report
var reportDate = DateTime.Now.ToString("yyyy-MM-dd");
var reportPath = Path.Combine(outputDir, $"RAG-eval-{mode}-{reportDate}.md");
var report = new StringBuilder();
report.AppendLine($"# RAG 评估报告 — {mode} ({reportDate})");
report.AppendLine();
report.AppendLine($"- **模式**: {mode}");
report.AppendLine($"- **评估集**: v{evalSet.Version}, {evalSet.Queries.Count} queries");
report.AppendLine($"- **Worker**: {workerUrl}");
report.AppendLine();
report.AppendLine("## 指标汇总");
report.AppendLine();
report.AppendLine("| 指标 | 值 |");
report.AppendLine("|------|-----|");
report.AppendLine($"| nDCG@5 | {ndcg5:F4} |");
report.AppendLine($"| Recall@10 | {recall10:F4} |");
report.AppendLine($"| MRR | {mrr:F4} |");
report.AppendLine($"| Hit Rate | {100.0 * hit / evalSet.Queries.Count:F1}% ({hit}/{evalSet.Queries.Count}) |");
report.AppendLine();
report.AppendLine("## 分类表现");
report.AppendLine();
report.AppendLine("| 类别 | 命中 | 总数 | 命中率 |");
report.AppendLine("|------|------|------|--------|");

var categories = results.GroupBy(r => r.Category);
foreach (var cat in categories)
{
    var catHit = cat.Count(r => r.HitCount > 0);
    var catTotal = cat.Count();
    report.AppendLine($"| {cat.Key} | {catHit} | {catTotal} | {100.0 * catHit / catTotal:F0}% |");
}

report.AppendLine();
report.AppendLine("## 逐条结果");
report.AppendLine();
report.AppendLine("| ID | 类别 | 查询 | Top-5 命中 | Top Score |");
report.AppendLine("|----|------|------|-----------|-----------|");
foreach (var r in results)
{
    var topScore = r.TopResults.FirstOrDefault()?.Score;
    var scoreStr = topScore.HasValue ? $"{topScore.Value:F4}" : "N/A";
    var hitStr = r.HitCount > 0 ? $"✅ {r.HitCount}" : "❌ 0";
    report.AppendLine($"| {r.Id} | {r.Category} | {r.Query} | {hitStr} | {scoreStr} |");
}

Directory.CreateDirectory(outputDir);
File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
Console.WriteLine($"\nReport saved to: {reportPath}");
return 0;

// --- Metric functions ---

static double ComputeNdcg(List<QueryResult> results, int k)
{
    double totalNdcg = 0;
    foreach (var r in results)
    {
        var topK = r.TopResults.Take(k).ToList();
        if (topK.Count == 0) continue;

        // DCG: sum of rel_i / log2(i+1)
        double dcg = 0;
        for (int i = 0; i < topK.Count; i++)
        {
            var rel = topK[i].Relevant ? 1.0 : 0.0;
            dcg += rel / Math.Log2(i + 2); // i+2 because log2(1) = 0
        }

        // Ideal DCG: all relevant results first
        var idealRels = topK.Select(t => t.Relevant ? 1.0 : 0.0).OrderByDescending(x => x).ToList();
        double idcg = 0;
        for (int i = 0; i < idealRels.Count; i++)
        {
            idcg += idealRels[i] / Math.Log2(i + 2);
        }

        totalNdcg += idcg > 0 ? dcg / idcg : 0;
    }
    return results.Count > 0 ? totalNdcg / results.Count : 0;
}

static double ComputeRecall(List<QueryResult> results, int k)
{
    // For each query: did any of top-K contain a relevant result?
    int recalled = results.Count(r => r.TopResults.Take(k).Any(t => t.Relevant));
    return results.Count > 0 ? (double)recalled / results.Count : 0;
}

static double ComputeMrr(List<QueryResult> results)
{
    double totalRr = 0;
    foreach (var r in results)
    {
        var firstRelevantIdx = -1;
        for (int i = 0; i < r.TopResults.Count; i++)
        {
            if (r.TopResults[i].Relevant)
            {
                firstRelevantIdx = i;
                break;
            }
        }
        if (firstRelevantIdx >= 0)
            totalRr += 1.0 / (firstRelevantIdx + 1);
    }
    return results.Count > 0 ? totalRr / results.Count : 0;
}

// --- Models ---

record EvalSet
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = "";
    [JsonPropertyName("description")]
    public string Description { get; init; } = "";
    [JsonPropertyName("queries")]
    public List<EvalQuery> Queries { get; init; } = new();
}

record EvalQuery
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    [JsonPropertyName("category")]
    public string Category { get; init; } = "";
    [JsonPropertyName("query")]
    public string Query { get; init; } = "";
    [JsonPropertyName("expected_keywords")]
    public List<string> ExpectedKeywords { get; init; } = new();
    [JsonPropertyName("description")]
    public string Description { get; init; } = "";
}

record SearchResponse
{
    [JsonPropertyName("query")]
    public string Query { get; init; } = "";
    [JsonPropertyName("totalResults")]
    public int TotalResults { get; init; }
    [JsonPropertyName("results")]
    public List<SearchResultItem> Results { get; init; } = new();
}

record SearchResultItem
{
    [JsonPropertyName("chunkId")]
    public string ChunkId { get; init; } = "";
    [JsonPropertyName("score")]
    public double Score { get; init; }
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";
    [JsonPropertyName("section")]
    public string? Section { get; init; }
    [JsonPropertyName("symbol")]
    public string Symbol { get; init; } = "";
}

class QueryResult
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public string Query { get; init; } = "";
    public List<ResultItem> TopResults { get; init; } = new();
    public int HitCount { get; init; }
}

class ResultItem
{
    public string ChunkId { get; init; } = "";
    public double Score { get; init; }
    public string? Section { get; init; }
    public bool Relevant { get; init; }
    public string TextPreview { get; init; } = "";
}
