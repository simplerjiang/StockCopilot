using System.Text.Json;
using System.Text.Json.Nodes;
using SimplerJiangAiAgent.Api.Modules.Stocks;
using Xunit;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StocksModuleAugmentResponseTests
{
    private const string CoreFieldsTemplate = """
        {
          "symbol": "600519",
          "success": true,
          "channel": "eastmoney",
          "reportCount": 3,
          "savedCount": 2,
          "skippedCount": 1,
          "failedCount": 0,
          "durationMs": 1234,
          "startedAtUtc": "2026-04-22T01:00:00Z",
          "completedAtUtc": "2026-04-22T01:00:01Z",
          "errors": []
        }
        """;

    private static readonly string[] CoreFieldNames =
    {
        "symbol", "success", "channel", "reportCount", "savedCount", "skippedCount",
        "failedCount", "durationMs", "startedAtUtc", "completedAtUtc", "errors",
    };

    private static JsonObject BuildInput(Action<JsonObject> mutate)
    {
        var obj = (JsonObject)JsonNode.Parse(CoreFieldsTemplate)!;
        mutate(obj);
        return obj;
    }

    private static void AssertCoreFieldsPreserved(JsonObject before, JsonObject after)
    {
        // Order check: first 11 keys must match the template order.
        var afterKeys = after.Select(kv => kv.Key).Take(CoreFieldNames.Length).ToArray();
        Assert.Equal(CoreFieldNames, afterKeys);

        foreach (var name in CoreFieldNames)
        {
            var b = before[name];
            var a = after[name];
            Assert.Equal(b?.ToJsonString(), a?.ToJsonString());
        }
    }

    [Fact]
    public void HappyPath_GeneratesAllFiveAliases()
    {
        var input = BuildInput(o =>
        {
            o["reportPeriods"] = new JsonArray("2025Q4", "2025Q3");
            o["reportTitles"] = new JsonArray("年度报告", "三季度报告");
            o["mainSourceChannel"] = "eastmoney";
            o["degradeReason"] = "primary-failed";
            o["pdfSummarySupplement"] = "summary text";
        });
        var before = (JsonObject)JsonNode.Parse(input.ToJsonString())!;

        var resultJson = StocksModule.AugmentCollectResponseWithFriendlyAliases(input.ToJsonString());
        var result = (JsonObject)JsonNode.Parse(resultJson)!;

        Assert.Equal("2025Q4", (string?)result["reportPeriod"]);
        Assert.Equal("年度报告", (string?)result["reportTitle"]);
        Assert.Equal("eastmoney", (string?)result["sourceChannel"]);
        Assert.Equal("primary-failed", (string?)result["fallbackReason"]);
        Assert.Equal("summary text", (string?)result["pdfSummary"]);

        AssertCoreFieldsPreserved(before, result);
    }

    [Fact]
    public void EmptyArrays_YieldNullPeriodAndTitle()
    {
        var input = BuildInput(o =>
        {
            o["reportPeriods"] = new JsonArray();
            o["reportTitles"] = new JsonArray();
        });
        var before = (JsonObject)JsonNode.Parse(input.ToJsonString())!;

        var resultJson = StocksModule.AugmentCollectResponseWithFriendlyAliases(input.ToJsonString());
        var result = (JsonObject)JsonNode.Parse(resultJson)!;

        Assert.True(result.ContainsKey("reportPeriod"));
        Assert.True(result.ContainsKey("reportTitle"));
        Assert.Null(result["reportPeriod"]);
        Assert.Null(result["reportTitle"]);

        AssertCoreFieldsPreserved(before, result);
    }

    [Fact]
    public void NullSources_YieldNullAliases()
    {
        var input = BuildInput(o =>
        {
            o["mainSourceChannel"] = null;
            o["degradeReason"] = null;
            o["pdfSummarySupplement"] = null;
        });
        var before = (JsonObject)JsonNode.Parse(input.ToJsonString())!;

        var resultJson = StocksModule.AugmentCollectResponseWithFriendlyAliases(input.ToJsonString());
        var result = (JsonObject)JsonNode.Parse(resultJson)!;

        Assert.True(result.ContainsKey("sourceChannel"));
        Assert.True(result.ContainsKey("fallbackReason"));
        Assert.True(result.ContainsKey("pdfSummary"));
        Assert.Null(result["sourceChannel"]);
        Assert.Null(result["fallbackReason"]);
        Assert.Null(result["pdfSummary"]);

        AssertCoreFieldsPreserved(before, result);
    }

    [Fact]
    public void ConflictProtection_ExistingAliasIsNotOverwritten()
    {
        var input = BuildInput(o =>
        {
            o["reportPeriod"] = "UPSTREAM_PERIOD";
            o["reportTitle"] = "UPSTREAM_TITLE";
            o["sourceChannel"] = "UPSTREAM_CHANNEL";
            o["fallbackReason"] = "UPSTREAM_REASON";
            o["pdfSummary"] = "UPSTREAM_SUMMARY";
            o["reportPeriods"] = new JsonArray("SHOULD_NOT_USE");
            o["reportTitles"] = new JsonArray("SHOULD_NOT_USE");
            o["mainSourceChannel"] = "SHOULD_NOT_USE";
            o["degradeReason"] = "SHOULD_NOT_USE";
            o["pdfSummarySupplement"] = "SHOULD_NOT_USE";
        });

        var resultJson = StocksModule.AugmentCollectResponseWithFriendlyAliases(input.ToJsonString());
        var result = (JsonObject)JsonNode.Parse(resultJson)!;

        Assert.Equal("UPSTREAM_PERIOD", (string?)result["reportPeriod"]);
        Assert.Equal("UPSTREAM_TITLE", (string?)result["reportTitle"]);
        Assert.Equal("UPSTREAM_CHANNEL", (string?)result["sourceChannel"]);
        Assert.Equal("UPSTREAM_REASON", (string?)result["fallbackReason"]);
        Assert.Equal("UPSTREAM_SUMMARY", (string?)result["pdfSummary"]);
    }

    [Theory]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    [InlineData("not a json {{{")]
    [InlineData("")]
    [InlineData("   ")]
    public void MalformedInput_ReturnedUnchanged(string input)
    {
        var result = StocksModule.AugmentCollectResponseWithFriendlyAliases(input);
        Assert.Equal(input, result);
    }
}
