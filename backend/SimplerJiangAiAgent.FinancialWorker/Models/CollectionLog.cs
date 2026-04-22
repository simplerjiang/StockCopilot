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

    /// <summary>本次落库覆盖到的报告期，如 ["2025-12-31","2025-09-30"]，按时间倒序</summary>
    public List<string> ReportPeriods { get; set; } = new();

    /// <summary>报告期对应的标题，最多 3 条，便于前端展示</summary>
    public List<string> ReportTitles { get; set; } = new();

    /// <summary>主来源通道（与 Channel 同义，便于前端语义清晰）</summary>
    public string? MainSourceChannel { get; set; }

    /// <summary>曾尝试但失败/跳过的通道，按尝试顺序</summary>
    public List<string> FallbackChannels { get; set; } = new();

    /// <summary>PDF 补充摘要，如 "pdf:2_tables_appended"，未触发为 null</summary>
    public string? PdfSummarySupplement { get; set; }

    /// <summary>非致命警告</summary>
    public List<string> Warnings { get; set; } = new();

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
