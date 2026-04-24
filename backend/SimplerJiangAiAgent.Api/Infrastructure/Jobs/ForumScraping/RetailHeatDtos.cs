namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs.ForumScraping;

public sealed record RetailHeatTimeSeriesDto(
    string Symbol,
    IReadOnlyList<RetailHeatDataPointDto> Data,
    RetailHeatDataPointDto? Latest,
    string Description
);

public sealed record RetailHeatDataPointDto(
    string Date,
    int DailyCount,
    double Ma20,
    double HeatRatio,
    string Signal,
    int PlatformCount,
    int PostCount,
    bool HasData
);

public sealed record RetailHeatCollectionStatusDto(
    string Symbol,
    bool InWatchlist,
    IReadOnlyList<PlatformCollectionStatusDto> Platforms,
    IReadOnlyList<string> MissingDates,
    int TotalTradingDays,
    int DaysWithData,
    double CoveragePercent
);

public sealed record PlatformCollectionStatusDto(
    string Platform,
    string? LastDate,
    int LastPostCount,
    int TotalRecords,
    string Status  // "ok" | "stale" | "none"
);

public sealed record SingleStockCollectResult(
    string Platform,
    bool Success,
    int? PostCount,
    string? Error
);
