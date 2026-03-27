using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class EastmoneyCompanyProfileParserTests
{
    [Fact]
    public void Parse_ShouldExtractNameSectorAndShareholderCount()
    {
        const string surveyJson = """
        {
          "jbzl": {
            "agjc": "浦发银行",
            "sshy": "银行"
          }
        }
        """;

        const string shareholderJson = """
        {
          "gdrs": [
            {
              "HOLDER_TOTAL_NUM": 119099
            }
          ]
        }
        """;

        var profile = EastmoneyCompanyProfileParser.Parse("sh600000", surveyJson, shareholderJson);

        Assert.Equal("sh600000", profile.Symbol);
        Assert.Equal("浦发银行", profile.Name);
        Assert.Equal("银行", profile.SectorName);
        Assert.Equal(119099, profile.ShareholderCount);
        Assert.Contains(profile.Facts, item => item.Label == "所属行业" && item.Value == "银行");
        Assert.Contains(profile.Facts, item => item.Label == "股东户数" && item.Value == "119099");
    }

    [Fact]
    public void Parse_ShouldRetainProductBusinessFactsFromCompanySurvey()
    {
        const string surveyJson = """
        {
          "jbzl": {
            "agjc": "比亚迪",
            "jyfw": "新能源汽车及动力电池研发、生产、销售",
            "sshy": "汽车整车",
            "sszjhhy": "汽车制造业",
            "qy": "广东"
          }
        }
        """;

        var profile = EastmoneyCompanyProfileParser.Parse("sz002594", surveyJson);

        Assert.Contains(profile.Facts, item => item.Label == "经营范围" && item.Value.Contains("新能源汽车", StringComparison.Ordinal));
        Assert.Contains(profile.Facts, item => item.Label == "所属行业" && item.Value == "汽车整车");
        Assert.Contains(profile.Facts, item => item.Label == "证监会行业" && item.Value == "汽车制造业");
        Assert.Contains(profile.Facts, item => item.Label == "所属地区" && item.Value == "广东");
    }

    [Fact]
    public void ParseFinanceFacts_ShouldPickLatestReportByReportPeriod()
    {
        const string financeJson = """
        {
          "data": [
            {
              "REPORT_DATE_NAME": "2025三季报",
              "REPORT_DATE": "2025-09-30",
              "TOTALOPERATEREVE": 123000000000,
              "PARENTNETPROFIT": 9900000000
            },
            {
              "REPORT_DATE_NAME": "2025年报",
              "REPORT_DATE": "2025-12-31",
              "TOTALOPERATEREVE": 168000000000,
              "PARENTNETPROFIT": 12800000000
            }
          ]
        }
        """;

        var facts = EastmoneyCompanyProfileParser.ParseFinanceFacts(financeJson);

        Assert.Contains(facts, item => item.Label == "最新财报期" && item.Value == "2025年报");
        Assert.Contains(facts, item => item.Label == "营业收入" && item.Value == "1680亿元");
        Assert.Contains(facts, item => item.Label == "归属净利润" && item.Value == "128亿元");
    }

    [Fact]
    public void Parse_WhenZyywEmptyButJyfwPresent_ShouldDeriveMainBusinessFromScope()
    {
        const string surveyJson = """
        {
          "jbzl": {
            "agjc": "贵州茅台",
            "zyyw": "",
            "jyfw": "茅台酒及系列酒的生产与销售;饮料、食品、包装材料的生产、销售;防伪技术开发;酒店经营管理",
            "sshy": "酿酒行业"
          }
        }
        """;

        var profile = EastmoneyCompanyProfileParser.Parse("sh600519", surveyJson);

        Assert.Contains(profile.Facts, item => item.Label == "主营业务" && item.Value.Contains("茅台酒", StringComparison.Ordinal));
        Assert.Contains(profile.Facts, item => item.Label == "主营业务" && item.Source == "东方财富公司概况(经营范围摘要)");
        Assert.Contains(profile.Facts, item => item.Label == "经营范围");
        Assert.Equal("酿酒行业", profile.SectorName);
    }

    [Fact]
    public void Parse_WhenZyywPresent_ShouldNotDeriveFromScope()
    {
        const string surveyJson = """
        {
          "jbzl": {
            "agjc": "浦发银行",
            "zyyw": "商业银行业务",
            "jyfw": "吸收公众存款;发放贷款;办理结算",
            "sshy": "银行"
          }
        }
        """;

        var profile = EastmoneyCompanyProfileParser.Parse("sh600000", surveyJson);

        var mainBizFact = Assert.Single(profile.Facts, item => item.Label == "主营业务");
        Assert.Equal("商业银行业务", mainBizFact.Value);
        Assert.Equal("东方财富公司概况", mainBizFact.Source);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("茅台酒及系列酒的生产与销售;饮料、食品", "茅台酒及系列酒的生产与销售；饮料、食品")]
    [InlineData("单一业务", "单一业务")]
    public void DeriveMainBusinessFromScope_ShouldExtractFirstSegments(string? input, string? expected)
    {
        var result = EastmoneyCompanyProfileParser.DeriveMainBusinessFromScope(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseMainBusinessComposition_ShouldExtractProductAndRegionFacts()
    {
        const string json = """
        {
          "result": {
            "data": [
              {
                "ITEM_NAME": "茅台酒",
                "MAIN_BUSINESS_INCOME": 110795000000,
                "MBI_RATIO": 0.8603,
                "MAINOP_TYPE": 2,
                "REPORT_DATE": "2025-09-30 00:00:00"
              },
              {
                "ITEM_NAME": "系列酒",
                "MAIN_BUSINESS_INCOME": 17924000000,
                "MBI_RATIO": 0.1392,
                "MAINOP_TYPE": 2,
                "REPORT_DATE": "2025-09-30 00:00:00"
              },
              {
                "ITEM_NAME": "国内",
                "MAIN_BUSINESS_INCOME": 124800000000,
                "MBI_RATIO": 0.9693,
                "MAINOP_TYPE": 3,
                "REPORT_DATE": "2025-09-30 00:00:00"
              },
              {
                "ITEM_NAME": "国外",
                "MAIN_BUSINESS_INCOME": 3900000000,
                "MBI_RATIO": 0.0303,
                "MAINOP_TYPE": 3,
                "REPORT_DATE": "2025-09-30 00:00:00"
              },
              {
                "ITEM_NAME": "旧产品",
                "MAIN_BUSINESS_INCOME": 50000000000,
                "MBI_RATIO": 0.50,
                "MAINOP_TYPE": 2,
                "REPORT_DATE": "2024-12-31 00:00:00"
              }
            ]
          }
        }
        """;

        var facts = EastmoneyCompanyProfileParser.ParseMainBusinessComposition(json);

        // 2 product + 2 region + 1 summary = 5, old-period item excluded
        Assert.Equal(5, facts.Count);
        Assert.Contains(facts, f => f.Label == "主营构成(产品)-茅台酒" && f.Value.Contains("亿元") && f.Value.Contains("86.03%"));
        Assert.Contains(facts, f => f.Label == "主营构成(产品)-系列酒" && f.Value.Contains("亿元") && f.Value.Contains("13.92%"));
        Assert.Contains(facts, f => f.Label == "主营构成(地区)-国内" && f.Value.Contains("96.93%"));
        Assert.Contains(facts, f => f.Label == "主营构成(地区)-国外" && f.Value.Contains("3.03%"));
        Assert.Contains(facts, f => f.Label == "主营构成报告期" && f.Value == "2025-09-30");
        Assert.All(facts.Where(f => f.Label != "主营构成报告期"), f => Assert.Equal("东方财富主营构成", f.Source));
    }

    [Fact]
    public void ParseMainBusinessComposition_WithNullOrEmpty_ShouldReturnEmpty()
    {
        Assert.Empty(EastmoneyCompanyProfileParser.ParseMainBusinessComposition(null));
        Assert.Empty(EastmoneyCompanyProfileParser.ParseMainBusinessComposition(""));
        Assert.Empty(EastmoneyCompanyProfileParser.ParseMainBusinessComposition("{}"));
    }

    [Fact]
    public void ParseMainBusinessComposition_SmallIncome_ShouldUseWanYuan()
    {
        const string json = """
        {
          "result": {
            "data": [
              {
                "ITEM_NAME": "其他",
                "MAIN_BUSINESS_INCOME": 5000000,
                "MBI_RATIO": 0.005,
                "MAINOP_TYPE": 2,
                "REPORT_DATE": "2025-06-30 00:00:00"
              }
            ]
          }
        }
        """;

        var facts = EastmoneyCompanyProfileParser.ParseMainBusinessComposition(json);

        Assert.Contains(facts, f => f.Label == "主营构成(产品)-其他" && f.Value.Contains("500万元") && f.Value.Contains("0.5%"));
    }

    [Theory]
    [InlineData(100000000000.0, 0.86, "1000亿元 86.0%")]
    [InlineData(50000000.0, null, "5000万元")]
    [InlineData(null, 0.5, "50.0%")]
    [InlineData(null, null, "--")]
    public void FormatMainBusinessValue_ShouldFormatCorrectly(double? income, double? ratio, string expected)
    {
        var result = EastmoneyCompanyProfileParser.FormatMainBusinessValue(
            income.HasValue ? (decimal)income.Value : null,
            ratio.HasValue ? (decimal)ratio.Value : null);
        Assert.Equal(expected, result);
    }
}