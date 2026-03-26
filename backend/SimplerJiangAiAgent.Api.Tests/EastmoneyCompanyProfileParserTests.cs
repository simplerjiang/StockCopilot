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
}