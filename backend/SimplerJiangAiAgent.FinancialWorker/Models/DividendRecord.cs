using LiteDB;

namespace SimplerJiangAiAgent.FinancialWorker.Models;

/// <summary>分红送配记录</summary>
public class DividendRecord
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    
    public string Symbol { get; set; } = string.Empty;
    
    /// <summary>分红方案，如 "10派20元"</summary>
    public string Plan { get; set; } = string.Empty;
    
    /// <summary>股权登记日</summary>
    public string RecordDate { get; set; } = string.Empty;
    
    /// <summary>除权除息日</summary>
    public string ExDividendDate { get; set; } = string.Empty;
    
    /// <summary>每股派息(元)</summary>
    public decimal? DividendPerShare { get; set; }
    
    /// <summary>每股送股</summary>
    public decimal? BonusSharePerShare { get; set; }
    
    /// <summary>每股转增</summary>
    public decimal? ConvertedSharePerShare { get; set; }
    
    /// <summary>原始数据</summary>
    public Dictionary<string, object?> RawData { get; set; } = new();
    
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}
