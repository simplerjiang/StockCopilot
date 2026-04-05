using LiteDB;

namespace SimplerJiangAiAgent.FinancialWorker.Models;

/// <summary>融资融券每日记录</summary>
public class MarginTradingRecord
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    
    public string Symbol { get; set; } = string.Empty;
    
    /// <summary>交易日期</summary>
    public string TradeDate { get; set; } = string.Empty;
    
    /// <summary>融资余额(元)</summary>
    public decimal? MarginBalance { get; set; }
    
    /// <summary>融资买入额(元)</summary>
    public decimal? MarginBuy { get; set; }
    
    /// <summary>融资偿还额(元)</summary>
    public decimal? MarginRepay { get; set; }
    
    /// <summary>融券余量(股)</summary>
    public decimal? ShortSellingVolume { get; set; }
    
    /// <summary>融券卖出量(股)</summary>
    public decimal? ShortSellingSell { get; set; }
    
    /// <summary>融券偿还量(股)</summary>
    public decimal? ShortSellingRepay { get; set; }
    
    /// <summary>融资融券余额(元)</summary>
    public decimal? TotalBalance { get; set; }
    
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}
