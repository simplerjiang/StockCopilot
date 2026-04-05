using LiteDB;

namespace SimplerJiangAiAgent.FinancialWorker.Models;

/// <summary>
/// 单个报告期的财务报表数据（资产负债表/利润表/现金流量表三合一）
/// </summary>
public class FinancialReport
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    
    /// <summary>股票代码，如 "600519"</summary>
    public string Symbol { get; set; } = string.Empty;
    
    /// <summary>报告期，如 "2024-12-31"</summary>
    public string ReportDate { get; set; } = string.Empty;
    
    /// <summary>报告类型：Annual / Q1 / Q2 / Q3</summary>
    public string ReportType { get; set; } = string.Empty;
    
    /// <summary>公司类型：4=一般企业, 1=银行, 2=保险, 3=券商</summary>
    public int CompanyType { get; set; } = 4;
    
    /// <summary>资产负债表字段（键值对）</summary>
    public Dictionary<string, object?> BalanceSheet { get; set; } = new();
    
    /// <summary>利润表字段（键值对）</summary>
    public Dictionary<string, object?> IncomeStatement { get; set; } = new();
    
    /// <summary>现金流量表字段（键值对）</summary>
    public Dictionary<string, object?> CashFlow { get; set; } = new();
    
    /// <summary>数据来源通道：emweb / datacenter / ths / pdf</summary>
    public string SourceChannel { get; set; } = string.Empty;
    
    /// <summary>采集时间</summary>
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>最后更新时间</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
