using Microsoft.Extensions.Logging;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services.IntentClassification;

public class QuestionIntentClassifier : IQuestionIntentClassifier
{
    private readonly ILlmService _llmService;
    private readonly ILogger<QuestionIntentClassifier> _logger;

    public QuestionIntentClassifier(ILlmService llmService, ILogger<QuestionIntentClassifier> logger)
    {
        _llmService = llmService;
        _logger = logger;
    }

    public async Task<QuestionIntent> ClassifyAsync(string question, string? stockSymbol = null, CancellationToken ct = default)
    {
        var ruleResult = ClassifyByRules(question);
        if (ruleResult is not null && ruleResult.Confidence >= 0.8)
        {
            _logger.LogDebug("Intent classified by rules: {Type} (confidence={Confidence})", ruleResult.Type, ruleResult.Confidence);
            return ruleResult;
        }

        try
        {
            var llmResult = await ClassifyByLlmAsync(question, stockSymbol, ct);
            if (llmResult is not null)
            {
                _logger.LogDebug("Intent classified by LLM: {Type} (confidence={Confidence})", llmResult.Type, llmResult.Confidence);
                return llmResult;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM intent classification failed, falling back to rule result or General");
        }

        return ruleResult ?? new QuestionIntent(IntentType.General, 0.5, false, false, SuggestedPipeline.LiveGate, "Fallback");
    }

    internal QuestionIntent? ClassifyByRules(string question)
    {
        var q = question.Trim();

        if (ContainsAny(q, "估值", "值多少", "贵不贵", "高估", "低估", "PE", "PB", "市盈率", "市净率", "目标价", "合理价格", "值不值"))
            return IntentRoutingTable.Resolve(IntentType.Valuation, 0.9);

        if (ContainsAny(q, "风险", "暴雷", "爆雷", "退市", "ST", "商誉减值", "坏账", "诉讼", "违规", "质押", "担保"))
            return IntentRoutingTable.Resolve(IntentType.Risk, 0.9);

        if (ContainsAny(q, "财报", "年报", "季报", "半年报", "营收", "净利润", "毛利率", "净利率", "ROE", "营业收入",
            "总资产", "负债", "现金流", "每股收益", "EPS", "利润表", "资产负债", "经营情况"))
            return IntentRoutingTable.Resolve(IntentType.FinancialAnalysis, 0.9);

        if (ContainsAny(q, "为什么涨", "为什么跌", "涨停", "跌停", "业绩变化", "增长原因", "下滑原因", "归因", "驱动因素"))
            return IntentRoutingTable.Resolve(IntentType.PerformanceAttribution, 0.85);

        if (ContainsAny(q, "K线", "k线", "均线", "MACD", "KDJ", "RSI", "趋势", "支撑位", "压力位", "买卖点",
            "技术面", "量价", "形态", "突破", "回调"))
            return IntentRoutingTable.Resolve(IntentType.TechnicalAnalysis, 0.9);

        if (ContainsAny(q, "大盘", "板块", "轮动", "行情", "市场", "指数", "上证", "深证", "创业板", "北向资金", "两市"))
            return IntentRoutingTable.Resolve(IntentType.MarketOverview, 0.85);

        if (ContainsAny(q, "推荐", "选股", "好股", "买什么", "配置", "标的", "看好哪", "有什么股"))
            return IntentRoutingTable.Resolve(IntentType.StockPicking, 0.9);

        // No keyword matched — short questions are too vague to classify
        if (q.Length < 4)
            return new QuestionIntent(IntentType.Clarification, 0.9, false, false, SuggestedPipeline.DirectReply, "问题过短");

        return new QuestionIntent(IntentType.General, 0.4, false, false, SuggestedPipeline.LiveGate, "NoRuleMatch");
    }

    private async Task<QuestionIntent?> ClassifyByLlmAsync(string question, string? stockSymbol, CancellationToken ct)
    {
        var stockContext = stockSymbol is not null ? $"（当前股票：{stockSymbol}）" : "";
        var prompt = $$"""
你是一个股票问答意图分类器。根据用户问题判断意图类型。{{stockContext}}

可选意图类型：
- Valuation: 估值相关（目标价、贵不贵、PE/PB）
- Risk: 风险相关（暴雷、退市、减值、质押）
- FinancialAnalysis: 财报分析（营收、利润、财务指标）
- PerformanceAttribution: 业绩归因（为什么涨跌、增长原因）
- TechnicalAnalysis: 技术面（K线、均线、指标、趋势）
- MarketOverview: 大盘/板块（市场行情、指数、板块轮动）
- StockPicking: 选股推荐（买什么、推荐标的）
- General: 其他通用问题
- Clarification: 问题模糊需追问

用户问题：{{question}}

仅回复 JSON，不要其他内容：
{"type":"IntentType","confidence":0.0-1.0,"reason":"简短理由"}
""";

        var request = new LlmChatRequest(prompt, null, 0.1, false, ResponseFormat: LlmResponseFormats.Json, MaxOutputTokens: 150);
        var result = await _llmService.ChatAsync("active", request, ct);

        var response = result.Content;
        if (string.IsNullOrWhiteSpace(response)) return null;

        try
        {
            var json = response;
            var jsonStart = json.IndexOf('{');
            var jsonEnd = json.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
                json = json.Substring(jsonStart, jsonEnd - jsonStart + 1);

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var typeStr = root.GetProperty("type").GetString();
            var confidence = root.GetProperty("confidence").GetDouble();
            var reason = root.TryGetProperty("reason", out var r) ? r.GetString() : null;

            if (Enum.TryParse<IntentType>(typeStr, true, out var intentType))
            {
                return IntentRoutingTable.Resolve(intentType, confidence, reason);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM intent response: {Response}", response);
        }

        return null;
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        foreach (var kw in keywords)
        {
            if (text.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
