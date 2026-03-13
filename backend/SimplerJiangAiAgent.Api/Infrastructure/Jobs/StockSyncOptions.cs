namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

public sealed class StockSyncOptions
{
    public const string SectionName = "StockSync";
    public const string DefaultAiProvider = "openai";
    public const string DefaultAiModel = "gemini-2.5-flash-lite";

    // 同步间隔（秒）
    public int IntervalSeconds { get; set; } = 60;

    // 默认同步的大盘指数
    public string MarketIndexSymbol { get; set; } = "sh000001";

    // 需要同步的股票列表
    public List<string> Symbols { get; set; } = new();

    // 本地事实 AI 清洗 provider
    public string AiProvider { get; set; } = DefaultAiProvider;

    // 本地事实 AI 清洗模型
    public string AiModel { get; set; } = DefaultAiModel;

    // 本地事实 AI 清洗批大小
    public int AiBatchSize { get; set; } = 12;
}
