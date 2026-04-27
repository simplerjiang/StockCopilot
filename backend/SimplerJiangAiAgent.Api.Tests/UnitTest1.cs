using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public class StockSymbolNormalizerTests
{
    [Theory]
    [InlineData("600000", "sh600000")]
    [InlineData("000001", "sz000001")]
    [InlineData("sh600000", "sh600000")]
    [InlineData("SZ000001", "sz000001")]
    [InlineData("bj430047", "bj430047")]
    public void Normalize_ShouldReturnExpected(string input, string expected)
    {
        var result = StockSymbolNormalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("600519")]
    [InlineData("sh600519")]
    [InlineData("SZ000001")]
    [InlineData("bj430047")]
    [InlineData(" sh600519 ")]
    public void IsValid_ShouldAcceptValidSymbols(string input)
    {
        Assert.True(StockSymbolNormalizer.IsValid(input));
    }

    [Theory]
    [InlineData("abc123")]
    [InlineData("!!!!")]
    [InlineData("99999")]
    [InlineData("xyz999")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("sh")]
    [InlineData("hk")]
    [InlineData("1234567")]
    [InlineData("hello")]
    [InlineData("us.AAPL")]
    [InlineData("us.TSLA")]
    [InlineData("gb.VOD")]
    [InlineData("hk00700")]
    [InlineData("hk9")]
    [InlineData("hk.00700")]
    public void IsValid_ShouldRejectInvalidSymbols(string? input)
    {
        Assert.False(StockSymbolNormalizer.IsValid(input));
    }

    [Theory]
    [InlineData("600519", new[] { "600519", "sh600519", "SH600519" })]
    [InlineData("sh600519", new[] { "sh600519", "SH600519", "600519" })]
    [InlineData("SZ000001", new[] { "SZ000001", "sz000001", "000001" })]
    public void BuildSymbolAliases_ShouldCoverRawAndNormalizedForms(string input, string[] expectedAliases)
    {
        var aliases = FinancialDataReadService.BuildSymbolAliases(input);

        foreach (var expectedAlias in expectedAliases)
        {
            Assert.Contains(expectedAlias, aliases);
        }
    }

    [Theory]
    [InlineData("us.AAPL", true)]
    [InlineData("us.TSLA", true)]
    [InlineData("gb.VOD", true)]
    [InlineData("sh600519", false)]
    [InlineData("bj430047", false)]
    [InlineData("hk00700", true)]
    [InlineData("hk.00700", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsForeignMarket_ShouldDetectCorrectly(string? input, bool expected)
    {
        Assert.Equal(expected, StockSymbolNormalizer.IsForeignMarket(input));
    }

    [Theory]
    [InlineData("sh000001", true)]
    [InlineData("sz399001", true)]
    [InlineData("399006", true)]
    [InlineData("sz000518", false)]
    [InlineData("000001", false)]
    [InlineData("bj430047", false)]
    public void IsIndex_ShouldBeExchangeAware(string input, bool expected)
    {
        Assert.Equal(expected, StockSymbolNormalizer.IsIndex(input));
    }

    [Theory]
    [InlineData("430047", "bj430047")]
    [InlineData("830799", "bj830799")]
    public void Normalize_ShouldHandleBjStocks(string input, string expected)
    {
        Assert.Equal(expected, StockSymbolNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_ShouldReturnTrimmedHkSymbolWithoutMarkingItValid()
    {
        Assert.Equal("hk00700", StockSymbolNormalizer.Normalize(" hk00700 "));
        Assert.False(StockSymbolNormalizer.IsValid("hk00700"));
    }
}

public class StockNameNormalizerTests
{
    [Theory]
    [InlineData("*ST 四环", "*ST四环")]
    [InlineData("*ST　四环", "*ST四环")]
    [InlineData("*ST八钢", "*ST八钢")]
    [InlineData("ST 四环", "ST四环")]
    [InlineData("S*ST 四环", "S*ST四环")]
    [InlineData("普通 公司", "普通 公司")]
    [InlineData("Foo Bar", "Foo Bar")]
    public void NormalizeDisplayName_ShouldOnlyRemoveWhitespaceAfterSpecialStPrefix(string input, string expected)
    {
        Assert.Equal(expected, StockNameNormalizer.NormalizeDisplayName(input));
    }

    [Fact]
    public void NormalizeDisplayNameOrNull_ShouldPreserveBlankAsNull()
    {
        Assert.Null(StockNameNormalizer.NormalizeDisplayNameOrNull(null));
        Assert.Null(StockNameNormalizer.NormalizeDisplayNameOrNull("   "));
    }
}
