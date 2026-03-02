using System.Text.Json;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;
using Xunit;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class StockAgentResultNormalizerTests
{
    [Fact]
    public void Normalize_CommanderAddsMissingGoal007Fields()
    {
        using var input = JsonDocument.Parse("""
        {
          "agent": "commander",
          "summary": "ok",
          "recommendation": {
            "confidence": 0.7
          }
        }
        """);

        var normalized = StockAgentResultNormalizer.Normalize(StockAgentKind.Commander, input.RootElement);

        Assert.True(normalized.TryGetProperty("evidence", out _));
        Assert.True(normalized.TryGetProperty("triggers", out _));
        Assert.True(normalized.TryGetProperty("invalidations", out _));
        Assert.True(normalized.TryGetProperty("riskLimits", out _));

        var recommendation = normalized.GetProperty("recommendation");
        Assert.True(recommendation.TryGetProperty("action", out _));
        Assert.True(recommendation.TryGetProperty("targetPrice", out _));
        Assert.True(recommendation.TryGetProperty("stopLossPrice", out _));
    }

    [Fact]
    public void Normalize_StockNewsKeepsAgentAndAddsDefaults()
    {
        using var input = JsonDocument.Parse("""
        {
          "agent": "stock_news",
          "summary": "news"
        }
        """);

        var normalized = StockAgentResultNormalizer.Normalize(StockAgentKind.StockNews, input.RootElement);

        Assert.Equal("stock_news", normalized.GetProperty("agent").GetString());
        Assert.True(normalized.TryGetProperty("confidence", out _));
        Assert.True(normalized.TryGetProperty("events", out var events));
        Assert.Equal(JsonValueKind.Array, events.ValueKind);
        Assert.True(normalized.TryGetProperty("evidence", out var evidence));
        Assert.Equal(JsonValueKind.Array, evidence.ValueKind);
    }

    [Fact]
    public void Normalize_StockNewsWithoutUsableEvidence_DowngradesToNeutralWatch()
    {
        using var input = JsonDocument.Parse("""
        {
          "agent": "stock_news",
          "summary": "看多",
          "confidence": 88,
          "evidence": [
            {
              "point": "消息",
              "source": "",
              "publishedAt": null
            }
          ]
        }
        """);

        var normalized = StockAgentResultNormalizer.Normalize(StockAgentKind.StockNews, input.RootElement);

        Assert.Equal("信息不足：缺少可验证来源或发布时间，建议观望。", normalized.GetProperty("summary").GetString());
        Assert.Equal(20, normalized.GetProperty("confidence").GetInt32());
        Assert.Contains("观望", normalized.GetProperty("signals")[0].GetString());
    }

    [Fact]
    public void Normalize_SectorNewsWithoutUsableEvidence_DowngradesToNeutralWatch()
    {
        using var input = JsonDocument.Parse("""
        {
          "agent": "sector_news",
          "summary": "偏多",
          "confidence": 75,
          "evidence": [
            {
              "point": "板块热度",
              "source": "未知",
              "publishedAt": "not-a-time"
            }
          ]
        }
        """);

        var normalized = StockAgentResultNormalizer.Normalize(StockAgentKind.SectorNews, input.RootElement);

        Assert.Equal("信息不足：缺少可验证来源或发布时间，建议观望。", normalized.GetProperty("summary").GetString());
        Assert.Equal(20, normalized.GetProperty("confidence").GetInt32());
        Assert.Contains("观望", normalized.GetProperty("signals")[0].GetString());
    }
}
