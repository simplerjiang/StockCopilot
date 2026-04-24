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
