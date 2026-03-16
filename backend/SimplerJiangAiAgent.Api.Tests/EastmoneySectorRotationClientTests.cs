using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Market.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class EastmoneySectorRotationClientTests
{
    [Theory]
    [InlineData(SectorBoardTypes.Industry, "m:90+t:2")]
    [InlineData(SectorBoardTypes.Concept, "m:90+t:3")]
    [InlineData(SectorBoardTypes.Style, "m:90+t:3")]
    public void NormalizeBoardFilter_UsesValidatedEastmoneyBoardMappings(string boardType, string expected)
    {
        var result = EastmoneySectorRotationClient.NormalizeBoardFilter(boardType);

        Assert.Equal(expected, result);
    }
}