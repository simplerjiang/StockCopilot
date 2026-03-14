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
}