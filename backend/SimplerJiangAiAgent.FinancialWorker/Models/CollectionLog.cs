using LiteDB;

namespace SimplerJiangAiAgent.FinancialWorker.Models;

/// <summary>采集日志</summary>
public class CollectionLog
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    
    public string Symbol { get; set; } = string.Empty;
    
    /// <summary>采集类型：FinancialReport / Indicator / Dividend / MarginTrading / Pdf</summary>
    public string CollectionType { get; set; } = string.Empty;
    
    /// <summary>使用的通道：emweb / datacenter / ths / cninfo</summary>
    public string Channel { get; set; } = string.Empty;
    
    /// <summary>是否降级</summary>
    public bool IsDegraded { get; set; }
    
    /// <summary>降级原因</summary>
    public string? DegradeReason { get; set; }
    
    /// <summary>是否成功</summary>
    public bool Success { get; set; }
    
    /// <summary>错误信息</summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>耗时(毫秒)</summary>
    public long DurationMs { get; set; }
    
    /// <summary>采集到的记录数</summary>
    public int RecordCount { get; set; }
    
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
