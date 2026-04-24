using Microsoft.Data.Sqlite;
using SimplerJiangAiAgent.FinancialWorker.Models;

namespace SimplerJiangAiAgent.FinancialWorker.Data;

public class RagDbContext : IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;

    public string ConnectionString => _connectionString;

    public RagDbContext(string connectionString)
    {
        _connectionString = connectionString;
        EnsureDatabase();
    }

    public SqliteConnection GetConnection()
    {
        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
        {
            _connection = new SqliteConnection(_connectionString);
            _connection.Open();
        }
        return _connection;
    }

    private void EnsureDatabase()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS chunks (
                chunk_id TEXT PRIMARY KEY,
                source_type TEXT NOT NULL DEFAULT 'financial_report',
                source_id TEXT NOT NULL,
                symbol TEXT NOT NULL,
                report_date TEXT NOT NULL,
                report_type TEXT,
                section TEXT,
                block_kind TEXT NOT NULL DEFAULT 'prose',
                page_start INTEGER,
                page_end INTEGER,
                text TEXT NOT NULL,
                tokenized_text TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE INDEX IF NOT EXISTS idx_chunks_symbol ON chunks(symbol);
            CREATE INDEX IF NOT EXISTS idx_chunks_source ON chunks(source_id);
            CREATE INDEX IF NOT EXISTS idx_chunks_report_date ON chunks(report_date);
        ";
        cmd.ExecuteNonQuery();

        // FTS5 virtual table for full-text search
        using var ftsCmd = conn.CreateCommand();
        ftsCmd.CommandText = @"
            CREATE VIRTUAL TABLE IF NOT EXISTS chunks_fts USING fts5(
                tokenized_text,
                content='chunks',
                content_rowid='rowid'
            );
        ";
        ftsCmd.ExecuteNonQuery();

        // Triggers to keep FTS5 in sync
        var triggers = new[]
        {
            @"CREATE TRIGGER IF NOT EXISTS chunks_ai AFTER INSERT ON chunks BEGIN
                INSERT INTO chunks_fts(rowid, tokenized_text) VALUES (new.rowid, new.tokenized_text);
            END;",
            @"CREATE TRIGGER IF NOT EXISTS chunks_ad AFTER DELETE ON chunks BEGIN
                INSERT INTO chunks_fts(chunks_fts, rowid, tokenized_text) VALUES('delete', old.rowid, old.tokenized_text);
            END;",
            @"CREATE TRIGGER IF NOT EXISTS chunks_au AFTER UPDATE ON chunks BEGIN
                INSERT INTO chunks_fts(chunks_fts, rowid, tokenized_text) VALUES('delete', old.rowid, old.tokenized_text);
                INSERT INTO chunks_fts(rowid, tokenized_text) VALUES (new.rowid, new.tokenized_text);
            END;"
        };
        foreach (var trigger in triggers)
        {
            using var tCmd = conn.CreateCommand();
            tCmd.CommandText = trigger;
            tCmd.ExecuteNonQuery();
        }

        // chunk_embeddings table for vector search
        using var embCmd = conn.CreateCommand();
        embCmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS chunk_embeddings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                chunk_id TEXT NOT NULL UNIQUE,
                embedding BLOB NOT NULL,
                model_name TEXT NOT NULL,
                dimension INTEGER NOT NULL,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (chunk_id) REFERENCES chunks(chunk_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_chunk_embeddings_chunk_id ON chunk_embeddings(chunk_id);
        ";
        embCmd.ExecuteNonQuery();
    }

    /// <summary>Insert a chunk and let triggers sync FTS5.</summary>
    public void InsertChunk(FinancialChunk chunk)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO chunks 
            (chunk_id, source_type, source_id, symbol, report_date, report_type, section, block_kind, page_start, page_end, text, tokenized_text, created_at)
            VALUES 
            ($chunk_id, $source_type, $source_id, $symbol, $report_date, $report_type, $section, $block_kind, $page_start, $page_end, $text, $tokenized_text, $created_at)";
        cmd.Parameters.AddWithValue("$chunk_id", chunk.ChunkId);
        cmd.Parameters.AddWithValue("$source_type", chunk.SourceType);
        cmd.Parameters.AddWithValue("$source_id", chunk.SourceId);
        cmd.Parameters.AddWithValue("$symbol", chunk.Symbol);
        cmd.Parameters.AddWithValue("$report_date", chunk.ReportDate);
        cmd.Parameters.AddWithValue("$report_type", (object?)chunk.ReportType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$section", (object?)chunk.Section ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$block_kind", chunk.BlockKind);
        cmd.Parameters.AddWithValue("$page_start", (object?)chunk.PageStart ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$page_end", (object?)chunk.PageEnd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$text", chunk.Text);
        cmd.Parameters.AddWithValue("$tokenized_text", chunk.TokenizedText);
        cmd.Parameters.AddWithValue("$created_at", chunk.CreatedAt.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Bulk insert chunks in a transaction.</summary>
    public void InsertChunks(IEnumerable<FinancialChunk> chunks)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        foreach (var chunk in chunks)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT OR REPLACE INTO chunks 
                (chunk_id, source_type, source_id, symbol, report_date, report_type, section, block_kind, page_start, page_end, text, tokenized_text, created_at)
                VALUES 
                ($chunk_id, $source_type, $source_id, $symbol, $report_date, $report_type, $section, $block_kind, $page_start, $page_end, $text, $tokenized_text, $created_at)";
            cmd.Parameters.AddWithValue("$chunk_id", chunk.ChunkId);
            cmd.Parameters.AddWithValue("$source_type", chunk.SourceType);
            cmd.Parameters.AddWithValue("$source_id", chunk.SourceId);
            cmd.Parameters.AddWithValue("$symbol", chunk.Symbol);
            cmd.Parameters.AddWithValue("$report_date", chunk.ReportDate);
            cmd.Parameters.AddWithValue("$report_type", (object?)chunk.ReportType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$section", (object?)chunk.Section ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$block_kind", chunk.BlockKind);
            cmd.Parameters.AddWithValue("$page_start", (object?)chunk.PageStart ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$page_end", (object?)chunk.PageEnd ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$text", chunk.Text);
            cmd.Parameters.AddWithValue("$tokenized_text", chunk.TokenizedText);
            cmd.Parameters.AddWithValue("$created_at", chunk.CreatedAt.ToString("o"));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Delete all chunks for a given source document.</summary>
    public int DeleteChunksBySourceId(string sourceId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM chunks WHERE source_id = $source_id";
        cmd.Parameters.AddWithValue("$source_id", sourceId);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>Count chunks, optionally filtered by source_id.</summary>
    public int CountChunks(string? sourceId = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        if (sourceId != null)
        {
            cmd.CommandText = "SELECT COUNT(*) FROM chunks WHERE source_id = $source_id";
            cmd.Parameters.AddWithValue("$source_id", sourceId);
        }
        else
        {
            cmd.CommandText = "SELECT COUNT(*) FROM chunks";
        }
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>Insert or replace embedding for a chunk.</summary>
    public void UpsertEmbedding(string chunkId, float[] embedding, string modelName)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO chunk_embeddings (chunk_id, embedding, model_name, dimension, created_at)
            VALUES ($chunk_id, $embedding, $model_name, $dimension, datetime('now'))";
        cmd.Parameters.AddWithValue("$chunk_id", chunkId);
        cmd.Parameters.AddWithValue("$embedding", FloatsToBlob(embedding));
        cmd.Parameters.AddWithValue("$model_name", modelName);
        cmd.Parameters.AddWithValue("$dimension", embedding.Length);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Bulk upsert embeddings in a transaction.</summary>
    public void UpsertEmbeddings(IEnumerable<(string ChunkId, float[] Embedding, string ModelName)> items)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        foreach (var (chunkId, embedding, modelName) in items)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT OR REPLACE INTO chunk_embeddings (chunk_id, embedding, model_name, dimension, created_at)
                VALUES ($chunk_id, $embedding, $model_name, $dimension, datetime('now'))";
            cmd.Parameters.AddWithValue("$chunk_id", chunkId);
            cmd.Parameters.AddWithValue("$embedding", FloatsToBlob(embedding));
            cmd.Parameters.AddWithValue("$model_name", modelName);
            cmd.Parameters.AddWithValue("$dimension", embedding.Length);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Delete embeddings for all chunks of a given source document.</summary>
    public int DeleteEmbeddingsBySourceId(string sourceId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM chunk_embeddings 
            WHERE chunk_id IN (SELECT chunk_id FROM chunks WHERE source_id = $source_id)";
        cmd.Parameters.AddWithValue("$source_id", sourceId);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Vector similarity search using cosine similarity computed in C#.
    /// Returns chunks sorted by descending similarity.
    /// </summary>
    public List<(string ChunkId, double Similarity)> SearchByVector(float[] queryEmbedding, int topK = 5,
        string? symbol = null, string? reportDate = null, string? reportType = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var sql = new System.Text.StringBuilder();
        sql.Append(@"
            SELECT e.chunk_id, e.embedding
            FROM chunk_embeddings e
            JOIN chunks c ON c.chunk_id = e.chunk_id
            WHERE 1=1");
        if (!string.IsNullOrEmpty(symbol))
            sql.Append(" AND c.symbol = $symbol");
        if (!string.IsNullOrEmpty(reportDate))
            sql.Append(" AND c.report_date = $reportDate");
        if (!string.IsNullOrEmpty(reportType))
            sql.Append(" AND c.report_type = $reportType");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql.ToString();
        if (!string.IsNullOrEmpty(symbol))
            cmd.Parameters.AddWithValue("$symbol", symbol);
        if (!string.IsNullOrEmpty(reportDate))
            cmd.Parameters.AddWithValue("$reportDate", reportDate);
        if (!string.IsNullOrEmpty(reportType))
            cmd.Parameters.AddWithValue("$reportType", reportType);

        var candidates = new List<(string ChunkId, float[] Embedding)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var chunkId = reader.GetString(0);
            var blob = (byte[])reader[1];
            candidates.Add((chunkId, BlobToFloats(blob)));
        }

        return candidates
            .Select(c => (c.ChunkId, Similarity: CosineSimilarity(queryEmbedding, c.Embedding)))
            .OrderByDescending(x => x.Similarity)
            .Take(topK)
            .ToList();
    }

    /// <summary>Get the embedding dimension currently stored (0 if no embeddings).</summary>
    public int GetEmbeddingDimension()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT dimension FROM chunk_embeddings LIMIT 1";
        var result = cmd.ExecuteScalar();
        return result != null ? Convert.ToInt32(result) : 0;
    }

    /// <summary>Count embeddings, optionally filtered by source_id.</summary>
    public int CountEmbeddings(string? sourceId = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        if (sourceId != null)
        {
            cmd.CommandText = @"
                SELECT COUNT(*) FROM chunk_embeddings 
                WHERE chunk_id IN (SELECT chunk_id FROM chunks WHERE source_id = $source_id)";
            cmd.Parameters.AddWithValue("$source_id", sourceId);
        }
        else
        {
            cmd.CommandText = "SELECT COUNT(*) FROM chunk_embeddings";
        }
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static byte[] FloatsToBlob(float[] floats)
    {
        var bytes = new byte[floats.Length * 4];
        Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BlobToFloats(byte[] blob)
    {
        var floats = new float[blob.Length / 4];
        Buffer.BlockCopy(blob, 0, floats, 0, blob.Length);
        return floats;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom > 0 ? dot / denom : 0;
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
