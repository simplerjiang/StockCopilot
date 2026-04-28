namespace SimplerJiangAiAgent.Api.Data.Entities;

public sealed class MacroLoanRate
{
    public long Id { get; set; }
    public DateOnly Date { get; set; }
    public decimal? Loan6M { get; set; }
    public decimal? Loan6MTo1Y { get; set; }
    public decimal? Loan1YTo3Y { get; set; }
    public decimal? Loan3YTo5Y { get; set; }
    public decimal? Loan5YPlus { get; set; }
    public DateTime CreatedAt { get; set; }
}
