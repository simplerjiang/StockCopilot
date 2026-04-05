using LiteDB;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IFinancialDataReadService : IDisposable
{
    List<Dictionary<string, object?>> GetReports(string symbol, int limit = 20);
    List<Dictionary<string, object?>> GetIndicators(string symbol, int limit = 20);
    List<Dictionary<string, object?>> GetDividends(string symbol);
    List<Dictionary<string, object?>> GetMarginTrading(string symbol, int limit = 100);
    List<Dictionary<string, object?>> GetCollectionLogs(string? symbol = null, int limit = 50);
    Dictionary<string, object?>? GetConfig();
    FinancialReportSummary? GetReportSummary(string symbol, int periods = 4);
    FinancialTrendSummary? GetTrendSummary(string symbol, int periods = 8);
}

public class FinancialDataReadService : IFinancialDataReadService
{
    private readonly LiteDatabase? _db;
    private readonly ILogger<FinancialDataReadService> _logger;
    private readonly bool _available;

    public FinancialDataReadService(ILogger<FinancialDataReadService> logger)
    {
        _logger = logger;
        var repoRoot = FindRepoRoot();
        var dbPath = Path.Combine(repoRoot, "App_Data", "financial-data.db");

        if (!File.Exists(dbPath))
        {
            _logger.LogWarning("Financial LiteDB not found at {Path}, returning empty data", dbPath);
            _available = false;
            return;
        }

        try
        {
            _db = new LiteDatabase($"Filename={dbPath};Connection=shared;ReadOnly=true");
            _available = true;
            _logger.LogInformation("Financial LiteDB opened (read-only): {Path}", dbPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open Financial LiteDB at {Path}", dbPath);
            _available = false;
        }
    }

    public List<Dictionary<string, object?>> GetReports(string symbol, int limit = 20)
    {
        if (!_available) return [];
        return _db!.GetCollection("financial_reports")
            .Find(Query.EQ("Symbol", symbol))
            .OrderByDescending(d => d["ReportDate"].AsString)
            .Take(limit)
            .Select(BsonToDict)
            .ToList();
    }

    public List<Dictionary<string, object?>> GetIndicators(string symbol, int limit = 20)
    {
        if (!_available) return [];
        return _db!.GetCollection("financial_indicators")
            .Find(Query.EQ("Symbol", symbol))
            .OrderByDescending(d => d["ReportDate"].AsString)
            .Take(limit)
            .Select(BsonToDict)
            .ToList();
    }

    public List<Dictionary<string, object?>> GetDividends(string symbol)
    {
        if (!_available) return [];
        return _db!.GetCollection("dividends")
            .Find(Query.EQ("Symbol", symbol))
            .OrderByDescending(d => d["RecordDate"].AsString)
            .Select(BsonToDict)
            .ToList();
    }

    public List<Dictionary<string, object?>> GetMarginTrading(string symbol, int limit = 100)
    {
        if (!_available) return [];
        return _db!.GetCollection("margin_trading")
            .Find(Query.EQ("Symbol", symbol))
            .OrderByDescending(d => d["TradeDate"].AsString)
            .Take(limit)
            .Select(BsonToDict)
            .ToList();
    }

    public List<Dictionary<string, object?>> GetCollectionLogs(string? symbol = null, int limit = 50)
    {
        if (!_available) return [];
        var col = _db!.GetCollection("collection_logs");
        var query = symbol is not null
            ? col.Find(Query.EQ("Symbol", symbol))
            : col.FindAll();
        return query
            .OrderByDescending(d => d["Timestamp"].AsDateTime)
            .Take(limit)
            .Select(BsonToDict)
            .ToList();
    }

    public Dictionary<string, object?>? GetConfig()
    {
        if (!_available) return null;
        var doc = _db!.GetCollection("config").FindById(1);
        return doc is null ? null : BsonToDict(doc);
    }

    public FinancialReportSummary? GetReportSummary(string symbol, int periods = 4)
    {
        if (!_available) return null;
        var docs = _db!.GetCollection("financial_reports")
            .Find(Query.EQ("Symbol", symbol))
            .OrderByDescending(d => d["ReportDate"].AsString)
            .Take(periods)
            .ToList();

        if (docs.Count == 0) return null;

        var summary = new FinancialReportSummary { Symbol = symbol };

        foreach (var doc in docs)
        {
            var period = new PeriodReport
            {
                ReportDate = SafeString(doc, "ReportDate"),
                ReportType = SafeString(doc, "ReportType"),
                SourceChannel = SafeString(doc, "SourceChannel"),
            };

            // Extract key metrics from balance sheet, income statement, cash flow
            var bs = SafeDoc(doc, "BalanceSheet");
            var inc = SafeDoc(doc, "IncomeStatement");
            var cf = SafeDoc(doc, "CashFlow");

            // Eastmoney-style English field names
            TryAdd(period.KeyMetrics, "TotalAssets", bs, "TOTAL_ASSETS");
            TryAdd(period.KeyMetrics, "TotalLiabilities", bs, "TOTAL_LIABILITIES");
            TryAdd(period.KeyMetrics, "TotalEquity", bs, "TOTAL_EQUITY");
            TryAdd(period.KeyMetrics, "CurrentAssets", bs, "TOTAL_CURRENT_ASSETS");
            TryAdd(period.KeyMetrics, "CurrentLiabilities", bs, "TOTAL_CURRENT_LIAB");
            TryAdd(period.KeyMetrics, "TotalRevenue", inc, "TOTAL_OPERATE_INCOME");
            TryAdd(period.KeyMetrics, "Revenue", inc, "OPERATE_INCOME");
            TryAdd(period.KeyMetrics, "NetProfit", inc, "NETPROFIT");
            TryAdd(period.KeyMetrics, "GrossProfit", inc, "OPERATE_INCOME_SUBTRACT_COST");
            TryAdd(period.KeyMetrics, "OperatingProfit", inc, "OPERATE_PROFIT");
            TryAdd(period.KeyMetrics, "TotalCost", inc, "TOTAL_OPERATE_COST");
            TryAdd(period.KeyMetrics, "OperatingCashFlow", cf, "NETCASH_OPERATE");
            TryAdd(period.KeyMetrics, "InvestingCashFlow", cf, "NETCASH_INVEST");
            TryAdd(period.KeyMetrics, "FinancingCashFlow", cf, "NETCASH_FINANCE");
            TryAdd(period.KeyMetrics, "NetCashFlow", cf, "CCE_ADD");

            // Tonghuashun-style Chinese field names fallback
            TryAddChinese(period.KeyMetrics, "TotalAssets", bs, "总资产");
            TryAddChinese(period.KeyMetrics, "TotalLiabilities", bs, "总负债");
            TryAddChinese(period.KeyMetrics, "TotalEquity", bs, "股东权益合计");
            TryAddChinese(period.KeyMetrics, "CurrentAssets", bs, "流动资产合计");
            TryAddChinese(period.KeyMetrics, "CurrentLiabilities", bs, "流动负债合计");
            TryAddChinese(period.KeyMetrics, "TotalRevenue", inc, "营业总收入");
            TryAddChinese(period.KeyMetrics, "Revenue", inc, "营业收入");
            TryAddChinese(period.KeyMetrics, "NetProfit", inc, "净利润");
            TryAddChinese(period.KeyMetrics, "GrossProfit", inc, "毛利润");
            TryAddChinese(period.KeyMetrics, "TotalCost", inc, "营业总成本");
            TryAddChinese(period.KeyMetrics, "NetCashFlow", cf, "现金及现金等价物净增加额");

            // Computed: debt-to-asset ratio
            if (!period.KeyMetrics.ContainsKey("DebtToAssetRatio")
                && period.KeyMetrics.TryGetValue("TotalAssets", out var taObj)
                && period.KeyMetrics.TryGetValue("TotalLiabilities", out var tlObj)
                && taObj is double taD && tlObj is double tlD && taD > 0)
            {
                period.KeyMetrics["DebtToAssetRatio"] = Math.Round(tlD / taD, 4);
            }

            summary.Periods.Add(period);
        }

        return summary;
    }

    public FinancialTrendSummary? GetTrendSummary(string symbol, int periods = 8)
    {
        if (!_available) return null;
        var docs = _db!.GetCollection("financial_reports")
            .Find(Query.EQ("Symbol", symbol))
            .OrderByDescending(d => d["ReportDate"].AsString)
            .Take(periods)
            .Reverse()
            .ToList();

        if (docs.Count == 0) return null;

        var trend = new FinancialTrendSummary { Symbol = symbol };

        double? prevRevenue = null, prevProfit = null, prevAssets = null;

        foreach (var doc in docs)
        {
            var periodLabel = SafeString(doc, "ReportDate");
            var inc = SafeDoc(doc, "IncomeStatement");
            var bs = SafeDoc(doc, "BalanceSheet");

            var revenue = SafeDouble(inc, "TOTAL_OPERATE_INCOME") ?? SafeDouble(inc, "营业总收入");
            var netProfit = SafeDouble(inc, "NETPROFIT") ?? SafeDouble(inc, "净利润");
            var totalAssets = SafeDouble(bs, "TOTAL_ASSETS") ?? SafeDouble(bs, "总资产");

            trend.Revenue.Add(new TrendPoint
            {
                Period = periodLabel,
                Value = revenue,
                YoY = CalcYoY(revenue, prevRevenue)
            });
            trend.NetProfit.Add(new TrendPoint
            {
                Period = periodLabel,
                Value = netProfit,
                YoY = CalcYoY(netProfit, prevProfit)
            });
            trend.TotalAssets.Add(new TrendPoint
            {
                Period = periodLabel,
                Value = totalAssets,
                YoY = CalcYoY(totalAssets, prevAssets)
            });

            prevRevenue = revenue;
            prevProfit = netProfit;
            prevAssets = totalAssets;
        }

        // Recent dividends
        var divDocs = _db!.GetCollection("dividends")
            .Find(Query.EQ("Symbol", symbol))
            .OrderByDescending(d => d["RecordDate"].AsString)
            .Take(5)
            .ToList();

        foreach (var d in divDocs)
        {
            trend.RecentDividends.Add(new DividendInfo
            {
                Plan = SafeString(d, "Plan"),
                DividendPerShare = SafeDecimal(d, "DividendPerShare")
            });
        }

        return trend;
    }

    public void Dispose()
    {
        _db?.Dispose();
    }

    // ── Helpers ──

    private static Dictionary<string, object?> BsonToDict(BsonDocument doc)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var key in doc.Keys)
        {
            if (key == "_id") continue; // skip internal LiteDB id
            dict[key] = BsonToObject(doc[key]);
        }
        return dict;
    }

    private static object? BsonToObject(BsonValue val)
    {
        if (val.IsNull) return null;
        if (val.IsString) return val.AsString;
        if (val.IsInt32) return val.AsInt32;
        if (val.IsInt64) return val.AsInt64;
        if (val.IsDouble) return val.AsDouble;
        if (val.IsDecimal) return val.AsDecimal;
        if (val.IsBoolean) return val.AsBoolean;
        if (val.IsDateTime) return val.AsDateTime;
        if (val.IsDocument)
        {
            var d = new Dictionary<string, object?>();
            foreach (var k in val.AsDocument.Keys)
                d[k] = BsonToObject(val.AsDocument[k]);
            return d;
        }
        if (val.IsArray)
            return val.AsArray.Select(BsonToObject).ToList();
        return val.ToString();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("SimplerJiangAiAgent.sln").Length > 0 ||
                Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    private static string SafeString(BsonDocument doc, string key)
    {
        if (doc.ContainsKey(key) && doc[key].IsString) return doc[key].AsString;
        return "";
    }

    private static BsonDocument SafeDoc(BsonDocument doc, string key)
    {
        if (doc.ContainsKey(key) && doc[key].IsDocument) return doc[key].AsDocument;
        return new BsonDocument();
    }

    private static double? SafeDouble(BsonDocument doc, string key)
    {
        if (!doc.ContainsKey(key)) return null;
        var v = doc[key];
        if (v.IsDouble) return v.AsDouble;
        if (v.IsInt32) return v.AsInt32;
        if (v.IsInt64) return v.AsInt64;
        if (v.IsDecimal) return (double)v.AsDecimal;
        return null;
    }

    private static decimal? SafeDecimal(BsonDocument doc, string key)
    {
        if (!doc.ContainsKey(key)) return null;
        var v = doc[key];
        if (v.IsDecimal) return v.AsDecimal;
        if (v.IsDouble) return (decimal)v.AsDouble;
        if (v.IsInt32) return v.AsInt32;
        if (v.IsInt64) return v.AsInt64;
        return null;
    }

    private static void TryAdd(Dictionary<string, object?> metrics, string outKey, BsonDocument source, string sourceKey)
    {
        if (metrics.ContainsKey(outKey)) return;
        var val = SafeDouble(source, sourceKey);
        if (val.HasValue) metrics[outKey] = val.Value;
    }

    private static void TryAddChinese(Dictionary<string, object?> metrics, string outKey, BsonDocument source, string partialKey)
    {
        if (metrics.ContainsKey(outKey)) return;
        // Search for keys containing the Chinese text (handles suffixes like "(万元)")
        foreach (var key in source.Keys)
        {
            if (key.Contains(partialKey))
            {
                var val = SafeDouble(source, key);
                if (val.HasValue)
                {
                    metrics[outKey] = val.Value;
                    return;
                }
            }
        }
    }

    private static double? CalcYoY(double? current, double? previous)
    {
        if (!current.HasValue || !previous.HasValue || previous.Value == 0) return null;
        return Math.Round((current.Value - previous.Value) / Math.Abs(previous.Value) * 100, 2);
    }
}

// ── DTOs for MCP structured summaries ──

public class FinancialReportSummary
{
    public string Symbol { get; set; } = "";
    public List<PeriodReport> Periods { get; set; } = new();
}

public class PeriodReport
{
    public string ReportDate { get; set; } = "";
    public string ReportType { get; set; } = "";
    public string SourceChannel { get; set; } = "";
    public Dictionary<string, object?> KeyMetrics { get; set; } = new();
}

public class FinancialTrendSummary
{
    public string Symbol { get; set; } = "";
    public List<TrendPoint> Revenue { get; set; } = new();
    public List<TrendPoint> NetProfit { get; set; } = new();
    public List<TrendPoint> TotalAssets { get; set; } = new();
    public List<DividendInfo> RecentDividends { get; set; } = new();
}

public class TrendPoint
{
    public string Period { get; set; } = "";
    public double? Value { get; set; }
    public double? YoY { get; set; }
}

public class DividendInfo
{
    public string Plan { get; set; } = "";
    public decimal? DividendPerShare { get; set; }
}
