using LiteDB;

namespace SimplerJiangAiAgent.FinancialWorker.Models;

/// <summary>
/// 主要财务指标（每股收益、ROE、毛利率等）
/// </summary>
public class FinancialIndicator
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    
    public string Symbol { get; set; } = string.Empty;
    
    /// <summary>报告期</summary>
    public string ReportDate { get; set; } = string.Empty;
    
    /// <summary>指标集合（如 EPS, ROE, 毛利率, 资产负债率 等）</summary>
    public Dictionary<string, object?> Metrics { get; set; } = new();
    
    public string SourceChannel { get; set; } = string.Empty;
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}
