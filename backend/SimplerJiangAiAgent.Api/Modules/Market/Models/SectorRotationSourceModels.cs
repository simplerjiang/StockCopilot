namespace SimplerJiangAiAgent.Api.Modules.Market.Models;

public sealed record EastmoneySectorBoardRow(
    string BoardType,
    string SectorCode,
    string SectorName,
    decimal ChangePercent,
    decimal MainNetInflow,
    decimal SuperLargeNetInflow,
    decimal LargeNetInflow,
    decimal MediumNetInflow,
    decimal SmallNetInflow,
    decimal TurnoverAmount,
    decimal TurnoverShare,
    int RankNo,
    string RawJson);

public sealed record EastmoneySectorLeaderRow(
    int RankInSector,
    string Symbol,
    string Name,
    decimal ChangePercent,
    decimal TurnoverAmount,
    bool IsLimitUp,
    bool IsBrokenBoard);

public sealed record EastmoneyMarketBreadthSnapshot(
    int Advancers,
    int Decliners,
    int FlatCount,
    decimal TotalTurnover);
