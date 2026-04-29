using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;

namespace SimplerJiangAiAgent.Api.Modules.Backtest;

public interface IBacktestService
{
    Task<BacktestResult?> RunAsync(long historyId, CancellationToken ct = default);
    Task<BacktestBatchResultDto> RunBatchAsync(string? symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default);
}

public record BacktestBatchResultDto(int Total, int Success, int Skipped, int Failed);

public sealed class BacktestService : IBacktestService
{
    private readonly AppDbContext _dbContext;

    public BacktestService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BacktestResult?> RunAsync(long historyId, CancellationToken ct = default)
    {
        var history = await _dbContext.StockAgentAnalysisHistories
            .FirstOrDefaultAsync(h => h.Id == historyId, ct);
        if (history == null) return null;

        var existing = await _dbContext.BacktestResults
            .FirstOrDefaultAsync(b => b.AnalysisHistoryId == historyId, ct);
        if (existing?.CalcStatus == "calculated") return existing;

        var signal = ExtractSignal(history.ResultJson);
        if (signal == null)
        {
            return await SaveResult(history, existing, null, "insufficient_data", ct);
        }

        var analysisDate = DateOnly.FromDateTime(history.CreatedAt);

        // K lines after analysis date
        var klines = await _dbContext.KLinePoints
            .Where(k => k.Symbol == history.Symbol
                && k.Interval == "day"
                && k.Date > history.CreatedAt.Date)
            .OrderBy(k => k.Date)
            .Take(15)
            .ToListAsync(ct);

        // Base close: last close on or before analysis date
        var baseKline = await _dbContext.KLinePoints
            .Where(k => k.Symbol == history.Symbol
                && k.Interval == "day"
                && k.Date <= history.CreatedAt.Date)
            .OrderByDescending(k => k.Date)
            .FirstOrDefaultAsync(ct);

        if (baseKline == null || baseKline.Close == 0)
        {
            return await SaveResult(history, existing, signal, "insufficient_data", ct);
        }

        var baseClose = baseKline.Close;
        var windows = new[] { 1, 3, 5, 10 };
        var returns = new decimal?[4];
        var isCorrect = new bool?[4];

        for (int i = 0; i < windows.Length; i++)
        {
            if (klines.Count >= windows[i])
            {
                var targetClose = klines[windows[i] - 1].Close;
                returns[i] = Math.Round((targetClose - baseClose) / baseClose * 100m, 2);
                isCorrect[i] = JudgeCorrectness(signal.Direction, returns[i]!.Value);
            }
        }

        // Target price / stop loss within 10-day window
        var within10 = klines.Take(10).ToList();
        bool? targetHit = null;
        bool? stopTriggered = null;

        if (signal.TargetPrice.HasValue && within10.Count > 0)
        {
            if (signal.Direction is "看多" or "偏多")
                targetHit = within10.Any(k => k.High >= signal.TargetPrice.Value);
            else if (signal.Direction is "看空" or "偏空")
                targetHit = within10.Any(k => k.Low <= signal.TargetPrice.Value);
        }

        if (signal.StopLoss.HasValue && within10.Count > 0)
        {
            if (signal.Direction is "看多" or "偏多")
                stopTriggered = within10.Any(k => k.Low <= signal.StopLoss.Value);
            else if (signal.Direction is "看空" or "偏空")
                stopTriggered = within10.Any(k => k.High >= signal.StopLoss.Value);
        }

        var result = existing ?? new BacktestResult
        {
            AnalysisHistoryId = historyId,
            Symbol = history.Symbol,
            Name = history.Name,
            CreatedAt = DateTime.UtcNow
        };

        result.AnalysisDate = analysisDate;
        result.PredictedDirection = signal.Direction;
        result.Confidence = (int)signal.Confidence;
        result.TargetPrice = signal.TargetPrice;
        result.StopLoss = signal.StopLoss;
        result.Window1dActual = returns[0];
        result.Window3dActual = returns[1];
        result.Window5dActual = returns[2];
        result.Window10dActual = returns[3];
        result.IsCorrect1d = isCorrect[0];
        result.IsCorrect3d = isCorrect[1];
        result.IsCorrect5d = isCorrect[2];
        result.IsCorrect10d = isCorrect[3];
        result.TargetHit = targetHit;
        result.StopTriggered = stopTriggered;
        result.CalcStatus = klines.Count >= 1 ? "calculated" : "insufficient_data";
        result.UpdatedAt = DateTime.UtcNow;

        if (existing == null) _dbContext.BacktestResults.Add(result);
        await _dbContext.SaveChangesAsync(ct);
        return result;
    }

    public async Task<BacktestBatchResultDto> RunBatchAsync(string? symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var query = _dbContext.StockAgentAnalysisHistories.AsQueryable();
        if (!string.IsNullOrEmpty(symbol))
            query = query.Where(h => h.Symbol == symbol);
        if (from.HasValue)
            query = query.Where(h => h.CreatedAt >= from.Value.ToDateTime(TimeOnly.MinValue));
        if (to.HasValue)
            query = query.Where(h => h.CreatedAt <= to.Value.ToDateTime(TimeOnly.MaxValue));

        var alreadyDone = _dbContext.BacktestResults
            .Select(b => b.AnalysisHistoryId);

        var histories = await query
            .Where(h => !alreadyDone.Contains(h.Id))
            .OrderBy(h => h.CreatedAt)
            .ToListAsync(ct);

        int total = histories.Count, success = 0, skipped = 0, failed = 0;

        foreach (var h in histories)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await RunAsync(h.Id, ct);
                if (result?.CalcStatus == "calculated") success++;
                else if (result?.CalcStatus == "insufficient_data") skipped++;
                else failed++;
            }
            catch
            {
                failed++;
            }
        }

        return new BacktestBatchResultDto(total, success, skipped, failed);
    }

    // ── Signal extraction ──────────────────────────

    private record AnalysisSignal(string Direction, decimal Confidence, decimal? TargetPrice, decimal? StopLoss);

    private static AnalysisSignal? ExtractSignal(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;

            JsonElement? commander = null;
            if (TryGetProperty(root, "agents", out var agents) && agents.ValueKind == JsonValueKind.Array)
            {
                foreach (var agent in agents.EnumerateArray())
                {
                    if (TryGetProperty(agent, "agentId", out var id)
                        && string.Equals(id.GetString(), "commander", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryGetProperty(agent, "data", out var data) && data.ValueKind == JsonValueKind.Object)
                            commander = data;
                        break;
                    }
                }
            }

            if (commander == null && TryGetProperty(root, "agent", out var agentProp)
                && agentProp.GetString() == "commander")
            {
                commander = root;
            }

            if (commander == null) return null;

            var cmd = commander.Value;
            var direction = ReadString(cmd, "directional_bias")
                ?? ExtractDirection(ReadString(cmd, "summary"));
            if (string.IsNullOrEmpty(direction)) return null;

            var confidence = ReadDecimal(cmd, "confidence_score") ?? 50m;

            var targetPrice = ReadDecimal(cmd, "targetPrice")
                ?? ReadNestedDecimal(cmd, "exit_plan", "take_profit")
                ?? ReadNestedDecimal(cmd, "metrics", "targetPrice");

            var stopLoss = ReadDecimal(cmd, "stopLossPrice")
                ?? ReadNestedDecimal(cmd, "exit_plan", "stop_loss")
                ?? ReadNestedDecimal(cmd, "metrics", "stopLossPrice");

            return new AnalysisSignal(direction, confidence, targetPrice, stopLoss);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractDirection(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return null;
        if (summary.Contains("看多", StringComparison.OrdinalIgnoreCase) || summary.Contains("偏多", StringComparison.OrdinalIgnoreCase))
            return "偏多";
        if (summary.Contains("看空", StringComparison.OrdinalIgnoreCase) || summary.Contains("偏空", StringComparison.OrdinalIgnoreCase))
            return "偏空";
        if (summary.Contains("中性", StringComparison.OrdinalIgnoreCase) || summary.Contains("观察", StringComparison.OrdinalIgnoreCase))
            return "中性";
        return null;
    }

    internal static bool JudgeCorrectness(string direction, decimal actualReturn)
    {
        return direction switch
        {
            "看多" or "偏多" => actualReturn > 0,
            "看空" or "偏空" => actualReturn < 0,
            "中性" or "观察" => Math.Abs(actualReturn) <= 2m,
            _ => false
        };
    }

    // ── JSON helpers (mirroring ReplayCalibrationService pattern) ──

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        value = default;
        if (root.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        return false;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return TryGetProperty(root, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static decimal? ReadDecimal(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out number)) return number;
        return null;
    }

    private static decimal? ReadNestedDecimal(JsonElement root, string objectName, string propertyName)
    {
        return TryGetProperty(root, objectName, out var obj) && obj.ValueKind == JsonValueKind.Object
            ? ReadDecimal(obj, propertyName)
            : null;
    }

    private async Task<BacktestResult> SaveResult(
        StockAgentAnalysisHistory history,
        BacktestResult? existing,
        AnalysisSignal? signal,
        string status,
        CancellationToken ct)
    {
        var result = existing ?? new BacktestResult
        {
            AnalysisHistoryId = history.Id,
            Symbol = history.Symbol,
            Name = history.Name,
            CreatedAt = DateTime.UtcNow
        };

        result.AnalysisDate = DateOnly.FromDateTime(history.CreatedAt);
        result.PredictedDirection = signal?.Direction ?? string.Empty;
        result.Confidence = signal != null ? (int)signal.Confidence : 0;
        result.TargetPrice = signal?.TargetPrice;
        result.StopLoss = signal?.StopLoss;
        result.CalcStatus = status;
        result.UpdatedAt = DateTime.UtcNow;

        if (existing == null) _dbContext.BacktestResults.Add(result);
        await _dbContext.SaveChangesAsync(ct);
        return result;
    }
}
