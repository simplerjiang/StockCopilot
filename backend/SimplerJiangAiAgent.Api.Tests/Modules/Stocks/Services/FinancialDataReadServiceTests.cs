using LiteDB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SimplerJiangAiAgent.Api.Infrastructure.Storage;
using SimplerJiangAiAgent.Api.Modules.Stocks.Contracts;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;
using SimplerJiangAiAgent.FinancialWorker.Models;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class FinancialDataReadServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly AppRuntimePaths _runtimePaths;
    private readonly string _dbPath;

    public FinancialDataReadServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "financial-read-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:DataRootPath"] = _tempRoot,
            })
            .Build();
        _runtimePaths = new AppRuntimePaths(new FakeHostEnvironment(_tempRoot), config);
        _runtimePaths.EnsureWritableDirectories();
        _dbPath = Path.Combine(_runtimePaths.AppDataPath, "financial-data.db");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // best effort cleanup
        }
    }

    private void SeedReports(IEnumerable<FinancialReport> reports)
    {
        // Create empty file so service detects existence; rewrite via LiteDB
        using var db = new LiteDatabase($"Filename={_dbPath};Connection=direct");
        var col = db.GetCollection<FinancialReport>("financial_reports");
        col.InsertBulk(reports);
    }

    private FinancialDataReadService CreateService()
    {
        // readOnly=true is fine for queries; data was already seeded
        return new FinancialDataReadService(_runtimePaths, NullLogger<FinancialDataReadService>.Instance, readOnly: true);
    }

    private static FinancialReport MakeReport(
        string symbol,
        string reportDate,
        string reportType = "Annual",
        string sourceChannel = "emweb",
        DateTime? collectedAt = null,
        DateTime? updatedAt = null)
    {
        var now = DateTime.UtcNow;
        return new FinancialReport
        {
            Id = ObjectId.NewObjectId(),
            Symbol = symbol,
            ReportDate = reportDate,
            ReportType = reportType,
            CompanyType = 4,
            SourceChannel = sourceChannel,
            BalanceSheet = new Dictionary<string, object?> { ["TOTAL_ASSETS"] = 1000.0 },
            IncomeStatement = new Dictionary<string, object?> { ["NETPROFIT"] = 100.0 },
            CashFlow = new Dictionary<string, object?> { ["NETCASH_OPERATE"] = 50.0 },
            CollectedAt = collectedAt ?? now,
            UpdatedAt = updatedAt ?? now,
        };
    }

    [Fact]
    public void ListReports_EmptyDb_ReturnsEmpty()
    {
        // Create empty database file so the service opens it
        using (var db = new LiteDatabase($"Filename={_dbPath};Connection=direct"))
        {
            db.GetCollection<FinancialReport>("financial_reports");
        }

        using var svc = CreateService();
        var result = svc.ListReports(new FinancialReportListQuery(null, null, null, null));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
    }

    [Fact]
    public void ListReports_SinglePage_ReturnsAll()
    {
        SeedReports(new[]
        {
            MakeReport("600519", "2024-12-31"),
            MakeReport("600519", "2024-09-30", "Q3"),
            MakeReport("000001", "2024-12-31"),
        });

        using var svc = CreateService();
        var result = svc.ListReports(new FinancialReportListQuery(null, null, null, null));

        Assert.Equal(3, result.Total);
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public void ListReports_Pagination_RespectsPageSize()
    {
        var reports = Enumerable.Range(1, 25)
            .Select(i => MakeReport("600519", $"2024-{i:D2}-01"))
            .ToList();
        SeedReports(reports);

        using var svc = CreateService();
        var page1 = svc.ListReports(new FinancialReportListQuery(null, null, null, null, Page: 1, PageSize: 10));
        var page2 = svc.ListReports(new FinancialReportListQuery(null, null, null, null, Page: 2, PageSize: 10));
        var page3 = svc.ListReports(new FinancialReportListQuery(null, null, null, null, Page: 3, PageSize: 10));

        Assert.Equal(25, page1.Total);
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(10, page2.Items.Count);
        Assert.Equal(5, page3.Items.Count);

        // Pages don't overlap
        var ids = page1.Items.Select(i => i.Id)
            .Concat(page2.Items.Select(i => i.Id))
            .Concat(page3.Items.Select(i => i.Id))
            .ToList();
        Assert.Equal(25, ids.Distinct().Count());
    }

    [Fact]
    public void ListReports_FilterBySymbol_ReturnsMatching()
    {
        SeedReports(new[]
        {
            MakeReport("600519", "2024-12-31"),
            MakeReport("600519", "2024-09-30"),
            MakeReport("000001", "2024-12-31"),
            MakeReport("000002", "2024-12-31"),
        });

        using var svc = CreateService();

        var single = svc.ListReports(new FinancialReportListQuery("600519", null, null, null));
        Assert.Equal(2, single.Total);
        Assert.All(single.Items, item => Assert.Equal("600519", item.Symbol));

        var multi = svc.ListReports(new FinancialReportListQuery("600519,000001", null, null, null));
        Assert.Equal(3, multi.Total);
        Assert.All(multi.Items, item => Assert.Contains(item.Symbol, new[] { "600519", "000001" }));
    }

    [Fact]
    public void ListReports_FilterByReportType_ReturnsMatching()
    {
        SeedReports(new[]
        {
            MakeReport("600519", "2024-12-31", "Annual"),
            MakeReport("600519", "2024-09-30", "Q3"),
            MakeReport("600519", "2024-06-30", "Q2"),
            MakeReport("600519", "2024-03-31", "Q1"),
        });

        using var svc = CreateService();

        var annual = svc.ListReports(new FinancialReportListQuery(null, "Annual", null, null));
        Assert.Equal(1, annual.Total);

        var quarters = svc.ListReports(new FinancialReportListQuery(null, "Q1,Q3", null, null));
        Assert.Equal(2, quarters.Total);
        Assert.All(quarters.Items, item => Assert.Contains(item.ReportType, new[] { "Q1", "Q3" }));
    }

    [Fact]
    public void ListReports_DateRange_ReturnsInRange()
    {
        SeedReports(new[]
        {
            MakeReport("600519", "2023-12-31"),
            MakeReport("600519", "2024-03-31"),
            MakeReport("600519", "2024-06-30"),
            MakeReport("600519", "2024-12-31"),
            MakeReport("600519", "2025-03-31"),
        });

        using var svc = CreateService();
        var result = svc.ListReports(new FinancialReportListQuery(null, null, "2024-01-01", "2024-12-31"));

        Assert.Equal(3, result.Total);
        Assert.All(result.Items, item =>
        {
            Assert.True(string.CompareOrdinal(item.ReportDate, "2024-01-01") >= 0);
            Assert.True(string.CompareOrdinal(item.ReportDate, "2024-12-31") <= 0);
        });
    }

    [Fact]
    public void ListReports_SortByReportDateAsc_OrdersCorrectly()
    {
        SeedReports(new[]
        {
            MakeReport("600519", "2024-12-31"),
            MakeReport("600519", "2024-03-31"),
            MakeReport("600519", "2024-09-30"),
            MakeReport("600519", "2024-06-30"),
        });

        using var svc = CreateService();
        var result = svc.ListReports(new FinancialReportListQuery(null, null, null, null, Sort: "reportDate:asc"));

        var dates = result.Items.Select(i => i.ReportDate).ToList();
        var sorted = dates.OrderBy(d => d, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, dates);
    }

    [Fact]
    public void GetReportById_Existing_ReturnsDetail()
    {
        var report = MakeReport("600519", "2024-12-31");
        SeedReports(new[] { report });

        using var svc = CreateService();
        var detail = svc.GetReportById(report.Id.ToString());

        Assert.NotNull(detail);
        Assert.Equal("600519", detail!.Symbol);
        Assert.Equal("2024-12-31", detail.ReportDate);
        Assert.Equal(4, detail.CompanyType);
        Assert.True(detail.BalanceSheet.ContainsKey("TOTAL_ASSETS"));
        Assert.True(detail.IncomeStatement.ContainsKey("NETPROFIT"));
        Assert.True(detail.CashFlow.ContainsKey("NETCASH_OPERATE"));
    }

    [Fact]
    public void GetReportById_InvalidId_ReturnsNull()
    {
        SeedReports(new[] { MakeReport("600519", "2024-12-31") });

        using var svc = CreateService();
        var detail = svc.GetReportById("not-a-valid-objectid");

        Assert.Null(detail);
    }

    [Fact]
    public void GetReportById_NotFound_ReturnsNull()
    {
        SeedReports(new[] { MakeReport("600519", "2024-12-31") });

        using var svc = CreateService();
        // A different valid ObjectId that wasn't inserted
        var unusedId = ObjectId.NewObjectId().ToString();
        var detail = svc.GetReportById(unusedId);

        Assert.Null(detail);
    }

    [Fact]
    public void ListReports_PageSizeClampedTo100()
    {
        SeedReports(new[] { MakeReport("600519", "2024-12-31") });

        using var svc = CreateService();
        var result = svc.ListReports(new FinancialReportListQuery(null, null, null, null, Page: 1, PageSize: 999));

        Assert.Equal(100, result.PageSize);
    }

    [Fact]
    public void ListReports_NegativePageClampedTo1()
    {
        SeedReports(new[]
        {
            MakeReport("600519", "2024-12-31"),
            MakeReport("600519", "2024-09-30"),
        });

        using var svc = CreateService();
        var result = svc.ListReports(new FinancialReportListQuery(null, null, null, null, Page: -5, PageSize: 10));

        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public void ListReports_WhenCollectedAtMissing_ReturnsNull()
    {
        // Insert a raw BsonDocument that omits CollectedAt / UpdatedAt entirely
        using (var db = new LiteDatabase($"Filename={_dbPath};Connection=direct"))
        {
            var col = db.GetCollection("financial_reports");
            var doc = new BsonDocument
            {
                ["_id"] = ObjectId.NewObjectId(),
                ["Symbol"] = "600519",
                ["ReportDate"] = "2024-12-31",
                ["ReportType"] = "Annual",
                ["CompanyType"] = 4,
                ["SourceChannel"] = "emweb",
                ["BalanceSheet"] = new BsonDocument(),
                ["IncomeStatement"] = new BsonDocument(),
                ["CashFlow"] = new BsonDocument(),
            };
            col.Insert(doc);
        }

        using var svc = CreateService();
        var result = svc.ListReports(new FinancialReportListQuery(null, null, null, null));

        Assert.Single(result.Items);
        Assert.Null(result.Items[0].CollectedAt);
        Assert.Null(result.Items[0].UpdatedAt);
    }

    // ─── Bug #35 regression：THS 通道三表金额应从"万元"归一化到"元" ───

    // 茅台 2025 年报（THS）：*资产合计=30383500 万元 ≈ 3.038 万亿元；
    // *营业总收入=17205400 万元 ≈ 1720 亿元；*净利润=8531000 万元 ≈ 853 亿元。
    [Fact]
    public void GetReportById_ThsChannel_ScalesMonetaryFieldsFromWanYuanToYuan()
    {
        var id = ObjectId.NewObjectId();
        using (var db = new LiteDatabase($"Filename={_dbPath};Connection=direct"))
        {
            var col = db.GetCollection("financial_reports");
            var doc = new BsonDocument
            {
                ["_id"] = id,
                ["Symbol"] = "600519",
                ["ReportDate"] = "2025-12-31",
                ["ReportType"] = "Annual",
                ["CompanyType"] = 4,
                ["SourceChannel"] = "ths",
                ["BalanceSheet"] = new BsonDocument
                {
                    ["*资产合计"] = 30383500.0,
                    ["*负债合计"] = 4987600.0,
                    ["*所有者权益（或股东权益）合计"] = 25395900.0,
                    ["*资产合计_同比"] = 1.63584304,
                    ["货币资金"] = 5169100.0,
                    ["资产负债率"] = 16.42,
                },
                ["IncomeStatement"] = new BsonDocument
                {
                    ["*营业总收入"] = 17205400.0,
                    ["*净利润"] = 8531000.0,
                    ["基本每股收益"] = 65.66,
                    ["*净利润_同比"] = -4.50485863,
                },
                ["CashFlow"] = new BsonDocument
                {
                    ["*经营活动产生的现金流量净额"] = 6152200.0,
                    ["*投资活动产生的现金流量净额"] = -3164200.0,
                    ["*筹资活动产生的现金流量净额"] = -7342700.0,
                },
                ["CollectedAt"] = DateTime.UtcNow,
                ["UpdatedAt"] = DateTime.UtcNow,
            };
            col.Insert(doc);
        }

        using var svc = CreateService();
        var detail = svc.GetReportById(id.ToString());

        Assert.NotNull(detail);

        // 核心断言：营业收入归一化后必须落在 1000 亿 ~ 10 万亿 之间（1e11 ~ 1e13 元）
        Assert.True(detail!.IncomeStatement.TryGetValue("*营业总收入", out var revenueObj));
        var revenue = Convert.ToDouble(revenueObj);
        Assert.InRange(revenue, 1e11, 1e13);
        Assert.Equal(17205400.0 * 10000.0, revenue, precision: 2);

        // 总资产应 >= 1000 亿元，<= 10 万亿元
        var totalAssets = Convert.ToDouble(detail.BalanceSheet["*资产合计"]);
        Assert.InRange(totalAssets, 1e11, 1e13);
        Assert.Equal(30383500.0 * 10000.0, totalAssets, precision: 2);

        // 净利润
        var netProfit = Convert.ToDouble(detail.IncomeStatement["*净利润"]);
        Assert.InRange(netProfit, 1e10, 1e12);
        Assert.Equal(8531000.0 * 10000.0, netProfit, precision: 2);

        // 经营现金流净额
        var ocf = Convert.ToDouble(detail.CashFlow["*经营活动产生的现金流量净额"]);
        Assert.Equal(6152200.0 * 10000.0, ocf, precision: 2);

        // 非金额字段保持原值：YoY/百分比/每股 不应被放大
        Assert.Equal(1.63584304, Convert.ToDouble(detail.BalanceSheet["*资产合计_同比"]), precision: 6);
        Assert.Equal(-4.50485863, Convert.ToDouble(detail.IncomeStatement["*净利润_同比"]), precision: 6);
        Assert.Equal(65.66, Convert.ToDouble(detail.IncomeStatement["基本每股收益"]), precision: 4);
        Assert.Equal(16.42, Convert.ToDouble(detail.BalanceSheet["资产负债率"]), precision: 4);
    }

    [Fact]
    public void GetReportById_NonThsChannel_DoesNotScaleValues()
    {
        // emweb / pdf 通道已存储为元，绝不应该再 ×10000
        var id = ObjectId.NewObjectId();
        using (var db = new LiteDatabase($"Filename={_dbPath};Connection=direct"))
        {
            var col = db.GetCollection("financial_reports");
            var doc = new BsonDocument
            {
                ["_id"] = id,
                ["Symbol"] = "600519",
                ["ReportDate"] = "2024-12-31",
                ["ReportType"] = "Annual",
                ["CompanyType"] = 4,
                ["SourceChannel"] = "emweb",
                ["BalanceSheet"] = new BsonDocument { ["TOTAL_ASSETS"] = 280_000_000_000.0 },
                ["IncomeStatement"] = new BsonDocument { ["TOTAL_OPERATE_INCOME"] = 170_000_000_000.0 },
                ["CashFlow"] = new BsonDocument(),
                ["CollectedAt"] = DateTime.UtcNow,
                ["UpdatedAt"] = DateTime.UtcNow,
            };
            col.Insert(doc);
        }

        using var svc = CreateService();
        var detail = svc.GetReportById(id.ToString());

        Assert.NotNull(detail);
        Assert.Equal(280_000_000_000.0, Convert.ToDouble(detail!.BalanceSheet["TOTAL_ASSETS"]), precision: 2);
        Assert.Equal(170_000_000_000.0, Convert.ToDouble(detail.IncomeStatement["TOTAL_OPERATE_INCOME"]), precision: 2);
    }

    [Fact]
    public void NormalizeThsRawDictToYuan_SkipsRateAndYoyAndPerShare()
    {
        var dict = new Dictionary<string, object?>
        {
            ["*资产合计"] = 30383500.0,
            ["*资产合计_同比"] = 1.6358,
            ["基本每股收益"] = 65.66,
            ["毛利率"] = 91.5,
            ["ROE%"] = 25.3,
        };

        FinancialDataReadService.NormalizeThsRawDictToYuan(dict);

        Assert.Equal(3.03835e11, (double)dict["*资产合计"]!, precision: 2);
        Assert.Equal(1.6358, (double)dict["*资产合计_同比"]!, precision: 4);
        Assert.Equal(65.66, (double)dict["基本每股收益"]!, precision: 4);
        Assert.Equal(91.5, (double)dict["毛利率"]!, precision: 4);
        Assert.Equal(25.3, (double)dict["ROE%"]!, precision: 4);
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string contentRoot)
        {
            ContentRootPath = contentRoot;
        }

        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "SimplerJiangAiAgent.Api.Tests";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
