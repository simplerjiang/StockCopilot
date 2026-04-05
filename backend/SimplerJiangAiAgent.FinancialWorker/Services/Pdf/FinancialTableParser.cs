using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace SimplerJiangAiAgent.FinancialWorker.Services.Pdf;

/// <summary>
/// 从 PDF 提取文本中解析财务三主表
/// </summary>
public class FinancialTableParser
{
    private readonly ILogger<FinancialTableParser> _logger;

    public FinancialTableParser(ILogger<FinancialTableParser> logger) => _logger = logger;

    /// <summary>
    /// 从提取结果中解析三主表
    /// </summary>
    public ParsedFinancialStatements Parse(PdfExtractionResult extraction)
    {
        var result = new ParsedFinancialStatements();
        var text = extraction.FullText;

        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("PDF 文本为空，无法解析");
            return result;
        }

        // 1. 定位并解析资产负债表
        result.BalanceSheet = ExtractStatement(text, BalanceSheetMarkers, BalanceSheetFieldMap, "资产负债表");

        // 2. 定位并解析利润表
        result.IncomeStatement = ExtractStatement(text, IncomeStatementMarkers, IncomeStatementFieldMap, "利润表");

        // 3. 定位并解析现金流量表
        result.CashFlowStatement = ExtractStatement(text, CashFlowStatementMarkers, CashFlowStatementFieldMap, "现金流量表");

        // 4. 提取报告期信息
        result.ReportDate = ExtractReportDate(text);
        result.ReportType = InferReportType(result.ReportDate);

        // 5. 验证资产负债表平衡
        ValidateBalance(result.BalanceSheet);

        _logger.LogInformation("PDF 解析完成: 资产负债表 {B} 项, 利润表 {I} 项, 现金流量表 {C} 项",
            result.BalanceSheet.Count, result.IncomeStatement.Count, result.CashFlowStatement.Count);

        return result;
    }

    private Dictionary<string, object?> ExtractStatement(
        string fullText,
        string[] sectionMarkers,
        Dictionary<string, string> fieldMap,
        string statementName)
    {
        var data = new Dictionary<string, object?>();

        // 找到报表起始位置
        var startIdx = -1;
        foreach (var marker in sectionMarkers)
        {
            startIdx = fullText.IndexOf(marker, StringComparison.Ordinal);
            if (startIdx >= 0) break;
        }

        if (startIdx < 0)
        {
            _logger.LogDebug("{Statement} 未找到", statementName);
            return data;
        }

        // 报表区域：从标记位置到下一个报表标记或结尾（最多取 15000 字符）
        var endIdx = FindNextSectionEnd(fullText, startIdx + 10, 15000);
        var section = fullText.Substring(startIdx, endIdx - startIdx);

        // 逐行扫描，匹配科目名称和金额
        var lines = section.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            foreach (var (cnName, stdField) in fieldMap)
            {
                if (!trimmed.Contains(cnName)) continue;

                // 提取该行中的数字
                var numbers = ExtractNumbers(trimmed, cnName);
                if (numbers.Count > 0)
                {
                    // 第一个数字通常是本期金额
                    data.TryAdd(stdField, numbers[0]);
                }
                break; // 一行只匹配一个科目
            }
        }

        return data;
    }

    /// <summary>
    /// 从一行文本中提取数值（处理逗号分隔、括号负数、单位）
    /// </summary>
    private static List<double> ExtractNumbers(string line, string skipKeyword)
    {
        var results = new List<double>();

        // 移除科目名称部分，只看数字区域
        var idx = line.IndexOf(skipKeyword, StringComparison.Ordinal);
        var numPart = idx >= 0 ? line[(idx + skipKeyword.Length)..] : line;

        // 匹配数字: 带逗号的数字、带小数点的数字、括号负数
        // Examples: 1,234,567.89  (1,234,567.89)  -123456.78  123456
        var pattern = @"[\(\（]?\s*-?\s*[\d,]+\.?\d*\s*[\)\）]?";
        var matches = Regex.Matches(numPart, pattern);

        foreach (Match m in matches)
        {
            var val = m.Value.Trim();
            if (string.IsNullOrWhiteSpace(val)) continue;

            var isNeg = val.Contains('(') || val.Contains('（');
            val = val.Replace("(", "").Replace(")", "").Replace("（", "").Replace("）", "")
                     .Replace(",", "").Replace(" ", "").Trim();

            if (double.TryParse(val, out var num))
            {
                if (isNeg) num = -num;
                results.Add(num);
            }
        }

        return results;
    }

    /// <summary>
    /// 提取报告年度/期间
    /// </summary>
    private static string? ExtractReportDate(string text)
    {
        // 常见格式: "2024年12月31日" 或 "2024-12-31" 或 "20241231"
        var match = Regex.Match(text, @"(\d{4})\s*年\s*(\d{1,2})\s*月\s*(\d{1,2})\s*日");
        if (match.Success)
        {
            return $"{match.Groups[1].Value}-{match.Groups[2].Value.PadLeft(2, '0')}-{match.Groups[3].Value.PadLeft(2, '0')}";
        }

        match = Regex.Match(text, @"(20\d{2})[-/](\d{2})[-/](\d{2})");
        if (match.Success)
        {
            return $"{match.Groups[1].Value}-{match.Groups[2].Value}-{match.Groups[3].Value}";
        }

        return null;
    }

    private static string InferReportType(string? reportDate)
    {
        if (string.IsNullOrEmpty(reportDate)) return "Unknown";
        if (reportDate.EndsWith("-12-31")) return "Annual";
        if (reportDate.EndsWith("-06-30")) return "Q2"; // 半年报
        if (reportDate.EndsWith("-03-31")) return "Q1";
        if (reportDate.EndsWith("-09-30")) return "Q3";
        return "Unknown";
    }

    private int FindNextSectionEnd(string text, int startAfter, int maxLen)
    {
        var searchEnd = Math.Min(text.Length, startAfter + maxLen);
        var nextSection = searchEnd;

        string[] endMarkers = ["合并资产负债表", "合并利润表", "合并现金流量表",
            "资产负债表", "利润表", "现金流量表", "所有者权益变动表",
            "财务报表附注", "注释", "审计报告"];

        foreach (var marker in endMarkers)
        {
            var idx = text.IndexOf(marker, startAfter, StringComparison.Ordinal);
            if (idx > startAfter && idx < nextSection)
                nextSection = idx;
        }

        return nextSection;
    }

    private void ValidateBalance(Dictionary<string, object?> balanceSheet)
    {
        if (balanceSheet.TryGetValue("TotalAssets", out var taObj) && taObj is double totalAssets &&
            balanceSheet.TryGetValue("TotalLiabilities", out var tlObj) && tlObj is double totalLiabilities &&
            balanceSheet.TryGetValue("TotalEquity", out var teObj) && teObj is double totalEquity)
        {
            var sum = totalLiabilities + totalEquity;
            var diff = Math.Abs(totalAssets - sum);
            var tolerance = Math.Abs(totalAssets) * 0.0001; // 0.01% tolerance

            if (diff > tolerance && tolerance > 0)
            {
                _logger.LogWarning("资产负债表不平衡: 总资产={TotalAssets:N2}, 负债+权益={Sum:N2}, 差额={Diff:N2}",
                    totalAssets, sum, diff);
            }
        }
    }

    // ==================== 科目映射表 ====================

    private static readonly string[] BalanceSheetMarkers =
    [
        "合并资产负债表", "资产负债表", "BALANCE SHEET"
    ];

    private static readonly string[] IncomeStatementMarkers =
    [
        "合并利润表", "利润表", "损益表", "INCOME STATEMENT", "PROFIT AND LOSS"
    ];

    private static readonly string[] CashFlowStatementMarkers =
    [
        "合并现金流量表", "现金流量表", "CASH FLOW STATEMENT"
    ];

    /// <summary>
    /// 资产负债表科目映射: 中文名 → 标准字段名  
    /// </summary>
    private static readonly Dictionary<string, string> BalanceSheetFieldMap = new()
    {
        ["货币资金"] = "CashAndEquivalents",
        ["交易性金融资产"] = "TradingFinancialAssets",
        ["应收票据"] = "NotesReceivable",
        ["应收账款"] = "AccountsReceivable",
        ["预付款项"] = "Prepayments",
        ["存货"] = "Inventory",
        ["流动资产合计"] = "CurrentAssets",
        ["固定资产"] = "FixedAssets",
        ["无形资产"] = "IntangibleAssets",
        ["商誉"] = "Goodwill",
        ["长期股权投资"] = "LongTermEquityInvestments",
        ["非流动资产合计"] = "NonCurrentAssets",
        ["资产总计"] = "TotalAssets",
        ["短期借款"] = "ShortTermBorrowings",
        ["应付票据"] = "NotesPayable",
        ["应付账款"] = "AccountsPayable",
        ["预收款项"] = "AdvanceReceipts",
        ["合同负债"] = "ContractLiabilities",
        ["流动负债合计"] = "CurrentLiabilities",
        ["长期借款"] = "LongTermBorrowings",
        ["应付债券"] = "BondsPayable",
        ["非流动负债合计"] = "NonCurrentLiabilities",
        ["负债合计"] = "TotalLiabilities",
        ["实收资本"] = "PaidInCapital",
        ["股本"] = "ShareCapital",
        ["资本公积"] = "CapitalReserve",
        ["盈余公积"] = "SurplusReserve",
        ["未分配利润"] = "RetainedEarnings",
        ["归属于母公司所有者权益合计"] = "EquityAttributableToParent",
        ["少数股东权益"] = "MinorityInterest",
        ["所有者权益合计"] = "TotalEquity",
        ["负债和所有者权益总计"] = "TotalLiabilitiesAndEquity"
    };

    /// <summary>
    /// 利润表科目映射
    /// </summary>
    private static readonly Dictionary<string, string> IncomeStatementFieldMap = new()
    {
        ["营业总收入"] = "TotalRevenue",
        ["营业收入"] = "Revenue",
        ["营业总成本"] = "TotalCost",
        ["营业成本"] = "CostOfRevenue",
        ["税金及附加"] = "TaxesAndSurcharges",
        ["销售费用"] = "SellingExpenses",
        ["管理费用"] = "AdministrativeExpenses",
        ["研发费用"] = "RAndDExpenses",
        ["财务费用"] = "FinancialExpenses",
        ["投资收益"] = "InvestmentIncome",
        ["公允价值变动收益"] = "FairValueChangeIncome",
        ["营业利润"] = "OperatingProfit",
        ["营业外收入"] = "NonOperatingIncome",
        ["营业外支出"] = "NonOperatingExpenses",
        ["利润总额"] = "ProfitBeforeTax",
        ["所得税费用"] = "IncomeTaxExpense",
        ["净利润"] = "NetProfit",
        ["归属于母公司所有者的净利润"] = "NetProfitAttributableToParent",
        ["少数股东损益"] = "MinorityInterestProfit",
        ["基本每股收益"] = "BasicEPS",
        ["稀释每股收益"] = "DilutedEPS"
    };

    /// <summary>
    /// 现金流量表科目映射
    /// </summary>
    private static readonly Dictionary<string, string> CashFlowStatementFieldMap = new()
    {
        ["销售商品、提供劳务收到的现金"] = "CashFromSales",
        ["收到的税费返还"] = "TaxRefunds",
        ["收到其他与经营活动有关的现金"] = "OtherOperatingCashIn",
        ["经营活动现金流入小计"] = "OperatingCashInflow",
        ["购买商品、接受劳务支付的现金"] = "CashPaidForGoods",
        ["支付给职工以及为职工支付的现金"] = "CashPaidToEmployees",
        ["支付的各项税费"] = "TaxesPaid",
        ["经营活动现金流出小计"] = "OperatingCashOutflow",
        ["经营活动产生的现金流量净额"] = "OperatingCashFlow",
        ["收回投资收到的现金"] = "CashFromInvestmentRecovery",
        ["取得投资收益收到的现金"] = "CashFromInvestmentIncome",
        ["处置固定资产收到的现金"] = "CashFromDisposalOfAssets",
        ["投资活动现金流入小计"] = "InvestingCashInflow",
        ["购建固定资产支付的现金"] = "CashForFixedAssets",
        ["投资支付的现金"] = "CashForInvestments",
        ["投资活动现金流出小计"] = "InvestingCashOutflow",
        ["投资活动产生的现金流量净额"] = "InvestingCashFlow",
        ["吸收投资收到的现金"] = "CashFromEquityFinancing",
        ["取得借款收到的现金"] = "CashFromBorrowings",
        ["偿还债务支付的现金"] = "CashForDebtRepayment",
        ["分配股利、利润或偿付利息支付的现金"] = "CashForDividendsAndInterest",
        ["筹资活动现金流入小计"] = "FinancingCashInflow",
        ["筹资活动现金流出小计"] = "FinancingCashOutflow",
        ["筹资活动产生的现金流量净额"] = "FinancingCashFlow",
        ["现金及现金等价物净增加额"] = "NetCashFlow",
        ["期末现金及现金等价物余额"] = "EndingCash"
    };
}

/// <summary>
/// PDF 解析后的三主表结构化数据
/// </summary>
public class ParsedFinancialStatements
{
    public Dictionary<string, object?> BalanceSheet { get; set; } = new();
    public Dictionary<string, object?> IncomeStatement { get; set; } = new();
    public Dictionary<string, object?> CashFlowStatement { get; set; } = new();
    public string? ReportDate { get; set; }
    public string ReportType { get; set; } = "Unknown";

    public bool HasData => BalanceSheet.Count > 0 || IncomeStatement.Count > 0 || CashFlowStatement.Count > 0;
}
