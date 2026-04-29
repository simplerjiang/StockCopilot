namespace SimplerJiangAiAgent.Api.Data.Entities;

public sealed class MacroDepositRate
{
    public long Id { get; set; }
    public DateOnly Date { get; set; }
    public decimal? DemandDeposit { get; set; }
    public decimal? Fixed3M { get; set; }
    public decimal? Fixed6M { get; set; }
    public decimal? Fixed1Y { get; set; }
    public decimal? Fixed2Y { get; set; }
    public decimal? Fixed3Y { get; set; }
    public decimal? Fixed5Y { get; set; }
    public DateTime CreatedAt { get; set; }
}
