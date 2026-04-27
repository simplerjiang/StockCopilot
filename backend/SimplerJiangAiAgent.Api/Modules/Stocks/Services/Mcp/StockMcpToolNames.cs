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
    public const string WebSearch = "WebSearchMcp";
    public const string WebSearchNews = "WebSearchNewsMcp";
    public const string WebReadUrl = "WebReadUrlMcp";
    public const string FinancialReport = "FinancialReportMcp";
    public const string FinancialTrend = "FinancialTrendMcp";
    public const string FinancialReportRag = "FinancialReportRag";
    public const string AnnouncementRag = "AnnouncementRag";

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
        Search,
        WebSearch,
        WebSearchNews,
        WebReadUrl,
        FinancialReport,
        FinancialTrend,
        FinancialReportRag,
        AnnouncementRag
    ];
}