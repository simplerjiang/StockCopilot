namespace SimplerJiangAiAgent.Api.Data.Entities;

public sealed class SectorRotationLeaderSnapshot
{
    public long Id { get; set; }
    public long SectorRotationSnapshotId { get; set; }
    public int RankInSector { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal ChangePercent { get; set; }
    public decimal TurnoverAmount { get; set; }
    public bool IsLimitUp { get; set; }
    public bool IsBrokenBoard { get; set; }
    public DateTime CreatedAt { get; set; }
    public SectorRotationSnapshot? SectorRotationSnapshot { get; set; }
}
