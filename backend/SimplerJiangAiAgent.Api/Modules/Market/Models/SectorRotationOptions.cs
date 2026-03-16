namespace SimplerJiangAiAgent.Api.Modules.Market.Models;

public sealed class SectorRotationOptions
{
    public const string SectionName = "SectorRotation";

    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 120;
    public int BoardPageSize { get; set; } = 20;
    public int LeaderTake { get; set; } = 5;
    public int SectorMemberTake { get; set; } = 30;
    public int BreadthSampleSize { get; set; } = 4000;
    public int DetailNewsTake { get; set; } = 8;
}

public static class SectorBoardTypes
{
    public const string Industry = "industry";
    public const string Concept = "concept";
    public const string Style = "style";

    public static readonly string[] All = [Industry, Concept, Style];
}
