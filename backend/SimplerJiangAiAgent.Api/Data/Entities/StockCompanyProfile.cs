namespace SimplerJiangAiAgent.Api.Data.Entities;

public sealed class StockCompanyProfile
{
    public long Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? SectorName { get; set; }
    public int? ShareholderCount { get; set; }
    public string? FundamentalFactsJson { get; set; }
    public DateTime? FundamentalUpdatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}