namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public static class StockMcpToolNames
{
    public const string CompanyOverview = "CompanyOverviewMcp";
    public const string Product = "StockProductMcp";
    public const string Fundamentals = "StockFundamentalsMcp";
    public const string Shareholder = "StockShareholderMcp";
    public const string MarketContext = "MarketContextMcp";
    public const string SocialSentiment = "SocialSentimentMcp";
    public const string Kline = "StockKlineMcp";
    public const string Minute = "StockMinuteMcp";
    public const string Strategy = "StockStrategyMcp";
    public const string News = "StockNewsMcp";
    public const string Search = "StockSearchMcp";

    public static readonly IReadOnlyList<string> All =
    [
        CompanyOverview,
        Product,
        Fundamentals,
        Shareholder,
        MarketContext,
        SocialSentiment,
        Kline,
        Minute,
        Strategy,
        News,
        Search
    ];
}