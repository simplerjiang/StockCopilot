namespace SimplerJiangAiAgent.Api.Data.Entities;

public class StockIndustryClassification
{
    public int Id { get; set; }
    public string StockCode { get; set; } = string.Empty;   // "SH600519"
    public string StockName { get; set; } = string.Empty;    // "贵州茅台"
    public string Industry { get; set; } = string.Empty;     // "酒、饮料和精制茶制造业"
    public string IndustryCode { get; set; } = string.Empty; // "C15"
    public string UpdateDate { get; set; } = string.Empty;   // "2026-04-28"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
