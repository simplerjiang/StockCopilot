using LiteDB;
using SimplerJiangAiAgent.Api.Infrastructure.Storage;
using SimplerJiangAiAgent.Api.Modules.Stocks.Contracts;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IFinancialDataReadService : IDisposable
{
    List<Dictionary<string, object?>> GetReports(string symbol, int limit = 20);
    List<Dictionary<string, object?>> GetIndicators(string symbol, int limit = 20);
    List<Dictionary<string, object?>> GetDividends(string symbol);
    List<Dictionary<string, object?>> GetMarginTrading(string symbol, int limit = 100);
    List<FinancialCollectionLogEntry> GetCollectionLogs(string? symbol = null, int limit = 50);
    Dictionary<string, object?>? GetConfig();
    FinancialReportSummary? GetReportSummary(string symbol, int periods = 4);
    FinancialTrendSummary? GetTrendSummary(string symbol, int periods = 8);
    PagedResult<FinancialReportListItem> ListReports(FinancialReportListQuery query);
    FinancialReportDetail? GetReportById(string id);
}

public class FinancialDataReadService : IFinancialDataReadService
{
    private readonly LiteDatabase? _db;
    private readonly ILogger<FinancialDataReadService> _logger;
    private readonly bool _available;

    public FinancialDataReadService(AppRuntimePaths runtimePaths, ILogger<FinancialDataReadService> logger, bool readOnly = true)
    {
        _logger = logger;
        var dbPath = Path.Combine(runtimePaths.AppDataPath, "financial-data.db");

        if (!File.Exists(dbPath))
        {
            _logger.LogWarning("Financial LiteDB not found at {Path}, returning empty data", dbPath);
            _available = false;
            return;
        }

        try
        {
            var connStr = readOnly
                ? $"Filename={dbPath};Connection=shared;ReadOnly=true"
                : $"Filename={dbPath};Connection=shared";
            _db = new LiteDatabase(connStr);
            _available = true;
            _logger.LogInformation("Financial LiteDB opened ({Mode}): {Path}", readOnly ? "read-only" : "read-write", dbPath);
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
        return FindBySymbol("financial_reports", symbol)
            .OrderByDescending(d => SafeString(d, "ReportDate"))
            .Take(limit)
            .Select(BsonToDict)
            .ToList();
    }

    public List<Dictionary<string, object?>> GetIndicators(string symbol, int limit = 20)
    {
        if (!_available) return [];
        return FindBySymbol("financial_indicators", symbol)
            .OrderByDescending(d => SafeString(d, "ReportDate"))
            .Take(limit)
            .Select(BsonToDict)
            .ToList();
    }

    public List<Dictionary<string, object?>> GetDividends(string symbol)
    {
        if (!_available) return [];
        return FindBySymbol("dividends", symbol)
            .OrderByDescending(d => SafeString(d, "RecordDate"))
            .Select(BsonToDict)
            .ToList();
    }

    public List<Dictionary<string, object?>> GetMarginTrading(string symbol, int limit = 100)
    {
        if (!_available) return [];
        return FindBySymbol("margin_trading", symbol)
            .OrderByDescending(d => SafeString(d, "TradeDate"))
            .Take(limit)
            .Select(BsonToDict)
            .ToList();
    }

    public List<FinancialCollectionLogEntry> GetCollectionLogs(string? symbol = null, int limit = 50)
    {
        if (!_available) return [];
        var query = string.IsNullOrWhiteSpace(symbol)
            ? _db!.GetCollection("collection_logs").FindAll()
            : FindBySymbol("collection_logs", symbol);
        return query
            .OrderByDescending(d => SafeDateTime(d, "Timestamp") ?? DateTime.MinValue)
            .Take(limit)
            .Select(MapCollectionLog)
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
        var docs = FindBySymbol("financial_reports", symbol)
            .OrderByDescending(d => SafeString(d, "ReportDate"))
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
        var docs = FindBySymbol("financial_reports", symbol)
            .OrderByDescending(d => SafeString(d, "ReportDate"))
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
        var divDocs = FindBySymbol("dividends", symbol)
            .OrderByDescending(d => SafeString(d, "RecordDate"))
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

    public PagedResult<FinancialReportListItem> ListReports(FinancialReportListQuery query)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, 100);

        if (!_available)
        {
            return new PagedResult<FinancialReportListItem>(Array.Empty<FinancialReportListItem>(), 0, page, pageSize);
        }

        var col = _db!.GetCollection("financial_reports");

        var parts = new List<string>();
        var pars = new BsonDocument();
        var idx = 0;

        var symbols = ParseCsv(query.Symbol);
        if (symbols.Count > 0)
        {
            pars[$"p{idx}"] = new BsonArray(symbols.Select(s => (BsonValue)s));
            parts.Add($"$.Symbol IN @p{idx}");
            idx++;
        }

        var reportTypes = ParseCsv(query.ReportType);
        if (reportTypes.Count > 0)
        {
            pars[$"p{idx}"] = new BsonArray(reportTypes.Select(s => (BsonValue)s));
            parts.Add($"$.ReportType IN @p{idx}");
            idx++;
        }

        if (!string.IsNullOrWhiteSpace(query.StartDate))
        {
            pars[$"p{idx}"] = query.StartDate;
            parts.Add($"$.ReportDate >= @p{idx}");
            idx++;
        }

        if (!string.IsNullOrWhiteSpace(query.EndDate))
        {
            pars[$"p{idx}"] = query.EndDate;
            parts.Add($"$.ReportDate <= @p{idx}");
            idx++;
        }

        BsonExpression? predicate = parts.Count == 0
            ? null
            : BsonExpression.Create(string.Join(" AND ", parts), pars);

        var (sortField, sortOrder) = ParseSort(query.Sort);

        var queryable = col.Query();
        if (predicate is not null)
        {
            queryable = queryable.Where(predicate);
        }

        var ordered = queryable.OrderBy(BsonExpression.Create(sortField), sortOrder);

        var total = predicate is null ? col.Count() : col.Count(predicate);
        var docs = ordered.Skip((page - 1) * pageSize).Limit(pageSize).ToList();

        var items = docs.Select(MapListItem).ToList();
        return new PagedResult<FinancialReportListItem>(items, total, page, pageSize);
    }

    public FinancialReportDetail? GetReportById(string id)
    {
        if (!_available || string.IsNullOrWhiteSpace(id)) return null;

        ObjectId objId;
        try
        {
            objId = new ObjectId(id.Trim());
        }
        catch
        {
            return null;
        }

        var col = _db!.GetCollection("financial_reports");
        var doc = col.FindById(objId);
        if (doc is null) return null;

        return new FinancialReportDetail(
            objId.ToString(),
            SafeString(doc, "Symbol"),
            SafeString(doc, "ReportDate"),
            SafeString(doc, "ReportType"),
            SafeInt32(doc, "CompanyType"),
            SafeNullableString(doc, "SourceChannel"),
            SafeDateTime(doc, "CollectedAt") ?? default,
            SafeDateTime(doc, "UpdatedAt") ?? default,
            BsonDocToDict(SafeDoc(doc, "BalanceSheet")),
            BsonDocToDict(SafeDoc(doc, "IncomeStatement")),
            BsonDocToDict(SafeDoc(doc, "CashFlow")));
    }

    private static FinancialReportListItem MapListItem(BsonDocument doc)
    {
        return new FinancialReportListItem(
            SafeId(doc) ?? string.Empty,
            SafeString(doc, "Symbol"),
            SafeString(doc, "ReportDate"),
            SafeString(doc, "ReportType"),
            SafeNullableString(doc, "SourceChannel"),
            SafeDateTime(doc, "CollectedAt") ?? default,
            SafeDateTime(doc, "UpdatedAt") ?? default);
    }

    private static Dictionary<string, object?> BsonDocToDict(BsonDocument doc)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var key in doc.Keys)
        {
            dict[key] = BsonToObject(doc[key]);
        }
        return dict;
    }

    private static List<string> ParseCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new List<string>();
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static (string Field, int Order) ParseSort(string? sort)
    {
        var raw = string.IsNullOrWhiteSpace(sort) ? "reportDate:desc" : sort.Trim();
        var parts = raw.Split(':', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var key = parts.Length > 0 ? parts[0].ToLowerInvariant() : "reportdate";
        var dir = parts.Length > 1 ? parts[1].ToLowerInvariant() : "desc";

        var field = key switch
        {
            "updatedat" => "$.UpdatedAt",
            "collectedat" => "$.CollectedAt",
            _ => "$.ReportDate",
        };
        var order = dir == "asc" ? Query.Ascending : Query.Descending;
        return (field, order);
    }

    public void Dispose()
    {
        _db?.Dispose();
    }

    // ── Helpers ──

    internal static IReadOnlyCollection<string> BuildSymbolAliases(string symbol)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return aliases;
        }

        var trimmed = symbol.Trim();
        aliases.Add(trimmed);

        var normalized = StockSymbolNormalizer.Normalize(trimmed);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            aliases.Add(normalized);
            aliases.Add(normalized.ToUpperInvariant());
        }

        if (normalized.Length == 8
            && (normalized.StartsWith("sh", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("sz", StringComparison.OrdinalIgnoreCase)))
        {
            aliases.Add(normalized[2..]);
        }

        if (trimmed.Length == 6 && trimmed.All(char.IsDigit))
        {
            var prefixed = StockSymbolNormalizer.Normalize(trimmed);
            aliases.Add(prefixed);
            aliases.Add(prefixed.ToUpperInvariant());
        }

        return aliases;
    }

    private IEnumerable<BsonDocument> FindBySymbol(string collectionName, string symbol)
    {
        var aliases = BuildSymbolAliases(symbol);
        return _db!.GetCollection(collectionName)
            .FindAll()
            .Where(doc => aliases.Contains(SafeString(doc, "Symbol")));
    }

    private static FinancialCollectionLogEntry MapCollectionLog(BsonDocument doc)
    {
        var timestamp = SafeDateTime(doc, "Timestamp");
        var fallbackId = string.Join(
            ':',
            new[]
            {
                SafeString(doc, "Symbol"),
                SafeString(doc, "CollectionType"),
                SafeString(doc, "Channel"),
                timestamp?.ToString("O") ?? string.Empty
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return new FinancialCollectionLogEntry
        {
            Id = SafeId(doc) ?? fallbackId,
            Symbol = SafeString(doc, "Symbol"),
            CollectionType = SafeString(doc, "CollectionType"),
            Channel = SafeString(doc, "Channel"),
            IsDegraded = SafeBoolean(doc, "IsDegraded"),
            DegradeReason = SafeNullableString(doc, "DegradeReason"),
            Success = SafeBoolean(doc, "Success"),
            ErrorMessage = SafeNullableString(doc, "ErrorMessage"),
            DurationMs = SafeInt64(doc, "DurationMs"),
            RecordCount = SafeInt32(doc, "RecordCount"),
            Timestamp = timestamp
        };
    }

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

    private static string SafeString(BsonDocument doc, string key)
    {
        if (doc.ContainsKey(key) && doc[key].IsString) return doc[key].AsString;
        return "";
    }

    private static string? SafeNullableString(BsonDocument doc, string key)
    {
        if (!doc.ContainsKey(key) || doc[key].IsNull) return null;
        if (doc[key].IsString) return doc[key].AsString;
        return BsonToObject(doc[key])?.ToString();
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

    private static bool SafeBoolean(BsonDocument doc, string key)
    {
        if (!doc.ContainsKey(key)) return false;
        var value = doc[key];
        if (value.IsBoolean) return value.AsBoolean;
        if (value.IsString && bool.TryParse(value.AsString, out var parsed)) return parsed;
        if (value.IsInt32) return value.AsInt32 != 0;
        if (value.IsInt64) return value.AsInt64 != 0;
        return false;
    }

    private static int SafeInt32(BsonDocument doc, string key)
    {
        if (!doc.ContainsKey(key)) return 0;
        var value = doc[key];
        if (value.IsInt32) return value.AsInt32;
        if (value.IsInt64) return (int)Math.Clamp(value.AsInt64, int.MinValue, int.MaxValue);
        if (value.IsDouble) return (int)Math.Round(value.AsDouble);
        if (value.IsDecimal) return decimal.ToInt32(decimal.Round(value.AsDecimal, 0, MidpointRounding.AwayFromZero));
        return 0;
    }

    private static long SafeInt64(BsonDocument doc, string key)
    {
        if (!doc.ContainsKey(key)) return 0;
        var value = doc[key];
        if (value.IsInt64) return value.AsInt64;
        if (value.IsInt32) return value.AsInt32;
        if (value.IsDouble) return (long)Math.Round(value.AsDouble);
        if (value.IsDecimal) return decimal.ToInt64(decimal.Round(value.AsDecimal, 0, MidpointRounding.AwayFromZero));
        return 0;
    }

    private static DateTime? SafeDateTime(BsonDocument doc, string key)
    {
        if (!doc.ContainsKey(key) || doc[key].IsNull) return null;
        var value = doc[key];
        if (value.IsDateTime) return value.AsDateTime;
        if (value.IsString && DateTime.TryParse(value.AsString, out var parsed)) return parsed;
        return null;
    }

    private static string? SafeId(BsonDocument doc)
    {
        if (!doc.TryGetValue("_id", out var idValue) || idValue.IsNull)
        {
            return null;
        }

        if (idValue.IsObjectId)
        {
            return idValue.AsObjectId.ToString();
        }

        return BsonToObject(idValue)?.ToString();
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

public sealed class FinancialCollectionLogEntry
{
    public string Id { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string CollectionType { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public bool IsDegraded { get; init; }
    public string? DegradeReason { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public long DurationMs { get; init; }
    public int RecordCount { get; init; }
    public DateTime? Timestamp { get; init; }
}

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
