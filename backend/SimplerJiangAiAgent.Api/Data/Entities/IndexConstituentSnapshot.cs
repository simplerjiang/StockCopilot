namespace SimplerJiangAiAgent.Api.Data.Entities;

public class IndexConstituentSnapshot
{
    public int Id { get; set; }
    public string IndexCode { get; set; } = string.Empty;
    public string StockCode { get; set; } = string.Empty;
    public string StockName { get; set; } = string.Empty;
    public string UpdateDate { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
