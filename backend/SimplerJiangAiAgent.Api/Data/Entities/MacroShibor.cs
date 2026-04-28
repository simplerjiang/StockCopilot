namespace SimplerJiangAiAgent.Api.Data.Entities;

public sealed class MacroShibor
{
    public long Id { get; set; }
    public DateOnly Date { get; set; }
    public decimal? Overnight { get; set; }
    public decimal? Week1 { get; set; }
    public decimal? Week2 { get; set; }
    public decimal? Month1 { get; set; }
    public decimal? Month3 { get; set; }
    public decimal? Month6 { get; set; }
    public decimal? Month9 { get; set; }
    public decimal? Year1 { get; set; }
    public DateTime CreatedAt { get; set; }
}
