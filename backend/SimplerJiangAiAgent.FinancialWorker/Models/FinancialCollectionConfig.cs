using LiteDB;

namespace SimplerJiangAiAgent.FinancialWorker.Models;

/// <summary>采集配置（LiteDB 中仅存一条记录）</summary>
public class FinancialCollectionConfig
{
    [BsonId]
    public int Id { get; set; } = 1; // 始终为1，单例配置
    
    /// <summary>是否启用自动采集</summary>
    public bool Enabled { get; set; }
    
    /// <summary>采集范围：Watchlist / All</summary>
    public string Scope { get; set; } = "Watchlist";
    
    /// <summary>历史回溯起始日期</summary>
    public DateTime StartDate { get; set; } = DateTime.UtcNow.AddYears(-3);
    
    /// <summary>采集频率：Daily / Weekly / Manual</summary>
    public string Frequency { get; set; } = "Daily";
    
    /// <summary>自选股列表（当 Scope=Watchlist 时使用）</summary>
    public List<string> WatchlistSymbols { get; set; } = new();
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
