namespace SimplerJiangAiAgent.Api.Data.Entities;

public sealed class MacroMoneySupply
{
    public long Id { get; set; }
    public string Granularity { get; set; } = "month";
    public DateOnly Date { get; set; }
    public decimal? M0 { get; set; }
    public decimal? M0YoY { get; set; }
    public decimal? M0MoM { get; set; }
    public decimal? M1 { get; set; }
    public decimal? M1YoY { get; set; }
    public decimal? M1MoM { get; set; }
    public decimal? M2 { get; set; }
    public decimal? M2YoY { get; set; }
    public decimal? M2MoM { get; set; }
    public DateTime CreatedAt { get; set; }
}
