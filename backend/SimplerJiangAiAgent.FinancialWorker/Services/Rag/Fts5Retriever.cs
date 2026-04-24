using Microsoft.Data.Sqlite;
using SimplerJiangAiAgent.FinancialWorker.Data;

namespace SimplerJiangAiAgent.FinancialWorker.Services.Rag;

public class Fts5Retriever : IRetriever
{
    private readonly RagDbContext _ragDb;
    private readonly IChineseTokenizer _tokenizer;

    public Fts5Retriever(RagDbContext ragDb, IChineseTokenizer tokenizer)
    {
        _ragDb = ragDb;
        _tokenizer = tokenizer;
    }

    public Task<List<RetrievedChunk>> RetrieveAsync(
        string query,
        string? symbol = null,
        string? reportDate = null,
        string? reportType = null,
        int topK = 5,
        CancellationToken ct = default)
    {
        var results = new List<RetrievedChunk>();

        // Tokenize the query the same way we tokenize chunks
        var tokenizedQuery = _tokenizer.Tokenize(query);
        if (string.IsNullOrWhiteSpace(tokenizedQuery))
            return Task.FromResult(results);

        // Escape FTS5 special characters by quoting each token
        tokenizedQuery = string.Join(" ", tokenizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => "\"" + token.Replace("\"", "\"\"") + "\""));

        using var conn = new SqliteConnection(_ragDb.ConnectionString);
        conn.Open();

        // Build query: FTS5 MATCH + metadata WHERE filters
        // bm25() returns negative values (more negative = better match), so ORDER BY bm25() ASC
        var sql = new System.Text.StringBuilder();
        sql.Append(@"
            SELECT c.chunk_id, c.source_id, c.symbol, c.report_date, c.report_type,
                   c.section, c.block_kind, c.page_start, c.page_end, c.text,
                   bm25(chunks_fts) as score
            FROM chunks_fts f
            JOIN chunks c ON c.rowid = f.rowid
            WHERE chunks_fts MATCH $query");

        if (!string.IsNullOrEmpty(symbol))
            sql.Append(" AND c.symbol = $symbol");
        if (!string.IsNullOrEmpty(reportDate))
            sql.Append(" AND c.report_date = $reportDate");
        if (!string.IsNullOrEmpty(reportType))
            sql.Append(" AND c.report_type = $reportType");

        sql.Append(" ORDER BY bm25(chunks_fts) LIMIT $topK");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql.ToString();
        cmd.Parameters.AddWithValue("$query", tokenizedQuery);
        if (!string.IsNullOrEmpty(symbol))
            cmd.Parameters.AddWithValue("$symbol", symbol);
        if (!string.IsNullOrEmpty(reportDate))
            cmd.Parameters.AddWithValue("$reportDate", reportDate);
        if (!string.IsNullOrEmpty(reportType))
            cmd.Parameters.AddWithValue("$reportType", reportType);
        cmd.Parameters.AddWithValue("$topK", topK);

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

        return Task.FromResult(results);
    }
}
