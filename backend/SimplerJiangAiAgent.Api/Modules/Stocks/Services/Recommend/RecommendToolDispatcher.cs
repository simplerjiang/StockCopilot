using System.Text.Encodings.Web;
using System.Text.Json;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend.WebSearch;
using Microsoft.Extensions.Logging;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend;

/// <summary>
/// Dispatches tool_call requests from LLM output to the corresponding MCP gateway methods.
/// </summary>
public interface IRecommendToolDispatcher
{
    Task<string> DispatchAsync(string toolName, Dictionary<string, string> args, CancellationToken ct = default);
}

public sealed class RecommendToolDispatcher : IRecommendToolDispatcher
{
    private readonly IMcpToolGateway _gateway;
    private readonly ILogger<RecommendToolDispatcher> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // Simple circuit breaker
    private static int _consecutiveFailures;
    private static DateTime _circuitOpenUntil = DateTime.MinValue;
    private const int CircuitBreakerThreshold = 3;
    private static readonly TimeSpan CircuitBreakerCooldown = TimeSpan.FromMinutes(5);

    public RecommendToolDispatcher(IMcpToolGateway gateway, ILogger<RecommendToolDispatcher> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    private bool IsCircuitOpen()
    {
        if (DateTime.UtcNow < _circuitOpenUntil)
            return true;
        return false;
    }

    private void RecordSuccess()
    {
        Interlocked.Exchange(ref _consecutiveFailures, 0);
    }

    private void RecordFailure()
    {
        if (Interlocked.Increment(ref _consecutiveFailures) >= CircuitBreakerThreshold)
        {
            _circuitOpenUntil = DateTime.UtcNow.Add(CircuitBreakerCooldown);
            _logger.LogWarning("Recommend tool circuit breaker opened for {Minutes}min after {Count} consecutive failures",
                CircuitBreakerCooldown.TotalMinutes, _consecutiveFailures);
        }
    }

    public async Task<string> DispatchAsync(string toolName, Dictionary<string, string> args, CancellationToken ct = default)
    {
        _logger.LogInformation("RecommendToolDispatch: {Tool} args={Args}", toolName, JsonSerializer.Serialize(args, JsonOpts));

        if (IsCircuitOpen())
            return JsonSerializer.Serialize(new { tool_error = true, error = "工具服务暂时不可用（熔断中），请基于已有信息继续分析" }, JsonOpts);

        try
        {
            var result = toolName switch
            {
                "web_search" => JsonSerializer.Serialize(
                    await _gateway.WebSearchAsync(
                        GetRequired(args, "query"),
                        new WebSearchOptions { MaxResults = GetInt(args, "max_results", 5) },
                        ct), JsonOpts),

                "web_search_news" => JsonSerializer.Serialize(
                    await _gateway.WebSearchNewsAsync(
                        GetRequired(args, "query"),
                        new WebSearchOptions { MaxResults = GetInt(args, "max_results", 5) },
                        ct), JsonOpts),

                "web_read_url" => string.IsNullOrWhiteSpace(GetRequired(args, "url"))
                    ? JsonSerializer.Serialize(new { tool_error = true, error = "缺少必需参数: url" }, JsonOpts)
                    : JsonSerializer.Serialize(
                        await _gateway.WebReadUrlAsync(
                            GetRequired(args, "url"),
                            GetInt(args, "max_chars", 8000),
                            ct), JsonOpts),

                "market_context" => JsonSerializer.Serialize(
                    await _gateway.GetMarketContextAsync(
                        args.GetValueOrDefault("symbol", "000001"),
                        null, null, ct), JsonOpts),

                "sector_realtime" => JsonSerializer.Serialize(
                    await _gateway.GetMarketContextAsync("000001", null, null, ct), JsonOpts),

                "stock_search" => JsonSerializer.Serialize(
                    await _gateway.SearchAsync(
                        GetRequired(args, "query"),
                        true, null, ct), JsonOpts),

                "stock_news" => JsonSerializer.Serialize(
                    await _gateway.GetNewsAsync(
                        args.GetValueOrDefault("symbol", "000001"),
                        args.GetValueOrDefault("level", "stock"),
                        null, null, ct), JsonOpts),

                "stock_kline" => JsonSerializer.Serialize(
                    await _gateway.GetKlineAsync(
                        GetRequiredSymbol(args),
                        args.GetValueOrDefault("interval", "day"),
                        GetInt(args, "count", 60),
                        null, null, null, ct), JsonOpts),

                "stock_minute" => JsonSerializer.Serialize(
                    await _gateway.GetMinuteAsync(
                        GetRequiredSymbol(args),
                        null, null, null, ct), JsonOpts),

                "stock_fundamentals" => JsonSerializer.Serialize(
                    await _gateway.GetFundamentalsAsync(
                        GetRequiredSymbol(args),
                        null, null, ct), JsonOpts),

                "stock_financial_report" => JsonSerializer.Serialize(
                    await _gateway.GetFinancialReportAsync(
                        GetRequiredSymbol(args),
                        GetInt(args, "periods", 4),
                        null, ct), JsonOpts),

                "stock_financial_trend" => JsonSerializer.Serialize(
                    await _gateway.GetFinancialTrendAsync(
                        GetRequiredSymbol(args),
                        GetInt(args, "periods", 8),
                        null, ct), JsonOpts),

                "stock_strategy" => JsonSerializer.Serialize(
                    await _gateway.GetStrategyAsync(
                        GetRequiredSymbol(args),
                        args.GetValueOrDefault("interval", "day"),
                        GetInt(args, "count", 60),
                        null, null, null, null, ct), JsonOpts),

                _ => JsonSerializer.Serialize(new { tool_error = true, error = $"未知工具: {toolName}" }, JsonOpts)
            };

            RecordSuccess();
            return result;
        }
        catch (MissingToolParameterException ex)
        {
            _logger.LogWarning("RecommendToolDispatch missing param: {Tool} {Param}", toolName, ex.ParameterName);
            return JsonSerializer.Serialize(new { tool_error = true, error = ex.Message }, JsonOpts);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RecommendToolDispatch failed: {Tool}", toolName);
            RecordFailure();
            return JsonSerializer.Serialize(new { tool_error = true, error = $"工具调用失败: {ex.Message}" }, JsonOpts);
        }
    }

    private static string GetRequiredSymbol(Dictionary<string, string> args)
    {
        if (args.TryGetValue("symbol", out var symbol) && !string.IsNullOrWhiteSpace(symbol))
            return symbol;
        throw new MissingToolParameterException("symbol");
    }

    private static string GetRequired(Dictionary<string, string> args, string key)
    {
        if (args.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;
        // URL returns empty so the caller's existing empty-check fires
        if (key == "url")
            return "";
        throw new MissingToolParameterException(key);
    }

    internal sealed class MissingToolParameterException : Exception
    {
        public string ParameterName { get; }
        public MissingToolParameterException(string parameterName)
            : base($"缺少必需参数: {parameterName}") => ParameterName = parameterName;
    }

    private static int GetInt(Dictionary<string, string> args, string key, int defaultValue)
    {
        if (args.TryGetValue(key, out var value) && int.TryParse(value, out var result))
            return Math.Clamp(result, 1, 200);
        return defaultValue;
    }
}
