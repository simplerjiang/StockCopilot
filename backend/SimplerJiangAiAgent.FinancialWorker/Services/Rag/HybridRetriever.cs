using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SimplerJiangAiAgent.FinancialWorker.Data;

namespace SimplerJiangAiAgent.FinancialWorker.Services.Rag;

public enum SearchMode
{
    Bm25,
    Vector,
    Hybrid
}

public class HybridRetriever : IRetriever
{
    private readonly RagDbContext _ragDb;
    private readonly IChineseTokenizer _tokenizer;
    private readonly IEmbedder _embedder;
    private readonly ILogger<HybridRetriever> _logger;
    private const int RrfK = 60; // RRF constant

    public HybridRetriever(RagDbContext ragDb, IChineseTokenizer tokenizer, IEmbedder embedder, ILogger<HybridRetriever> logger)
    {
        _ragDb = ragDb;
        _tokenizer = tokenizer;
        _embedder = embedder;
        _logger = logger;
    }

    /// <summary>Actual mode used (may differ from requested if embedder unavailable).</summary>
    public SearchMode ActualMode { get; private set; }

    public async Task<List<RetrievedChunk>> RetrieveAsync(
        string query,
        string? symbol = null,
        string? reportDate = null,
        string? reportType = null,
        int topK = 5,
        CancellationToken ct = default)
    {
        return await RetrieveAsync(query, SearchMode.Hybrid, symbol, reportDate, reportType, topK, ct);
    }

    public async Task<List<RetrievedChunk>> RetrieveAsync(
        string query,
        SearchMode mode,
        string? symbol = null,
        string? reportDate = null,
        string? reportType = null,
        int topK = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            ActualMode = mode;
            return new List<RetrievedChunk>();
        }

        // Determine actual mode (degrade if embedder unavailable)
        if ((mode == SearchMode.Vector || mode == SearchMode.Hybrid) && !_embedder.IsAvailable)
        {
            _logger.LogDebug("[RAG] Embedder unavailable, degrading from {Requested} to BM25", mode);
            mode = SearchMode.Bm25;
        }
        ActualMode = mode;

        return mode switch
        {
            SearchMode.Bm25 => Bm25Search(query, symbol, reportDate, reportType, topK),
            SearchMode.Vector => await VectorSearch(query, symbol, reportDate, reportType, topK, ct),
            SearchMode.Hybrid => await HybridSearch(query, symbol, reportDate, reportType, topK, ct),
            _ => new List<RetrievedChunk>()
        };
    }

    private List<RetrievedChunk> Bm25Search(string query, string? symbol, string? reportDate, string? reportType, int topK)
    {
        var tokenizedQuery = _tokenizer.Tokenize(query);
        if (string.IsNullOrWhiteSpace(tokenizedQuery))
            return new();

        var tokens = tokenizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // 过滤掉常见停用词
        var stopwords = new HashSet<string> { "的", "了", "是", "在", "和", "与", "或", "及", "等", "对", "为", "中", "有", "从", "到", "以", "上", "下", "个", "这", "那", "最近", "请", "帮", "我", "看看", "一下", "什么", "怎么", "如何", "是否", "还", "能", "可以", "可能", "应该", "需要", "想", "要", "做", "吗", "呢", "啊", "吧", "哪", "几", "多少" };
        var filteredTokens = tokens.Where(t => !stopwords.Contains(t) && t.Length > 1).ToArray();
        if (filteredTokens.Length == 0) filteredTokens = tokens; // 降级：停用词过滤后为空则保留原始

        tokenizedQuery = string.Join(" OR ", filteredTokens
            .Select(token => "\"" + token.Replace("\"", "\"\"") + "\""));

        using var conn = new SqliteConnection(_ragDb.ConnectionString);
        conn.Open();

        var sql = new System.Text.StringBuilder();
        sql.Append(@"
            SELECT c.chunk_id, c.source_id, c.symbol, c.report_date, c.report_type,
                   c.section, c.block_kind, c.page_start, c.page_end, c.text,
                   bm25(chunks_fts) as score
            FROM chunks_fts f
            JOIN chunks c ON c.rowid = f.rowid
            WHERE chunks_fts MATCH $query");
        AppendFilters(sql, symbol, reportDate, reportType);
        sql.Append(" ORDER BY bm25(chunks_fts) LIMIT $topK");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql.ToString();
        cmd.Parameters.AddWithValue("$query", tokenizedQuery);
        AddFilterParams(cmd, symbol, reportDate, reportType);
        cmd.Parameters.AddWithValue("$topK", topK);

        return ReadChunks(cmd);
    }

    private async Task<List<RetrievedChunk>> VectorSearch(string query, string? symbol, string? reportDate, string? reportType, int topK, CancellationToken ct)
    {
        var queryEmbedding = await _embedder.EmbedAsync(query, ct);
        if (queryEmbedding == null)
            return new();

        var vectorResults = _ragDb.SearchByVector(queryEmbedding, topK, symbol, reportDate, reportType);

        // Load chunk details
        var results = new List<RetrievedChunk>();
        using var conn = new SqliteConnection(_ragDb.ConnectionString);
        conn.Open();

        foreach (var (chunkId, similarity) in vectorResults)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT chunk_id, source_id, symbol, report_date, report_type,
                       section, block_kind, page_start, page_end, text
                FROM chunks WHERE chunk_id = $chunk_id";
            cmd.Parameters.AddWithValue("$chunk_id", chunkId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                results.Add(new RetrievedChunk
                {
                    ChunkId = reader.GetString(0),
                    SourceId = reader.GetString(1),
                    Symbol = reader.GetString(2),
                    ReportDate = reader.GetString(3),
                    ReportType = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Section = reader.IsDBNull(5) ? null : reader.GetString(5),
                    BlockKind = reader.GetString(6),
                    PageStart = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    PageEnd = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    Text = reader.GetString(9),
                    Score = similarity
                });
            }
        }

        return results;
    }

    private async Task<List<RetrievedChunk>> HybridSearch(string query, string? symbol, string? reportDate, string? reportType, int topK, CancellationToken ct)
    {
        // Run both searches with expanded topK to get more candidates for fusion
        var expandedK = topK * 3;
        var bm25Task = Task.FromResult(Bm25Search(query, symbol, reportDate, reportType, expandedK));
        var vectorTask = VectorSearch(query, symbol, reportDate, reportType, expandedK, ct);

        await Task.WhenAll(bm25Task, vectorTask);

        var bm25Results = bm25Task.Result;
        var vectorResults = vectorTask.Result;

        // RRF (Reciprocal Rank Fusion)
        var rrfScores = new Dictionary<string, (double Score, RetrievedChunk Chunk)>();

        for (int i = 0; i < bm25Results.Count; i++)
        {
            var chunk = bm25Results[i];
            var rrfScore = 1.0 / (RrfK + i + 1);
            rrfScores[chunk.ChunkId] = (rrfScore, chunk);
        }

        for (int i = 0; i < vectorResults.Count; i++)
        {
            var chunk = vectorResults[i];
            var rrfScore = 1.0 / (RrfK + i + 1);
            if (rrfScores.TryGetValue(chunk.ChunkId, out var existing))
            {
                rrfScores[chunk.ChunkId] = (existing.Score + rrfScore, existing.Chunk);
            }
            else
            {
                rrfScores[chunk.ChunkId] = (rrfScore, chunk);
            }
        }

        return rrfScores.Values
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x =>
            {
                x.Chunk.Score = x.Score;
                return x.Chunk;
            })
            .ToList();
    }

    private static void AppendFilters(System.Text.StringBuilder sql, string? symbol, string? reportDate, string? reportType)
    {
        if (!string.IsNullOrEmpty(symbol))
            sql.Append(" AND c.symbol = $symbol");
        if (!string.IsNullOrEmpty(reportDate))
            sql.Append(" AND c.report_date = $reportDate");
        if (!string.IsNullOrEmpty(reportType))
            sql.Append(" AND c.report_type = $reportType");
    }

    private static void AddFilterParams(SqliteCommand cmd, string? symbol, string? reportDate, string? reportType)
    {
        if (!string.IsNullOrEmpty(symbol))
            cmd.Parameters.AddWithValue("$symbol", symbol);
        if (!string.IsNullOrEmpty(reportDate))
            cmd.Parameters.AddWithValue("$reportDate", reportDate);
        if (!string.IsNullOrEmpty(reportType))
            cmd.Parameters.AddWithValue("$reportType", reportType);
    }

    private static List<RetrievedChunk> ReadChunks(SqliteCommand cmd)
    {
        var results = new List<RetrievedChunk>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new RetrievedChunk
            {
                ChunkId = reader.GetString(0),
                SourceId = reader.GetString(1),
                Symbol = reader.GetString(2),
                ReportDate = reader.GetString(3),
                ReportType = reader.IsDBNull(4) ? null : reader.GetString(4),
                Section = reader.IsDBNull(5) ? null : reader.GetString(5),
                BlockKind = reader.GetString(6),
                PageStart = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                PageEnd = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                Text = reader.GetString(9),
                Score = reader.GetDouble(10)
            });
        }
        return results;
    }
}
