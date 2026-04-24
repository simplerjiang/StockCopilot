using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services.IntentClassification;

public record EvidencePack(
    string Symbol,
    string Query,
    IntentType Intent,
    IReadOnlyList<RagCitationDto> RagChunks,
    IReadOnlyList<FinancialMetricSummary> FinancialMetrics,
    LocalFactSummary? LocalFacts,
    IReadOnlyList<string> DegradedSources
)
{
    public bool HasRagEvidence => RagChunks.Count > 0;
    public bool HasFinancialMetrics => FinancialMetrics.Count > 0;
    public bool HasLocalFacts => LocalFacts is not null && (LocalFacts.StockNewsCount + LocalFacts.SectorReportCount + LocalFacts.MarketReportCount) > 0;
}

public record FinancialMetricSummary(
    string ReportDate,
    string? ReportType,
    string? SourceChannel,
    IReadOnlyDictionary<string, object?> KeyMetrics
);

public record LocalFactSummary(
    int StockNewsCount,
    int SectorReportCount,
    int MarketReportCount,
    IReadOnlyList<string> TopHeadlines
);
