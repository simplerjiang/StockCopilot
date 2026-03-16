using System.Text.Json;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface ITradingPlanDraftService
{
    Task<TradingPlanDraftDto> BuildDraftAsync(string symbol, long analysisHistoryId, CancellationToken cancellationToken = default);
}

public sealed class TradingPlanDraftService : ITradingPlanDraftService
{
    private readonly IStockAgentHistoryService _historyService;
    private readonly IStockMarketContextService _marketContextService;

    public TradingPlanDraftService(IStockAgentHistoryService historyService, IStockMarketContextService marketContextService)
    {
        _historyService = historyService;
        _marketContextService = marketContextService;
    }

    public async Task<TradingPlanDraftDto> BuildDraftAsync(string symbol, long analysisHistoryId, CancellationToken cancellationToken = default)
    {
        var normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            throw new ArgumentException("symbol 不能为空", nameof(symbol));
        }

        var history = await _historyService.GetByIdAsync(analysisHistoryId, cancellationToken);
        if (history is null)
        {
            throw new InvalidOperationException("分析历史不存在");
        }

        if (!string.Equals(history.Symbol, normalizedSymbol, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("分析历史与当前股票不匹配");
        }

        using var document = JsonDocument.Parse(history.ResultJson);
        var commanderData = FindCommanderData(document.RootElement);
        if (commanderData is null)
        {
            throw new InvalidOperationException("未找到 commander 分析结果");
        }

        var financialData = FindAgentData(document.RootElement, "financial_analysis");
        var trendData = FindAgentData(document.RootElement, "trend_analysis");

        var summary = ReadString(commanderData.Value, "analysis_opinion") ?? ReadString(commanderData.Value, "summary");
        var expectedCatalyst = ReadString(commanderData.Value, "trigger_conditions");
        var invalidConditions = ReadString(commanderData.Value, "invalid_conditions");
        var riskLimits = JoinArray(commanderData.Value, "riskLimits") ?? ReadString(commanderData.Value, "risk_warning");
        var triggerPrice = ReadChartPrice(commanderData.Value, "breakoutPrice");
        var invalidPrice = ReadChartPrice(commanderData.Value, "supportPrice");
        var stopLossPrice = ReadChartPrice(commanderData.Value, "stopLossPrice")
            ?? ReadChartPrice(commanderData.Value, "supportPrice");
        var targetPrice = ReadChartPrice(commanderData.Value, "targetPrice")
            ?? ReadMetricPrice(financialData, "institutionTargetPrice")
            ?? ReadForecastExtreme(trendData, true);
        var takeProfitPrice = ReadChartPrice(commanderData.Value, "takeProfitPrice")
            ?? targetPrice
            ?? ReadForecastExtreme(trendData, true);
        var direction = ResolveDirection(commanderData.Value).ToString();
        var marketContext = await _marketContextService.GetLatestAsync(normalizedSymbol, cancellationToken);

        return new TradingPlanDraftDto(
            normalizedSymbol,
            history.Name,
            direction,
            TradingPlanStatus.Pending.ToString(),
            triggerPrice,
            invalidPrice,
            stopLossPrice,
            takeProfitPrice,
            targetPrice,
            expectedCatalyst,
            invalidConditions,
            riskLimits,
            summary,
            history.Id,
            "commander",
                null,
                marketContext);
    }

    private static JsonElement? FindCommanderData(JsonElement root)
    {
        return FindAgentData(root, "commander");
    }

    private static JsonElement? FindAgentData(JsonElement root, string agentIdName)
    {
        if (!root.TryGetProperty("agents", out var agents) || agents.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var agent in agents.EnumerateArray())
        {
            if (!agent.TryGetProperty("agentId", out var agentId) || agentId.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (!string.Equals(agentId.GetString(), agentIdName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (agent.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                return data;
            }
        }

        return null;
    }

    private static decimal? ReadMetricPrice(JsonElement? data, string propertyName)
    {
        if (data is null || !data.Value.TryGetProperty("metrics", out var metrics) || metrics.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!metrics.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out decimalValue))
        {
            return decimalValue;
        }

        return null;
    }

    private static decimal? ReadForecastExtreme(JsonElement? data, bool highest)
    {
        if (data is null || !data.Value.TryGetProperty("forecast", out var forecast) || forecast.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var prices = forecast.EnumerateArray()
            .Select(item =>
            {
                if (!item.TryGetProperty("price", out var price))
                {
                    return (decimal?)null;
                }

                if (price.ValueKind == JsonValueKind.Number && price.TryGetDecimal(out var decimalValue))
                {
                    return decimalValue;
                }

                return price.ValueKind == JsonValueKind.String && decimal.TryParse(price.GetString(), out decimalValue)
                    ? decimalValue
                    : (decimal?)null;
            })
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .ToArray();

        if (prices.Length == 0)
        {
            return null;
        }

        return highest ? prices.Max() : prices.Min();
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var result = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static string? JoinArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

        return items.Length == 0 ? null : string.Join("；", items);
    }

    private static decimal? ReadChartPrice(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty("chart", out var chart) || chart.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!chart.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out decimalValue))
        {
            return decimalValue;
        }

        return null;
    }

    private static TradingPlanDirection ResolveDirection(JsonElement root)
    {
        var candidates = new[]
        {
            ReadString(root, "direction"),
            ReadString(root, "action"),
            ReadString(root, "recommendation"),
            ReadString(root, "analysis_opinion")
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (candidate.Contains("short", StringComparison.OrdinalIgnoreCase)
                || candidate.Contains("bear", StringComparison.OrdinalIgnoreCase)
                || candidate.Contains("看空", StringComparison.OrdinalIgnoreCase)
                || candidate.Contains("减仓", StringComparison.OrdinalIgnoreCase)
                || candidate.Contains("卖出", StringComparison.OrdinalIgnoreCase))
            {
                return TradingPlanDirection.Short;
            }

            if (candidate.Contains("long", StringComparison.OrdinalIgnoreCase)
                || candidate.Contains("bull", StringComparison.OrdinalIgnoreCase)
                || candidate.Contains("看多", StringComparison.OrdinalIgnoreCase)
                || candidate.Contains("加仓", StringComparison.OrdinalIgnoreCase)
                || candidate.Contains("买入", StringComparison.OrdinalIgnoreCase))
            {
                return TradingPlanDirection.Long;
            }
        }

        return TradingPlanDirection.Long;
    }
}