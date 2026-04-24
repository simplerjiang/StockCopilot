using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services.IntentClassification;

public class EvidencePackBuilder : IEvidencePackBuilder
{
    private readonly RagContextEnricher _ragEnricher;
    private readonly IStockCopilotMcpService _mcpService;
    private readonly IQueryLocalFactDatabaseTool _localFactTool;
    private readonly ILogger<EvidencePackBuilder> _logger;

    public EvidencePackBuilder(
        RagContextEnricher ragEnricher,
        IStockCopilotMcpService mcpService,
        IQueryLocalFactDatabaseTool localFactTool,
        ILogger<EvidencePackBuilder> logger)
    {
        _ragEnricher = ragEnricher;
        _mcpService = mcpService;
        _localFactTool = localFactTool;
        _logger = logger;
    }

    public async Task<EvidencePack> BuildAsync(string symbol, string query, IntentType intent, CancellationToken ct = default)
    {
        var routing = IntentRoutingTable.GetRule(intent);
        var degradedSources = new List<string>();

        var ragTask = routing.RequiresRag
            ? SafeExecuteRagAsync(query, symbol, ct, degradedSources)
            : Task.FromResult<List<RagCitationDto>>(new());

        var financialTask = routing.RequiresFinancialData
            ? SafeExecuteFinancialAsync(symbol, ct, degradedSources)
            : Task.FromResult<List<FinancialMetricSummary>>(new());

        var localFactTask = SafeExecuteLocalFactAsync(symbol, ct, degradedSources);

        await Task.WhenAll(ragTask, financialTask, localFactTask);

        return new EvidencePack(
            symbol,
            query,
            intent,
            await ragTask,
            await financialTask,
            await localFactTask,
            degradedSources
        );
    }

    public string FormatAsPromptContext(EvidencePack pack)
    {
        var sb = new System.Text.StringBuilder();

        if (pack.HasRagEvidence)
        {
            sb.AppendLine(RagContextEnricher.FormatAsContext(pack.RagChunks.ToList()));
        }

        if (pack.HasFinancialMetrics)
        {
            sb.AppendLine("\n--- 结构化财务指标 ---");
            foreach (var m in pack.FinancialMetrics.Take(4))
            {
                sb.AppendLine($"\n[{m.ReportDate} {m.ReportType ?? ""} ({m.SourceChannel ?? ""})]");
                foreach (var kv in m.KeyMetrics.Where(kv => kv.Value is not null).Take(15))
                    sb.AppendLine($"  {kv.Key}: {kv.Value}");
            }
            sb.AppendLine("--- 财务指标结束 ---\n");
        }

        if (pack.HasLocalFacts)
        {
            sb.AppendLine("\n--- 近期新闻摘要 ---");
            foreach (var headline in pack.LocalFacts!.TopHeadlines.Take(5))
                sb.AppendLine($"  • {headline}");
            sb.AppendLine("--- 新闻摘要结束 ---\n");
        }

        if (pack.DegradedSources.Count > 0)
        {
            sb.AppendLine($"[注意：以下证据源不可用: {string.Join(", ", pack.DegradedSources)}]");
        }

        return sb.ToString();
    }

    private async Task<List<RagCitationDto>> SafeExecuteRagAsync(
        string query, string symbol, CancellationToken ct, List<string> degraded)
    {
        try
        {
            return await _ragEnricher.EnrichAsync(query, symbol, 5, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EvidencePack] RAG failed");
            degraded.Add("RAG");
            return new();
        }
    }

    private async Task<List<FinancialMetricSummary>> SafeExecuteFinancialAsync(
        string symbol, CancellationToken ct, List<string> degraded)
    {
        try
        {
            var envelope = await _mcpService.GetFinancialReportAsync(symbol, 4, null, ct);
            if (envelope?.Data?.Periods == null || envelope.Data.Periods.Count == 0)
            {
                degraded.Add("FinancialReport");
                return new();
            }
            return envelope.Data.Periods.Select(p => new FinancialMetricSummary(
                p.ReportDate, p.ReportType, p.SourceChannel, p.KeyMetrics
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EvidencePack] FinancialReport failed for {Symbol}", symbol);
            degraded.Add("FinancialReport");
            return new();
        }
    }

    private async Task<LocalFactSummary?> SafeExecuteLocalFactAsync(
        string symbol, CancellationToken ct, List<string> degraded)
    {
        try
        {
            var facts = await _localFactTool.QueryAsync(symbol, ct);
            if (facts == null) return null;

            var headlines = facts.StockNews
                .OrderByDescending(n => n.PublishTime)
                .Take(5)
                .Select(n => $"[{n.PublishTime:MM-dd}] {n.TranslatedTitle ?? n.Title}")
                .ToList();

            return new LocalFactSummary(
                facts.StockNews.Count,
                facts.SectorReports.Count,
                facts.MarketReports.Count,
                headlines
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EvidencePack] LocalFact failed for {Symbol}", symbol);
            degraded.Add("LocalFact");
            return null;
        }
    }
}
