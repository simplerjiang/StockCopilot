namespace SimplerJiangAiAgent.Api.Data.Entities;

public sealed class StockDividendRecord
{
    public long Id { get; set; }
    public string StockCode { get; set; } = string.Empty; // e.g. "sh600519"
    public string StockName { get; set; } = string.Empty;
    public DateOnly? PreNoticeDate { get; set; }
    public decimal? DividendPerShare { get; set; }
    public decimal? DividendPerShareAfterTax { get; set; }
    public string? StockDividendPerShare { get; set; }
    public DateOnly? RecordDate { get; set; }
    public DateOnly? ExDividendDate { get; set; }
    public DateOnly? LastTradeDate { get; set; }
    public DateOnly? ListedDate { get; set; }
    public DateTime CreatedAt { get; set; }
}
