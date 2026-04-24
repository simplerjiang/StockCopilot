using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

/// <summary>
/// Enriches AI analysis context with financial report RAG evidence (v0.4.3 S6).
/// Queries FinancialWorker's RAG search endpoint.
/// </summary>
public class RagContextEnricher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RagContextEnricher> _logger;

    public RagContextEnricher(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<RagContextEnricher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Query RAG for relevant financial report chunks.
    /// Returns empty list if worker unavailable or no results.
    /// </summary>
    public async Task<List<RagCitationDto>> EnrichAsync(
        string query,
        string? symbol = null,
        int topK = 3,
        CancellationToken ct = default)
    {
        try
        {
            // Normalize symbol: chunks are stored without sh/sz prefix
            if (symbol != null && symbol.Length == 8 &&
                (symbol.StartsWith("sh", StringComparison.OrdinalIgnoreCase) ||
                 symbol.StartsWith("sz", StringComparison.OrdinalIgnoreCase)))
            {
                symbol = symbol[2..];
            }

            var workerBaseUrl = _configuration["FinancialWorker:BaseUrl"] ?? "http://localhost:5120";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var request = new
            {
                query,
                symbol,
                topK,
                mode = "hybrid"
            };

            var response = await client.PostAsJsonAsync($"{workerBaseUrl}/api/rag/search", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[RAG] Worker RAG search returned {Status} for symbol={Symbol} query={Query}", response.StatusCode, symbol, query);
                return new();
            }

            var result = await response.Content.ReadFromJsonAsync<RagSearchResponseInternal>(cancellationToken: ct);
            if (result?.Results == null || result.Results.Count == 0)
                return new();

            return result.Results.Select(r => new RagCitationDto
            {
                ChunkId = r.ChunkId,
                Symbol = r.Symbol,
                ReportDate = r.ReportDate,
                ReportType = r.ReportType,
                Section = r.Section,
                BlockKind = r.BlockKind,
                PageStart = r.PageStart,
                PageEnd = r.PageEnd,
                Text = r.Text,
                Score = r.Score
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RAG] Context enrichment failed for symbol={Symbol} query={Query}", symbol, query);
            return new();
        }
    }

    /// <summary>
    /// Format citations as context text for LLM prompt injection.
    /// </summary>
    public static string FormatAsContext(List<RagCitationDto> citations)
    {
        if (citations.Count == 0)
            return "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("\n--- 以下是相关财报原文摘录（仅供参考，请基于事实分析）---");
        foreach (var c in citations)
        {
            var source = $"[{c.ReportDate} {c.ReportType ?? ""} {c.Section ?? ""}]";
            sb.AppendLine($"\n{source}");
            sb.AppendLine(c.Text.Length > 500 ? c.Text[..500] + "..." : c.Text);
            if (c.PageStart.HasValue)
                sb.AppendLine($"(第{c.PageStart}页{(c.PageEnd.HasValue && c.PageEnd != c.PageStart ? $"-{c.PageEnd}页" : "")})");
        }
        sb.AppendLine("--- 财报摘录结束 ---\n");
        return sb.ToString();
    }

    // Internal DTO for deserializing Worker response
    private class RagSearchResponseInternal
    {
        [JsonPropertyName("query")]
        public string Query { get; set; } = "";
        [JsonPropertyName("totalResults")]
        public int TotalResults { get; set; }
        [JsonPropertyName("mode")]
        public string? Mode { get; set; }
        [JsonPropertyName("results")]
        public List<RagSearchResultInternal> Results { get; set; } = new();
    }

    private class RagSearchResultInternal
    {
        [JsonPropertyName("chunkId")]
        public string ChunkId { get; set; } = "";
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; } = "";
        [JsonPropertyName("reportDate")]
        public string ReportDate { get; set; } = "";
        [JsonPropertyName("reportType")]
        public string? ReportType { get; set; }
        [JsonPropertyName("section")]
        public string? Section { get; set; }
        [JsonPropertyName("blockKind")]
        public string BlockKind { get; set; } = "";
        [JsonPropertyName("pageStart")]
        public int? PageStart { get; set; }
        [JsonPropertyName("pageEnd")]
        public int? PageEnd { get; set; }
        [JsonPropertyName("text")]
        public string Text { get; set; } = "";
        [JsonPropertyName("score")]
        public double Score { get; set; }
    }
}
