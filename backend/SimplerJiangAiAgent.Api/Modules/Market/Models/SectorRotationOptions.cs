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

    /// <summary>
    /// Normalize and validate boardType. Returns null for invalid values.
    /// Accepts aliases: "hy" → industry.
    /// </summary>
    public static string? TryNormalize(string? boardType)
    {
        if (string.IsNullOrWhiteSpace(boardType)) return Concept;
        return boardType.Trim().ToLowerInvariant() switch
        {
            "concept" => Concept,
            "industry" or "hy" => Industry,
            "style" => Style,
            _ => null
        };
    }
}
