namespace SimplerJiangAiAgent.Api.Modules.Market.Models;

public sealed record BatchStockQuoteDto(
    string Symbol,
    string Name,
    decimal Price,
    decimal Change,
    decimal ChangePercent,
    decimal High,
    decimal Low,
    decimal? TurnoverRate,
    decimal? PeRatio,
    decimal TurnoverAmount,
    decimal VolumeRatio,
    DateTime Timestamp);

public sealed record MarketCapitalFlowPointDto(
    DateTime Timestamp,
    decimal MainNetInflow,
    decimal SmallOrderNetInflow,
    decimal MediumOrderNetInflow,
    decimal LargeOrderNetInflow,
    decimal SuperLargeOrderNetInflow);

public sealed record MarketCapitalFlowSnapshotDto(
    DateTime SnapshotTime,
    DateOnly TradingDate,
    string AmountUnit,
    decimal MainNetInflow,
    decimal SmallOrderNetInflow,
    decimal MediumOrderNetInflow,
    decimal LargeOrderNetInflow,
    decimal SuperLargeOrderNetInflow,
    IReadOnlyList<MarketCapitalFlowPointDto> Points);

public sealed record NorthboundFlowPointDto(
    DateTime Timestamp,
    decimal ShanghaiNetInflow,
    decimal ShanghaiBalance,
    decimal ShenzhenNetInflow,
    decimal ShenzhenBalance,
    decimal TotalNetInflow);

public sealed record NorthboundFlowSnapshotDto(
    DateTime SnapshotTime,
    string TradingDateLabel,
    string AmountUnit,
    decimal ShanghaiNetInflow,
    decimal ShanghaiBalance,
    decimal ShenzhenNetInflow,
    decimal ShenzhenBalance,
    decimal TotalNetInflow,
    IReadOnlyList<NorthboundFlowPointDto> Points);

public sealed record MarketBreadthBucketDto(
    int ChangeBucket,
    string Label,
    int Count);

public sealed record MarketBreadthDistributionDto(
    DateOnly TradingDate,
    int Advancers,
    int Decliners,
    int FlatCount,
    int LimitUpCount,
    int LimitDownCount,
    IReadOnlyList<MarketBreadthBucketDto> Buckets);

public sealed record MarketRealtimeOverviewDto(
    DateTime SnapshotTime,
    IReadOnlyList<BatchStockQuoteDto> Indices,
    MarketCapitalFlowSnapshotDto? MainCapitalFlow,
    NorthboundFlowSnapshotDto? NorthboundFlow,
    MarketBreadthDistributionDto? Breadth,
    bool IsStale = false);