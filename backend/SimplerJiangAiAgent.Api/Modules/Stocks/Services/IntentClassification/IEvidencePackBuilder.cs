namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services.IntentClassification;

public interface IEvidencePackBuilder
{
    Task<EvidencePack> BuildAsync(string symbol, string query, IntentType intent, CancellationToken ct = default);
    string FormatAsPromptContext(EvidencePack pack);
}
