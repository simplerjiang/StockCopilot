using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

public enum LocalFactMarketPendingMode
{
    Default = 0,
    RequestPath = 1
}

public interface ILocalFactAiEnrichmentService
{
    Task ProcessMarketPendingAsync(
        CancellationToken cancellationToken = default,
        LocalFactMarketPendingMode mode = LocalFactMarketPendingMode.Default);
    Task ProcessSymbolPendingAsync(string symbol, CancellationToken cancellationToken = default);
    Task<LocalFactPendingProcessSummary> ProcessPendingBatchAsync(CancellationToken cancellationToken = default);
    Task<LocalFactPendingCounts> GetPendingCountsAsync(CancellationToken cancellationToken = default);
}

public sealed record LocalFactPendingCounts(int Market, int Sector, int Stock)
{
    public int Total => Market + Sector + Stock;
}

public sealed record LocalFactPendingContinuation(
    bool MayContinueAutomatically,
    string ReasonCode);

public sealed record LocalFactArchiveJobEvent(
    DateTimeOffset Timestamp,
    string Level,
    string Type,
    string Message,
    string? Details = null,
    int? Round = null,
    int? Retry = null);

public sealed record LocalFactPendingProcessSummary(
    LocalFactPendingCounts Processed,
    LocalFactPendingCounts Remaining,
    bool Completed,
    string? StopReason,
    LocalFactPendingContinuation? Continuation = null)
{
    public IReadOnlyList<LocalFactArchiveJobEvent> Events { get; init; } = [];
}

public interface ILocalFactArchiveJobCoordinator
{
    LocalFactArchiveJobStatus GetStatus();
    Task<LocalFactArchiveJobStatus> StartOrResumeAsync(CancellationToken cancellationToken = default);
    Task<LocalFactArchiveJobStatus> PauseAsync(CancellationToken cancellationToken = default);
    Task<LocalFactArchiveJobStatus> RestartAsync(CancellationToken cancellationToken = default);
}

public sealed record LocalFactArchiveJobStatus(
    int RunId,
    string State,
    bool IsRunning,
    bool Completed,
    bool RequiresManualResume,
    int Rounds,
    LocalFactPendingCounts Processed,
    LocalFactPendingCounts Remaining,
    string? StopReason,
    string? Message,
    LocalFactPendingContinuation? Continuation = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? UpdatedAt = null,
    DateTimeOffset? FinishedAt = null)
{
    public string? AttentionMessage { get; init; }
    public int ConsecutiveRecoverableFailures { get; init; }
    public int MaxRecoverableFailures { get; init; } = 3;
    public IReadOnlyList<LocalFactArchiveJobEvent> RecentEvents { get; init; } = [];
}

public sealed class LocalFactArchiveJobCoordinator : ILocalFactArchiveJobCoordinator
{
    private static readonly LocalFactPendingCounts EmptyCounts = new(0, 0, 0);
    private static readonly TimeSpan[] RecoverableRetryDelays =
    [
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2)
    ];
    private const int MaxRecoverableFailures = 3;
    private const int MaxRecentEvents = 16;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LocalFactArchiveJobCoordinator> _logger;
    private readonly object _sync = new();
    private LocalFactArchiveJobStatus _status = CreateIdleStatus();
    private Task? _runner;
    private CancellationTokenSource? _runnerCancellation;
    private CancellationTokenSource? _retryDelayCancellation;
    private int? _pauseRequestedRunId;
    private int _lastRunId;

    public LocalFactArchiveJobCoordinator(
        IServiceScopeFactory scopeFactory,
        ILogger<LocalFactArchiveJobCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public LocalFactArchiveJobStatus GetStatus()
    {
        lock (_sync)
        {
            return _status;
        }
    }

    public Task<LocalFactArchiveJobStatus> StartOrResumeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_runner is { IsCompleted: false })
            {
                var resumed = _status with
                {
                    Message = BuildResumeMessage(_status),
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                _status = resumed;
                return Task.FromResult(resumed);
            }

            _pauseRequestedRunId = null;

            if (CanResumeExistingRun(_status))
            {
                var resumed = _status with
                {
                    State = "running",
                    IsRunning = true,
                    Completed = false,
                    RequiresManualResume = false,
                    StopReason = null,
                    Message = BuildResumeMessage(_status),
                    Continuation = new LocalFactPendingContinuation(true, "manual_resume"),
                    AttentionMessage = null,
                    ConsecutiveRecoverableFailures = 0,
                    MaxRecoverableFailures = MaxRecoverableFailures,
                    RecentEvents = AppendEvent(
                        _status.RecentEvents,
                        CreateEvent("info", "state", "已继续后台清洗任务。", round: _status.Rounds)),
                    UpdatedAt = DateTimeOffset.UtcNow,
                    FinishedAt = null
                };

                _status = resumed;
                StartRunnerLocked(resumed.RunId);
                return Task.FromResult(resumed);
            }

            var now = DateTimeOffset.UtcNow;
            var runId = checked(_lastRunId + 1);
            _lastRunId = runId;
            var started = new LocalFactArchiveJobStatus(
                runId,
                "running",
                true,
                false,
                false,
                0,
                EmptyCounts,
                EmptyCounts,
                null,
                "后台清洗任务已启动，等待首轮结果。",
                null,
                now,
                now,
                null)
            {
                MaxRecoverableFailures = MaxRecoverableFailures,
                RecentEvents =
                [
                    CreateEvent("info", "state", "已启动后台清洗任务。")
                ]
            };

            _status = started;
            StartRunnerLocked(runId);
            return Task.FromResult(started);
        }
    }

    public Task<LocalFactArchiveJobStatus> PauseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CancellationTokenSource? retryDelayCancellation = null;
        LocalFactArchiveJobStatus status;
        lock (_sync)
        {
            if (_runner is not { IsCompleted: false })
            {
                return Task.FromResult(_status);
            }

            _pauseRequestedRunId = _status.RunId;
            retryDelayCancellation = _retryDelayCancellation;
            status = _status with
            {
                Message = "正在暂停后台清洗，等待当前批次完成。",
                AttentionMessage = "已收到暂停请求，当前批次结束后会进入暂停状态。",
                RecentEvents = AppendEvent(
                    _status.RecentEvents,
                    CreateEvent("info", "pause", "已收到暂停请求，等待当前批次完成。", round: Math.Max(_status.Rounds, 1))),
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _status = status;
        }

        try
        {
            retryDelayCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        return Task.FromResult(status);
    }

    public async Task<LocalFactArchiveJobStatus> RestartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LocalFactArchiveJobStatus CreateRestartBlockedStatus()
        {
            var running = _status with
            {
                Message = "后台清洗仍在运行中，请先暂停后再重新开始。",
                AttentionMessage = "如需重新开始，请先暂停当前后台清洗任务。",
                RecentEvents = AppendEvent(
                    _status.RecentEvents,
                    CreateEvent("warning", "restart", "后台清洗仍在运行中，请先暂停后再重新开始。", round: Math.Max(_status.Rounds, 1))),
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _status = running;
            return running;
        }

        lock (_sync)
        {
            if (_runner is { IsCompleted: false })
            {
                return CreateRestartBlockedStatus();
            }
        }

        var remaining = await ReadPendingCountsAsync(cancellationToken);

        lock (_sync)
        {
            if (_runner is { IsCompleted: false })
            {
                return CreateRestartBlockedStatus();
            }

            _pauseRequestedRunId = null;
            var now = DateTimeOffset.UtcNow;
            var runId = checked(_lastRunId + 1);
            _lastRunId = runId;
            var restarted = new LocalFactArchiveJobStatus(
                runId,
                "running",
                true,
                false,
                false,
                0,
                EmptyCounts,
                remaining,
                null,
                "后台清洗任务已重新开始，等待首轮结果。",
                null,
                now,
                now,
                null)
            {
                MaxRecoverableFailures = MaxRecoverableFailures,
                RecentEvents =
                [
                    CreateEvent("info", "restart", "已重新开始后台清洗任务。")
                ]
            };

            _status = restarted;
            StartRunnerLocked(runId);
            return restarted;
        }
    }

    private async Task<LocalFactPendingCounts> ReadPendingCountsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var aiService = scope.ServiceProvider.GetRequiredService<ILocalFactAiEnrichmentService>();
        return await aiService.GetPendingCountsAsync(cancellationToken);
    }

    private async Task RunAsync(int runId, CancellationToken cancellationToken)
    {
        var consecutiveRecoverableFailures = 0;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                LocalFactPendingProcessSummary summary;
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var aiService = scope.ServiceProvider.GetRequiredService<ILocalFactAiEnrichmentService>();
                    summary = await aiService.ProcessPendingBatchAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (IsPauseRequested(runId))
                {
                    _logger.LogInformation("资讯库后台清洗任务已按用户请求暂停");
                    TransitionToPaused(runId, "后台清洗已暂停。", "后台清洗已按用户请求暂停。", Math.Max(_status.Rounds, 1));
                    return;
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogInformation(ex, "资讯库后台清洗任务被取消");
                    TransitionToFailure(runId, "后台清洗任务已取消。", "cancelled");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "资讯库后台清洗任务执行出现可恢复异常，将按策略自动重试");
                    if (!HandleRecoverableException(runId, ex, ref consecutiveRecoverableFailures, out var retryDelay, out var shouldRetry))
                    {
                        return;
                    }

                    if (!shouldRetry)
                    {
                        return;
                    }

                    if (!await DelayBeforeRetryAsync(runId, retryDelay, cancellationToken))
                    {
                        return;
                    }

                    continue;
                }

                if (ShouldRetry(summary))
                {
                    if (!HandleRecoverableSummary(runId, summary, ref consecutiveRecoverableFailures, out var retryDelay, out var shouldRetry))
                    {
                        return;
                    }

                    if (!shouldRetry)
                    {
                        return;
                    }

                    if (!await DelayBeforeRetryAsync(runId, retryDelay, cancellationToken))
                    {
                        return;
                    }

                    continue;
                }

                consecutiveRecoverableFailures = 0;
                if (!ApplyBatchSummary(runId, summary, out var shouldContinue))
                {
                    return;
                }

                if (IsPauseRequested(runId))
                {
                    TransitionToPaused(runId, "后台清洗已暂停。", "当前批次处理完成后已进入暂停状态。", _status.Rounds);
                    return;
                }

                if (!shouldContinue)
                {
                    return;
                }
            }
        }
        finally
        {
            lock (_sync)
            {
                if (_status.RunId == runId)
                {
                    _pauseRequestedRunId = null;
                    _retryDelayCancellation?.Dispose();
                    _retryDelayCancellation = null;
                    _runnerCancellation?.Dispose();
                    _runnerCancellation = null;
                }
            }
        }
    }

    private bool ApplyBatchSummary(int runId, LocalFactPendingProcessSummary summary, out bool shouldContinue)
    {
        lock (_sync)
        {
            shouldContinue = false;
            if (_status.RunId != runId)
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            var rounds = _status.Rounds + 1;
            var processed = AddCounts(_status.Processed, summary.Processed);
            var completed = summary.Completed || summary.Remaining.Total == 0;
            var recentEvents = AppendEvents(_status.RecentEvents, summary.Events);
            shouldContinue = !completed
                && summary.Continuation?.MayContinueAutomatically == true;

            var isBudgetPaused = !completed && !shouldContinue
                && string.Equals(summary.Continuation?.ReasonCode, "round_budget_reached", StringComparison.Ordinal);

            _status = _status with
            {
                State = completed ? "completed" : shouldContinue ? "running" : isBudgetPaused ? "paused" : "failed",
                IsRunning = shouldContinue,
                Completed = completed,
                RequiresManualResume = !completed && !shouldContinue && summary.Remaining.Total > 0,
                Rounds = rounds,
                Processed = processed,
                Remaining = summary.Remaining,
                StopReason = completed || shouldContinue || isBudgetPaused ? null : summary.StopReason,
                Message = BuildStatusMessage(completed, shouldContinue, rounds, processed, summary),
                Continuation = summary.Continuation,
                AttentionMessage = shouldContinue
                    ? summary.StopReason
                    : completed
                        ? null
                        : summary.StopReason,
                ConsecutiveRecoverableFailures = 0,
                MaxRecoverableFailures = MaxRecoverableFailures,
                RecentEvents = AppendEvent(
                    recentEvents,
                    completed
                        ? CreateEvent("info", "progress", $"第 {rounds} 轮完成，后台清洗已完成。", round: rounds)
                        : shouldContinue
                            ? CreateEvent("info", "progress", $"第 {rounds} 轮完成，正在继续下一轮。", details: summary.StopReason, round: rounds)
                            : isBudgetPaused
                                ? CreateEvent("info", "progress", $"第 {rounds} 轮完成，已达到批次上限，可手动继续。", details: summary.StopReason, round: rounds)
                                : CreateEvent("error", "progress", summary.StopReason ?? "后台清洗已停止，仍有待处理资讯。", round: rounds)),
                UpdatedAt = now,
                FinishedAt = shouldContinue ? null : now
            };

            return true;
        }
    }

    private bool HandleRecoverableSummary(
        int runId,
        LocalFactPendingProcessSummary summary,
        ref int consecutiveRecoverableFailures,
        out TimeSpan retryDelay,
        out bool shouldRetry)
    {
        consecutiveRecoverableFailures += 1;
        retryDelay = GetRetryDelay(consecutiveRecoverableFailures);
        shouldRetry = false;

        lock (_sync)
        {
            if (_status.RunId != runId)
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            var rounds = _status.Rounds + 1;
            var processed = AddCounts(_status.Processed, summary.Processed);
            var warningMessage = summary.StopReason ?? "本轮批量清洗未取得进展，剩余待处理项保持未完成状态。";
            var recentEvents = AppendEvents(_status.RecentEvents, summary.Events);

            if (consecutiveRecoverableFailures >= MaxRecoverableFailures)
            {
                var failureMessage = $"后台清洗已连续 {consecutiveRecoverableFailures} 轮未取得进展，待处理资讯已保留，已停止自动重试。";
                _status = _status with
                {
                    State = "failed",
                    IsRunning = false,
                    Completed = false,
                    RequiresManualResume = summary.Remaining.Total > 0,
                    Rounds = rounds,
                    Processed = processed,
                    Remaining = summary.Remaining,
                    StopReason = failureMessage,
                    Message = failureMessage,
                    Continuation = new LocalFactPendingContinuation(false, "retry_exhausted"),
                    AttentionMessage = warningMessage,
                    ConsecutiveRecoverableFailures = consecutiveRecoverableFailures,
                    MaxRecoverableFailures = MaxRecoverableFailures,
                    RecentEvents = AppendEvent(
                        AppendEvent(
                            recentEvents,
                            CreateEvent("warning", "round", warningMessage, round: rounds, retry: consecutiveRecoverableFailures)),
                        CreateEvent("error", "retry", failureMessage, round: rounds, retry: consecutiveRecoverableFailures)),
                    UpdatedAt = now,
                    FinishedAt = now
                };

                return true;
            }

            shouldRetry = true;
            _status = _status with
            {
                State = "running",
                IsRunning = true,
                Completed = false,
                RequiresManualResume = false,
                Rounds = rounds,
                Processed = processed,
                Remaining = summary.Remaining,
                StopReason = null,
                Message = $"后台清洗遇到可恢复问题，待处理资讯已保留，将在 {FormatRetryDelay(retryDelay)}后进行第 {consecutiveRecoverableFailures} 次自动重试。",
                Continuation = new LocalFactPendingContinuation(true, "auto_retry"),
                AttentionMessage = warningMessage,
                ConsecutiveRecoverableFailures = consecutiveRecoverableFailures,
                MaxRecoverableFailures = MaxRecoverableFailures,
                RecentEvents = AppendEvent(
                    AppendEvent(
                        recentEvents,
                        CreateEvent("warning", "round", warningMessage, round: rounds, retry: consecutiveRecoverableFailures)),
                    CreateEvent("info", "retry", $"待处理资讯已保留，计划在 {FormatRetryDelay(retryDelay)}后进行第 {consecutiveRecoverableFailures} 次自动重试。", round: rounds, retry: consecutiveRecoverableFailures)),
                UpdatedAt = now,
                FinishedAt = null
            };

            return true;
        }
    }

    private bool HandleRecoverableException(
        int runId,
        Exception exception,
        ref int consecutiveRecoverableFailures,
        out TimeSpan retryDelay,
        out bool shouldRetry)
    {
        consecutiveRecoverableFailures += 1;
        retryDelay = GetRetryDelay(consecutiveRecoverableFailures);
        shouldRetry = false;

        lock (_sync)
        {
            if (_status.RunId != runId)
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            var rounds = _status.Rounds + 1;
            var warningMessage = $"第 {rounds} 轮后台清洗发生异常：{exception.Message}";
            var recentEvents = AppendEvent(
                _status.RecentEvents,
                CreateEvent("warning", "error", warningMessage, details: exception.Message, round: rounds, retry: consecutiveRecoverableFailures));

            if (consecutiveRecoverableFailures >= MaxRecoverableFailures)
            {
                var failureMessage = $"后台清洗已连续 {consecutiveRecoverableFailures} 次执行异常，待处理资讯已保留，已停止自动重试。";
                _status = _status with
                {
                    State = "failed",
                    IsRunning = false,
                    Completed = false,
                    RequiresManualResume = true,
                    Rounds = rounds,
                    StopReason = failureMessage,
                    Message = failureMessage,
                    Continuation = new LocalFactPendingContinuation(false, "retry_exhausted"),
                    AttentionMessage = warningMessage,
                    ConsecutiveRecoverableFailures = consecutiveRecoverableFailures,
                    MaxRecoverableFailures = MaxRecoverableFailures,
                    RecentEvents = AppendEvent(
                        recentEvents,
                        CreateEvent("error", "retry", failureMessage, round: rounds, retry: consecutiveRecoverableFailures)),
                    UpdatedAt = now,
                    FinishedAt = now
                };

                return true;
            }

            shouldRetry = true;
            _status = _status with
            {
                State = "running",
                IsRunning = true,
                Completed = false,
                RequiresManualResume = false,
                Rounds = rounds,
                StopReason = null,
                Message = $"后台清洗遇到可恢复异常，待处理资讯已保留，将在 {FormatRetryDelay(retryDelay)}后进行第 {consecutiveRecoverableFailures} 次自动重试。",
                Continuation = new LocalFactPendingContinuation(true, "auto_retry"),
                AttentionMessage = warningMessage,
                ConsecutiveRecoverableFailures = consecutiveRecoverableFailures,
                MaxRecoverableFailures = MaxRecoverableFailures,
                RecentEvents = AppendEvent(
                    recentEvents,
                    CreateEvent("info", "retry", $"待处理资讯已保留，计划在 {FormatRetryDelay(retryDelay)}后进行第 {consecutiveRecoverableFailures} 次自动重试。", round: rounds, retry: consecutiveRecoverableFailures)),
                UpdatedAt = now,
                FinishedAt = null
            };

            return true;
        }
    }

    private async Task<bool> DelayBeforeRetryAsync(int runId, TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        CancellationTokenSource retryDelayCancellation;
        lock (_sync)
        {
            if (_status.RunId != runId)
            {
                return false;
            }

            _retryDelayCancellation?.Dispose();
            _retryDelayCancellation = new CancellationTokenSource();
            retryDelayCancellation = _retryDelayCancellation;
        }

        try
        {
            if (IsPauseRequested(runId))
            {
                TransitionToPaused(runId, "后台清洗已暂停。", "已在下一轮开始前暂停后台清洗任务。", Math.Max(_status.Rounds, 1));
                return false;
            }

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, retryDelayCancellation.Token);
            await Task.Delay(retryDelay, linkedCancellation.Token);
            return true;
        }
        catch (OperationCanceledException) when (retryDelayCancellation.IsCancellationRequested && IsPauseRequested(runId))
        {
            _logger.LogInformation("资讯库后台清洗任务在重试等待阶段已按用户请求暂停");
            TransitionToPaused(runId, "后台清洗已暂停。", "已在下一轮开始前暂停后台清洗任务。", Math.Max(_status.Rounds, 1));
            return false;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogInformation(ex, "资讯库后台清洗任务在重试等待阶段被取消");
            TransitionToFailure(runId, "后台清洗任务已取消。", "cancelled");
            return false;
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_retryDelayCancellation, retryDelayCancellation))
                {
                    _retryDelayCancellation = null;
                }
            }

            retryDelayCancellation.Dispose();
        }
    }

    private void TransitionToFailure(int runId, string message, string reasonCode)
    {
        lock (_sync)
        {
            if (_status.RunId != runId)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            _status = _status with
            {
                State = "failed",
                IsRunning = false,
                Completed = false,
                RequiresManualResume = _status.Remaining.Total > 0,
                StopReason = message,
                Message = message,
                Continuation = new LocalFactPendingContinuation(false, reasonCode),
                AttentionMessage = null,
                ConsecutiveRecoverableFailures = 0,
                MaxRecoverableFailures = MaxRecoverableFailures,
                RecentEvents = AppendEvent(
                    _status.RecentEvents,
                    CreateEvent(reasonCode == "cancelled" ? "info" : "error", "state", message, round: Math.Max(_status.Rounds, 1))),
                UpdatedAt = now,
                FinishedAt = now
            };
        }
    }

    private void TransitionToPaused(int runId, string message, string detail, int round)
    {
        lock (_sync)
        {
            if (_status.RunId != runId)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            _status = _status with
            {
                State = "paused",
                IsRunning = false,
                Completed = false,
                RequiresManualResume = true,
                StopReason = null,
                Message = message,
                Continuation = new LocalFactPendingContinuation(false, "paused"),
                AttentionMessage = detail,
                ConsecutiveRecoverableFailures = 0,
                MaxRecoverableFailures = MaxRecoverableFailures,
                RecentEvents = AppendEvent(
                    _status.RecentEvents,
                    CreateEvent("info", "pause", detail, round: round)),
                UpdatedAt = now,
                FinishedAt = now
            };
        }
    }

    private static LocalFactArchiveJobStatus CreateIdleStatus()
    {
        return new LocalFactArchiveJobStatus(
            0,
            "idle",
            false,
            false,
            false,
            0,
            EmptyCounts,
            EmptyCounts,
            null,
            "尚未启动后台清洗任务。",
            null,
            null,
            null,
            null)
        {
            MaxRecoverableFailures = MaxRecoverableFailures
        };
    }

    private static LocalFactPendingCounts AddCounts(LocalFactPendingCounts left, LocalFactPendingCounts right)
    {
        return new LocalFactPendingCounts(
            left.Market + right.Market,
            left.Sector + right.Sector,
            left.Stock + right.Stock);
    }

    private static string BuildResumeMessage(LocalFactArchiveJobStatus status)
    {
        if (string.Equals(status.State, "paused", StringComparison.OrdinalIgnoreCase))
        {
            return "后台清洗已恢复，将从剩余待处理项继续。";
        }

        if (string.Equals(status.State, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return "后台清洗已重新进入运行状态，将继续处理剩余待清洗资讯。";
        }

        if (status.Rounds <= 0)
        {
            return "后台清洗任务已启动，等待首轮结果。";
        }

        return $"后台清洗任务已在运行中，已完成 {status.Rounds} 轮，累计处理 {status.Processed.Total} 条。";
    }

    private static string BuildStatusMessage(
        bool completed,
        bool shouldContinue,
        int rounds,
        LocalFactPendingCounts processed,
        LocalFactPendingProcessSummary summary)
    {
        if (shouldContinue)
        {
            if (rounds <= 0 || processed.Total == 0)
            {
                return "后台清洗任务已启动，等待首轮结果。";
            }

            return $"后台清洗仍在进行中，已完成 {rounds} 轮，累计处理 {processed.Total} 条，正在继续下一批。";
        }

        if (completed)
        {
            return processed.Total == 0
                ? "当前没有待清洗资讯。"
                : $"后台清洗已完成，共处理 {processed.Total} 条。";
        }

        if (!string.IsNullOrWhiteSpace(summary.StopReason))
        {
            return summary.StopReason;
        }

        return processed.Total == 0
            ? "后台清洗未取得进展。"
            : $"后台清洗已暂停，共处理 {processed.Total} 条。";
    }

    private static bool CanResumeExistingRun(LocalFactArchiveJobStatus status)
    {
        return !status.IsRunning
            && (string.Equals(status.State, "paused", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(status.State, "failed", StringComparison.OrdinalIgnoreCase)
                    && status.Remaining.Total > 0));
    }

    private void StartRunnerLocked(int runId)
    {
        _retryDelayCancellation?.Dispose();
        _retryDelayCancellation = null;
        _runnerCancellation?.Dispose();
        _runnerCancellation = new CancellationTokenSource();
        var runnerToken = _runnerCancellation.Token;
        _runner = Task.Run(() => RunAsync(runId, runnerToken));
    }

    private bool IsPauseRequested(int runId)
    {
        lock (_sync)
        {
            return _pauseRequestedRunId == runId;
        }
    }

    private static bool ShouldRetry(LocalFactPendingProcessSummary summary)
    {
        return !summary.Completed
            && summary.Remaining.Total > 0
            && summary.Processed.Total == 0;
    }

    private static TimeSpan GetRetryDelay(int consecutiveRecoverableFailures)
    {
        var index = Math.Clamp(consecutiveRecoverableFailures - 1, 0, RecoverableRetryDelays.Length - 1);
        return RecoverableRetryDelays[index];
    }

    private static string FormatRetryDelay(TimeSpan retryDelay)
    {
        return retryDelay.TotalSeconds >= 1
            ? $"{retryDelay.TotalSeconds:0.#} 秒"
            : $"{retryDelay.TotalMilliseconds:0} 毫秒";
    }

    private static IReadOnlyList<LocalFactArchiveJobEvent> AppendEvent(
        IReadOnlyList<LocalFactArchiveJobEvent>? existing,
        LocalFactArchiveJobEvent entry)
    {
        return AppendEvents(existing, [entry]);
    }

    private static IReadOnlyList<LocalFactArchiveJobEvent> AppendEvents(
        IReadOnlyList<LocalFactArchiveJobEvent>? existing,
        IEnumerable<LocalFactArchiveJobEvent>? additions)
    {
        var merged = (existing ?? [])
            .Concat(additions ?? [])
            .OrderBy(item => item.Timestamp)
            .TakeLast(MaxRecentEvents)
            .ToArray();

        return merged;
    }

    private static LocalFactArchiveJobEvent CreateEvent(
        string level,
        string type,
        string message,
        string? details = null,
        int? round = null,
        int? retry = null)
    {
        return new LocalFactArchiveJobEvent(
            DateTimeOffset.UtcNow,
            level,
            type,
            message,
            details,
            round,
            retry);
    }
}

public sealed class LocalFactAiEnrichmentService : ILocalFactAiEnrichmentService
{
    private const string NeutralSentiment = "中性";
    private const int MarketPendingBatchLimit = 500;
    // Match the live market surface so request paths enrich the rows they can return first.
    private const int MarketRequestPathPendingLimit = 30;
    private const int SymbolPendingBatchLimit = 200;
    private const int MaxArchiveJsonRepairAttempts = 1;
    private const int MaxArchiveEventDetailLength = 600;
    private const double ArchiveBatchTemperature = 0.1;
    private const string GenericArchiveNoProgressStopReason = "本轮批量清洗未取得进展，剩余待处理项保持未完成状态。";
    private const string EmptyArchiveResponseStopReason = "本轮批量清洗未取得进展：模型返回 null 或空内容，待处理资讯已保留。";
    private const string NonArrayArchiveResponseStopReason = "本轮批量清洗未取得进展：模型返回内容不是可解析 JSON 数组，待处理资讯已保留。";
    private const string InvalidArchiveJsonStopReason = "本轮批量清洗未取得进展：模型返回 JSON 解析失败，待处理资讯已保留。";
    private const string EmptyArchiveResponseEventMessage = "模型返回 null 或空内容，已跳过该批次。";
    private const string NonArrayArchiveResponseEventMessage = "模型返回内容不是可解析 JSON 数组，已跳过该批次。";
    private const string InvalidArchiveJsonEventMessage = "模型返回 JSON 解析失败，已跳过该批次。";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim ArchivePendingSweepGate = new(1, 1);
    private readonly AppDbContext _dbContext;
    private readonly ILlmService _llmService;
    private readonly ILlmSettingsStore _settingsStore;
    private readonly StockSyncOptions _options;
    private readonly ILogger<LocalFactAiEnrichmentService> _logger;
    private sealed record AiBatchExecutionOptions(string Provider, string Model, int BatchSize, int MaxConcurrency, string ProviderType);
    private sealed record ArchiveRequestBudget(int TakePerScope, int TotalItemLimit, string LimitDescription);
    private sealed record BatchProcessingResult(LocalFactPendingCounts Processed, IReadOnlyList<LocalFactArchiveJobEvent> Events);
    private sealed record ArchiveBatchParseResult(
        Dictionary<string, NewsEnrichmentResult>? Parsed,
        string RawContent,
        Exception? Error,
        IReadOnlyList<LocalFactArchiveJobEvent> Events);

    public LocalFactAiEnrichmentService(
        AppDbContext dbContext,
        ILlmService llmService,
        ILlmSettingsStore settingsStore,
        IOptions<StockSyncOptions> options,
        ILogger<LocalFactAiEnrichmentService> logger)
    {
        _dbContext = dbContext;
        _llmService = llmService;
        _settingsStore = settingsStore;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProcessMarketPendingAsync(
        CancellationToken cancellationToken = default,
        LocalFactMarketPendingMode mode = LocalFactMarketPendingMode.Default)
    {
        var take = mode == LocalFactMarketPendingMode.RequestPath
            ? MarketRequestPathPendingLimit
            : MarketPendingBatchLimit;
        var pending = await LoadMarketPendingEnvelopesAsync(take, mode, cancellationToken);
        await ProcessBatchesAsync(pending, cancellationToken);
    }

    public async Task ProcessSymbolPendingAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = StockSymbolNormalizer.Normalize(symbol);
        var pending = await LoadSymbolPendingEnvelopesAsync(normalized, SymbolPendingBatchLimit, cancellationToken);
        await ProcessBatchesAsync(pending, cancellationToken);
    }

    public async Task<LocalFactPendingProcessSummary> ProcessPendingBatchAsync(CancellationToken cancellationToken = default)
    {
        var gateHeld = false;
        try
        {
            await ArchivePendingSweepGate.WaitAsync(cancellationToken);
            gateHeld = true;

            var before = await GetPendingCountsAsync(cancellationToken);
            if (before.Total == 0)
            {
                var empty = new LocalFactPendingCounts(0, 0, 0);
                return new LocalFactPendingProcessSummary(
                    empty,
                    empty,
                    true,
                    null,
                    new LocalFactPendingContinuation(false, "completed"));
            }

            var batchOptions = await ResolveBatchExecutionOptionsAsync(cancellationToken);
            var archiveBudget = ResolveArchiveRequestBudget(batchOptions);
            var pending = await LoadArchivePendingEnvelopesAsync(archiveBudget, cancellationToken);
            if (pending.Count == 0)
            {
                return new LocalFactPendingProcessSummary(
                    new LocalFactPendingCounts(0, 0, 0),
                    before,
                    false,
                    "待清洗记录未能进入本轮批次，请稍后重试。",
                    new LocalFactPendingContinuation(false, "batch_selection_empty"));
            }

            var processedResult = await ProcessBatchesAsync(pending, cancellationToken, batchOptions);
            var remaining = await GetPendingCountsAsync(cancellationToken);
            var (stopReason, continuation) = BuildArchiveRoundState(before, processedResult.Processed, remaining, archiveBudget, processedResult.Events);

            return new LocalFactPendingProcessSummary(
                processedResult.Processed,
                remaining,
                remaining.Total == 0,
                stopReason,
                continuation)
            {
                Events = processedResult.Events
            };
        }
        finally
        {
            if (gateHeld)
            {
                ArchivePendingSweepGate.Release();
            }
        }
    }

    private async Task<List<PendingNewsEnvelope>> LoadMarketPendingEnvelopesAsync(
        int take,
        LocalFactMarketPendingMode mode,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.LocalSectorReports
            .Where(item => item.Level == "market" && !item.IsAiProcessed);

        var pending = mode == LocalFactMarketPendingMode.RequestPath
            ? await query
                .OrderByDescending(item => item.CrawledAt)
                .ThenByDescending(item => item.PublishTime)
                .ThenByDescending(item => item.Id)
                .Take(take)
                .ToListAsync(cancellationToken)
            : await query
                .OrderBy(item => item.PublishTime)
                .ThenBy(item => item.Id)
                .Take(take)
                .ToListAsync(cancellationToken);

        return pending.Select(CreateEnvelope).ToList();
    }

    private async Task<List<PendingNewsEnvelope>> LoadSymbolPendingEnvelopesAsync(string normalizedSymbol, int takePerScope, CancellationToken cancellationToken)
    {
        var stockItems = await _dbContext.LocalStockNews
            .Where(item => item.Symbol == normalizedSymbol && !item.IsAiProcessed)
            .OrderBy(item => item.PublishTime)
            .Take(takePerScope)
            .ToListAsync(cancellationToken);

        var sectorItems = await _dbContext.LocalSectorReports
            .Where(item => item.Symbol == normalizedSymbol && item.Level == "sector" && !item.IsAiProcessed)
            .OrderBy(item => item.PublishTime)
            .Take(takePerScope)
            .ToListAsync(cancellationToken);

        return stockItems.Select(CreateEnvelope)
            .Concat(sectorItems.Select(CreateEnvelope))
            .OrderBy(item => item.PublishTime)
            .ToList();
    }

    private async Task<List<PendingNewsEnvelope>> LoadArchivePendingEnvelopesAsync(ArchiveRequestBudget budget, CancellationToken cancellationToken)
    {
        var marketItems = await _dbContext.LocalSectorReports
            .Where(item => item.Level == "market" && !item.IsAiProcessed)
            .OrderBy(item => item.PublishTime)
            .Take(budget.TakePerScope)
            .ToListAsync(cancellationToken);

        var sectorItems = await _dbContext.LocalSectorReports
            .Where(item => item.Level == "sector" && !item.IsAiProcessed)
            .OrderBy(item => item.PublishTime)
            .Take(budget.TakePerScope)
            .ToListAsync(cancellationToken);

        var stockItems = await _dbContext.LocalStockNews
            .Where(item => !item.IsAiProcessed)
            .OrderBy(item => item.PublishTime)
            .Take(budget.TakePerScope)
            .ToListAsync(cancellationToken);

        return SelectArchivePendingEnvelopes(
            [
                marketItems.Select(CreateEnvelope).OrderBy(item => item.PublishTime).ToList(),
                sectorItems.Select(CreateEnvelope).OrderBy(item => item.PublishTime).ToList(),
                stockItems.Select(CreateEnvelope).OrderBy(item => item.PublishTime).ToList()
            ],
            budget.TotalItemLimit);
    }

    public async Task<LocalFactPendingCounts> GetPendingCountsAsync(CancellationToken cancellationToken = default)
    {
        var market = await _dbContext.LocalSectorReports
            .CountAsync(item => item.Level == "market" && !item.IsAiProcessed, cancellationToken);
        var sector = await _dbContext.LocalSectorReports
            .CountAsync(item => item.Level == "sector" && !item.IsAiProcessed, cancellationToken);
        var stock = await _dbContext.LocalStockNews
            .CountAsync(item => !item.IsAiProcessed, cancellationToken);

        return new LocalFactPendingCounts(market, sector, stock);
    }

    private static (string? StopReason, LocalFactPendingContinuation Continuation) BuildArchiveRoundState(
        LocalFactPendingCounts before,
        LocalFactPendingCounts processed,
        LocalFactPendingCounts remaining,
        ArchiveRequestBudget budget,
        IReadOnlyList<LocalFactArchiveJobEvent>? events)
    {
        if (remaining.Total == 0)
        {
            return (null, new LocalFactPendingContinuation(false, "completed"));
        }

        if (processed.Total == 0)
        {
            return (
                BuildArchiveNoProgressStopReason(events),
                new LocalFactPendingContinuation(false, "no_progress"));
        }

        var roundLimitReached = before.Total > budget.TotalItemLimit
            || before.Market > budget.TakePerScope
            || before.Sector > budget.TakePerScope
            || before.Stock > budget.TakePerScope;

        if (roundLimitReached)
        {
            return (
                $"本轮已达到单次清洗上限（{budget.LimitDescription}），已保存部分结果。",
                new LocalFactPendingContinuation(false, "round_budget_reached"));
        }

        return (
            "本轮已保存部分结果，仍有待处理资讯。",
            new LocalFactPendingContinuation(true, "remaining_pending"));
    }

    private static string BuildArchiveNoProgressStopReason(IReadOnlyList<LocalFactArchiveJobEvent>? events)
    {
        var parseEvent = (events ?? [])
            .LastOrDefault(item => string.Equals(item.Type, "parse", StringComparison.OrdinalIgnoreCase));

        if (parseEvent is null)
        {
            return GenericArchiveNoProgressStopReason;
        }

        if (parseEvent.Message.Contains(EmptyArchiveResponseEventMessage, StringComparison.Ordinal))
        {
            return EmptyArchiveResponseStopReason;
        }

        if (parseEvent.Message.Contains(NonArrayArchiveResponseEventMessage, StringComparison.Ordinal))
        {
            return NonArrayArchiveResponseStopReason;
        }

        if (parseEvent.Message.Contains(InvalidArchiveJsonEventMessage, StringComparison.Ordinal))
        {
            return InvalidArchiveJsonStopReason;
        }

        return GenericArchiveNoProgressStopReason;
    }

    private static ArchiveRequestBudget ResolveArchiveRequestBudget(AiBatchExecutionOptions batchOptions)
    {
        if (!string.Equals(batchOptions.ProviderType, "ollama", StringComparison.OrdinalIgnoreCase))
        {
            return new ArchiveRequestBudget(
                batchOptions.BatchSize,
                batchOptions.BatchSize * 3,
                $"每个层级最多 {batchOptions.BatchSize} 条");
        }

        return new ArchiveRequestBudget(
            batchOptions.BatchSize,
            batchOptions.BatchSize,
            $"最多 {batchOptions.BatchSize} 条");
    }

    private static List<PendingNewsEnvelope> SelectArchivePendingEnvelopes(
        IReadOnlyList<IReadOnlyList<PendingNewsEnvelope>> scopedPending,
        int totalItemLimit)
    {
        if (totalItemLimit <= 0)
        {
            return [];
        }

        var queues = scopedPending
            .Select(scope => new Queue<PendingNewsEnvelope>(scope.OrderBy(item => item.PublishTime)))
            .ToArray();
        var selected = new List<PendingNewsEnvelope>(totalItemLimit);

        while (selected.Count < totalItemLimit)
        {
            var heads = queues
                .Where(queue => queue.Count > 0)
                .Select(queue => queue.Peek())
                .OrderBy(item => item.PublishTime)
                .ToList();

            if (heads.Count == 0)
            {
                break;
            }

            var tookAny = false;
            foreach (var head in heads)
            {
                if (selected.Count >= totalItemLimit)
                {
                    break;
                }

                foreach (var queue in queues)
                {
                    if (queue.Count == 0)
                    {
                        continue;
                    }

                    if (!string.Equals(queue.Peek().Id, head.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    selected.Add(queue.Dequeue());
                    tookAny = true;
                    break;
                }
            }

            if (!tookAny)
            {
                break;
            }
        }

        return selected
            .OrderBy(item => item.PublishTime)
            .ToList();
    }

    private static PendingNewsEnvelope CreateEnvelope(LocalStockNews item)
    {
        return new PendingNewsEnvelope(
            $"stock:{item.Id}",
            item.Title,
            item.Source,
            item.SourceTag,
            "stock",
            item.Category,
            item.Name,
            item.Symbol,
            item.SectorName,
            item.ArticleExcerpt,
            item.ArticleSummary,
            item.PublishTime,
            Apply: result => Apply(item, result));
    }

    private static PendingNewsEnvelope CreateEnvelope(LocalSectorReport item)
    {
        return new PendingNewsEnvelope(
            $"{item.Level}:{item.Id}",
            item.Title,
            item.Source,
            item.SourceTag,
            item.Level,
            null,
            null,
            item.Symbol,
            item.SectorName,
            item.ArticleExcerpt,
            item.ArticleSummary,
            item.PublishTime,
            Apply: result => Apply(item, result));
    }

    private async Task<BatchProcessingResult> ProcessBatchesAsync(
        IReadOnlyList<PendingNewsEnvelope> pending,
        CancellationToken cancellationToken,
        AiBatchExecutionOptions? executionOptions = null)
    {
        if (pending.Count == 0)
        {
            return new BatchProcessingResult(new LocalFactPendingCounts(0, 0, 0), []);
        }

        var batchOptions = executionOptions ?? await ResolveBatchExecutionOptionsAsync(cancellationToken);
        var aiProvider = batchOptions.Provider;
        var aiModel = batchOptions.Model;
        var batchSize = batchOptions.BatchSize;
        var maxConcurrency = batchOptions.MaxConcurrency;

        // Build all batches upfront
        var batches = new List<PendingNewsEnvelope[]>();
        for (var index = 0; index < pending.Count; index += batchSize)
            batches.Add(pending.Skip(index).Take(batchSize).ToArray());

        // Fire LLM calls in parallel with concurrency limit; cancel remaining on rate limit
        using var semaphore = new SemaphoreSlim(maxConcurrency);
        using var rateLimitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = batches.Select(async (batch, batchIndex) =>
        {
            await semaphore.WaitAsync(rateLimitCts.Token);
            try
            {
                var prompt = BuildPrompt(batch);
                var requestEvent = CreateArchiveJobEvent(
                    "info",
                    "request",
                    $"发送第 {batchIndex + 1} / {batches.Count} 批清洗请求，共 {batch.Length} 条。",
                    BuildRequestPreview(aiProvider, aiModel, batch));
                var result = await _llmService.ChatAsync(
                    aiProvider,
                    CreateArchiveLlmRequest(prompt, aiModel, ArchiveBatchTemperature),
                    rateLimitCts.Token);
                return (
                    batch,
                    content: result.Content,
                    error: (Exception?)null,
                    events: new[]
                    {
                        requestEvent,
                        CreateArchiveJobEvent(
                            "info",
                            "response",
                            IsNullOrEmptyModelResponse(result.Content)
                                ? $"第 {batchIndex + 1} / {batches.Count} 批收到空响应。"
                                : $"第 {batchIndex + 1} / {batches.Count} 批已收到模型响应。",
                            BuildArchiveResponseDetails(result.Content))
                    }.AsEnumerable());
            }
            catch (OperationCanceledException) when (rateLimitCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // Aborted due to rate limit in a sibling task — not an error
                return (
                    batch,
                    content: (string?)null,
                    error: (Exception?)null,
                    events: new[]
                    {
                        CreateArchiveJobEvent(
                            "warning",
                            "response",
                            $"第 {batchIndex + 1} / {batches.Count} 批因同轮限流取消，等待后续重试。")
                    }.AsEnumerable());
            }
            catch (Exception ex)
            {
                if (IsRateLimit(ex))
                {
                    // Signal other tasks to stop
                    try { rateLimitCts.Cancel(); } catch (ObjectDisposedException) { }
                }
                return (
                    batch,
                    content: (string?)null,
                    error: (Exception?)ex,
                    events: new[]
                    {
                        CreateArchiveJobEvent(
                            IsRateLimit(ex) ? "warning" : "error",
                            "error",
                            $"第 {batchIndex + 1} / {batches.Count} 批清洗失败：{ex.Message}",
                            ex.Message)
                    }.AsEnumerable());
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);

        // Apply results and save sequentially (DbContext is not thread-safe)
        var appliedMarket = 0;
        var appliedSector = 0;
        var appliedStock = 0;
        var events = new List<LocalFactArchiveJobEvent>();
        for (var batchIndex = 0; batchIndex < results.Length; batchIndex++)
        {
            var (batch, content, error, batchEvents) = results[batchIndex];
            events.AddRange(batchEvents);

            if (error != null)
            {
                _logger.LogWarning(error, "本地事实 AI 清洗失败，保留未处理状态以便下轮重试");
                continue;
            }

            if (content is null) continue;

            if (IsNullOrEmptyModelResponse(content))
            {
                _logger.LogWarning("本地事实 AI 清洗返回空内容，跳过该批次");
                events.Add(CreateArchiveJobEvent(
                    "warning",
                    "parse",
                    EmptyArchiveResponseEventMessage,
                    BuildArchiveResponseDetails(content)));
                continue;
            }

            var parseResult = await ParseBatchResultWithRepairAsync(
                batch,
                aiProvider,
                aiModel,
                content,
                batchIndex + 1,
                results.Length,
                cancellationToken);

            events.AddRange(parseResult.Events);
            if (parseResult.Error is not null)
            {
                _logger.LogWarning(parseResult.Error, "本地事实 AI 清洗结果解析失败，跳过该批次");
                events.Add(CreateArchiveJobEvent(
                    "warning",
                    "parse",
                    BuildArchiveParseEventMessage(parseResult.Error, parseResult.RawContent),
                    BuildArchiveResponseDetails(parseResult.RawContent)));
                continue;
            }

            foreach (var item in batch)
            {
                if (parseResult.Parsed is not null
                    && parseResult.Parsed.TryGetValue(item.Id, out var enrichment))
                {
                    item.Apply(enrichment);
                    IncrementProcessedScope(item.Scope, ref appliedMarket, ref appliedSector, ref appliedStock);
                }
            }
        }

        if (appliedMarket + appliedSector + appliedStock > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return new BatchProcessingResult(
            new LocalFactPendingCounts(appliedMarket, appliedSector, appliedStock),
            events
                .OrderBy(item => item.Timestamp)
                .TakeLast(8)
                .ToArray());
    }

    private async Task<AiBatchExecutionOptions> ResolveBatchExecutionOptionsAsync(CancellationToken cancellationToken)
    {
        var (cleansingProvider, cleansingModel, cleansingBatchSize) = await _settingsStore.GetNewsCleansingSettingsAsync(cancellationToken);
        var aiProvider = string.IsNullOrWhiteSpace(cleansingProvider) || cleansingProvider == "active"
            ? _options.AiProvider
            : cleansingProvider;
        var aiBatchSize = cleansingBatchSize > 0 ? cleansingBatchSize : _options.AiBatchSize;

        var resolvedProviderKey = await _settingsStore.ResolveProviderKeyAsync(aiProvider, cancellationToken);
        var providerSettings = await _settingsStore.GetProviderAsync(resolvedProviderKey, cancellationToken);
        var isOllamaProvider = string.Equals(providerSettings?.ProviderType, "ollama", StringComparison.OrdinalIgnoreCase)
            || string.Equals(resolvedProviderKey, "ollama", StringComparison.OrdinalIgnoreCase)
            || string.Equals(aiProvider, "ollama", StringComparison.OrdinalIgnoreCase);
        var providerType = isOllamaProvider ? "ollama" : (providerSettings?.ProviderType ?? "openai");
        var batchSize = Math.Clamp(aiBatchSize, 5, 20);
        var aiModel = string.IsNullOrWhiteSpace(cleansingModel)
            ? (string.IsNullOrWhiteSpace(providerSettings?.Model) ? _options.AiModel : providerSettings.Model)
            : cleansingModel;
        var maxConcurrency = isOllamaProvider ? 1 : 3;

        return new AiBatchExecutionOptions(aiProvider, aiModel, batchSize, maxConcurrency, providerType);
    }

    private static string BuildPrompt(IReadOnlyList<PendingNewsEnvelope> batch)
    {
        var payload = JsonSerializer.Serialize(batch.Select(item => new
        {
            id = item.Id,
            title = item.Title,
            source = item.Source,
            sourceTag = item.SourceTag,
            scope = item.Scope,
            category = item.Category,
            name = item.Name,
            symbol = item.Symbol,
            sectorName = item.SectorName,
            articleSummary = TruncatePromptText(item.ArticleSummary),
            articleExcerpt = TruncatePromptText(item.ArticleExcerpt),
            publishedAt = item.PublishTime
        }), JsonOptions);

        return "你是财经资讯清洗器，只返回 JSON 数组，不要 Markdown，不要解释。" +
             "\n只允许输出一个 JSON 数组，缺失值请使用 null 或空数组。" +
             "\n每个元素必须包含 id, translatedTitle, aiSentiment, aiTarget, aiTags。" +
             "\naiSentiment 只能是 利好 / 中性 / 利空。" +
             "\naiTarget 必须来自标题、摘要、正文中明确出现的实体，或来自输入 name / symbol 对应的关联股票名称；不得凭行业联想生成。" +
             "\n当 name 非空时，aiTarget 优先使用该关联公司名；不明确时填 无明确标的。" +
             "\naiTags 必须是数组，只能从以下标签中选择 0 到 2 个：紧急消息、突发事件、宏观货币、地缘政治、行业周期、行业预期、政策红利、财报业绩、经营数据、资金面、监管政策、海外映射、商品价格、风险预警。不得把其它股票或公司名写入 aiTags。" +
             "\n若原文已是清晰中文，translatedTitle 返回 null；不要编造事实，不要输出数组外文字。" +
             "\n输入：\n" + payload;
    }

    private static string? TruncatePromptText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 360 ? trimmed : trimmed[..360];
    }

    private static LlmChatRequest CreateArchiveLlmRequest(string prompt, string model, double temperature)
    {
        return new LlmChatRequest(
            prompt,
            model,
            temperature,
            false,
            ResponseFormat: LlmResponseFormats.Json);
    }

    private async Task<ArchiveBatchParseResult> ParseBatchResultWithRepairAsync(
        IReadOnlyList<PendingNewsEnvelope> batch,
        string provider,
        string model,
        string content,
        int batchNumber,
        int totalBatches,
        CancellationToken cancellationToken)
    {
        try
        {
            return new ArchiveBatchParseResult(ParseBatchResult(content), content, null, []);
        }
        catch (Exception ex) when (ShouldAttemptArchiveJsonRepair(content, ex))
        {
            var currentRawContent = content;
            var repairEvents = new List<LocalFactArchiveJobEvent>();
            var originalPrompt = BuildPrompt(batch);

            for (var attempt = 1; attempt <= MaxArchiveJsonRepairAttempts; attempt++)
            {
                repairEvents.Add(CreateArchiveJobEvent(
                    "info",
                    "request",
                    $"第 {batchNumber} / {totalBatches} 批触发单次 JSON repair。",
                    BuildRequestPreview(provider, model, batch)));

                try
                {
                    var repairPrompt = BuildArchiveBatchRepairPrompt(originalPrompt, currentRawContent, ex.Message);
                    var repairResult = await _llmService.ChatAsync(
                        provider,
                        CreateArchiveLlmRequest(repairPrompt, model, ArchiveBatchTemperature),
                        cancellationToken);

                    currentRawContent = repairResult.Content?.Trim() ?? string.Empty;
                    repairEvents.Add(CreateArchiveJobEvent(
                        "info",
                        "response",
                        IsNullOrEmptyModelResponse(currentRawContent)
                            ? $"第 {batchNumber} / {totalBatches} 批 JSON repair 收到空响应。"
                            : $"第 {batchNumber} / {totalBatches} 批 JSON repair 已收到模型响应。",
                        BuildArchiveResponseDetails(currentRawContent)));

                    try
                    {
                        return new ArchiveBatchParseResult(ParseBatchResult(currentRawContent), currentRawContent, null, repairEvents);
                    }
                    catch (Exception repairEx)
                    {
                        ex = repairEx;
                    }
                }
                catch (Exception repairCallEx)
                {
                    repairEvents.Add(CreateArchiveJobEvent(
                        IsRateLimit(repairCallEx) ? "warning" : "error",
                        "error",
                        $"第 {batchNumber} / {totalBatches} 批 JSON repair 失败：{repairCallEx.Message}",
                        repairCallEx.Message));
                    return new ArchiveBatchParseResult(null, currentRawContent, repairCallEx, repairEvents);
                }
            }

            return new ArchiveBatchParseResult(null, currentRawContent, ex, repairEvents);
        }
        catch (Exception ex)
        {
            return new ArchiveBatchParseResult(null, content, ex, []);
        }
    }

    private static bool ShouldAttemptArchiveJsonRepair(string content, Exception exception)
    {
        if (MaxArchiveJsonRepairAttempts <= 0 || IsNullOrEmptyModelResponse(content) || exception is not JsonException)
        {
            return false;
        }

        var parseEventMessage = BuildArchiveParseEventMessage(exception, content);
        return string.Equals(parseEventMessage, NonArrayArchiveResponseEventMessage, StringComparison.Ordinal)
            || string.Equals(parseEventMessage, InvalidArchiveJsonEventMessage, StringComparison.Ordinal);
    }

    private static string BuildArchiveBatchRepairPrompt(string originalPrompt, string rawModelResponse, string parseError)
    {
        return string.Join("\n", [
            "你刚才的输出不是有效 JSON 数组。现在进入唯一一次 JSON repair。",
            "只允许输出一个 JSON 数组，不要任何解释、Markdown、代码块、自然语言或思考过程。",
            "必须保留每个元素的 id, translatedTitle, aiSentiment, aiTarget, aiTags。缺失值请使用 null 或空数组。",
            "aiSentiment 只能是 利好 / 中性 / 利空。",
            $"上一次解析错误：{parseError}",
            string.Empty,
            "请严格按照下面原始任务重新输出唯一的 JSON 数组：",
            originalPrompt,
            string.Empty,
            "上一次无效输出：",
            rawModelResponse
        ]).Trim();
    }

    private static string BuildRequestPreview(string provider, string model, IReadOnlyList<PendingNewsEnvelope> batch)
    {
        var payload = JsonSerializer.Serialize(new
        {
            provider,
            model,
            items = batch.Select(item => new
            {
                id = item.Id,
                scope = item.Scope,
                title = item.Title,
                symbol = item.Symbol,
                sectorName = item.SectorName,
                publishedAt = item.PublishTime
            })
        }, JsonOptions);

        return TruncateArchiveEventDetails(payload) ?? string.Empty;
    }

    private static Dictionary<string, NewsEnrichmentResult> ParseBatchResult(string content)
    {
        var arrayCandidate = ExtractBatchResultArrayCandidate(content, out _);
        if (string.IsNullOrWhiteSpace(arrayCandidate))
        {
            throw new JsonException("未找到批次结果 JSON 数组。");
        }

        if (TryParseBatchResultArray(arrayCandidate, out var result, out var parseError))
        {
            return result;
        }

        if (TrySalvageBatchResultItems(arrayCandidate, out result))
        {
            return result;
        }

        throw parseError ?? new JsonException("批次结果 JSON 数组解析失败。");
    }

    private static bool TryParseBatchResultArray(
        string content,
        out Dictionary<string, NewsEnrichmentResult> result,
        out JsonException? parseError)
    {
        result = new Dictionary<string, NewsEnrichmentResult>(StringComparer.OrdinalIgnoreCase);
        parseError = null;

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                parseError = new JsonException("批次结果 JSON 根节点不是数组。");
                return false;
            }

            result = ReadBatchResultArray(document.RootElement);
            return true;
        }
        catch (JsonException ex)
        {
            parseError = ex;
            return false;
        }
    }

    private static bool TrySalvageBatchResultItems(
        string content,
        out Dictionary<string, NewsEnrichmentResult> result)
    {
        result = new Dictionary<string, NewsEnrichmentResult>(StringComparer.OrdinalIgnoreCase);
        var span = content.AsSpan();
        var arrayStart = content.IndexOf('[');
        if (arrayStart < 0)
        {
            return false;
        }

        var index = arrayStart + 1;
        while (index < span.Length)
        {
            index = FindNextObjectStart(span, index);
            if (index < 0)
            {
                break;
            }

            if (!TryFindBalancedJsonObject(span, index, out var objectEnd))
            {
                break;
            }

            try
            {
                using var document = JsonDocument.Parse(span[index..(objectEnd + 1)].ToString());
                TryAddBatchResultItem(result, document.RootElement);
            }
            catch (JsonException)
            {
            }

            index = objectEnd + 1;
        }

        return result.Count > 0;
    }

    private static Dictionary<string, NewsEnrichmentResult> ReadBatchResultArray(JsonElement arrayElement)
    {
        var result = new Dictionary<string, NewsEnrichmentResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in arrayElement.EnumerateArray())
        {
            TryAddBatchResultItem(result, element);
        }

        return result;
    }

    private static void TryAddBatchResultItem(
        Dictionary<string, NewsEnrichmentResult> result,
        JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var id = GetString(element, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var tags = new List<string>();
        if (element.TryGetProperty("aiTags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in tagsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    tags.Add(value.Trim());
                }
            }
        }

        result[id] = new NewsEnrichmentResult(
            GetString(element, "translatedTitle"),
            NormalizeSentiment(GetString(element, "aiSentiment")),
            NormalizeText(GetString(element, "aiTarget")),
            tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            true);
    }

    private static string? ExtractBatchResultArrayCandidate(string content, out bool candidateIsComplete)
    {
        candidateIsComplete = false;
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var span = content.AsSpan();
        var inString = false;
        var escape = false;
        var objectDepth = 0;

        for (var index = 0; index < span.Length; index++)
        {
            var ch = span[index];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escape = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                objectDepth += 1;
                continue;
            }

            if (ch == '}')
            {
                objectDepth = Math.Max(0, objectDepth - 1);
                continue;
            }

            if (ch != '[' || objectDepth > 0)
            {
                continue;
            }

            var nextIndex = SkipWhitespace(span, index + 1);
            if (nextIndex >= span.Length)
            {
                return null;
            }

            if (span[nextIndex] != '{' && span[nextIndex] != ']')
            {
                continue;
            }

            if (TryFindBalancedJsonArray(span, index, out var arrayEnd))
            {
                candidateIsComplete = true;
                return span[index..(arrayEnd + 1)].ToString();
            }

            return span[index..].ToString().Trim();
        }

        return null;
    }

    private static bool IsNullOrEmptyModelResponse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return true;
        }

        return string.Equals(content.Trim(), "null", StringComparison.OrdinalIgnoreCase);
    }

    private static string? BuildArchiveResponseDetails(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "原始返回为空。";
        }

        return content;
    }

    private static string BuildArchiveParseEventMessage(Exception exception, string? content)
    {
        if (IsNullOrEmptyModelResponse(content))
        {
            return EmptyArchiveResponseEventMessage;
        }

        if (exception is JsonException jsonException)
        {
            if (jsonException.Message.Contains("未找到批次结果 JSON 数组。", StringComparison.Ordinal)
                || jsonException.Message.Contains("批次结果 JSON 根节点不是数组。", StringComparison.Ordinal))
            {
                return NonArrayArchiveResponseEventMessage;
            }

            return InvalidArchiveJsonEventMessage;
        }

        return $"批次结果解析失败：{exception.Message}";
    }

    private static int SkipWhitespace(ReadOnlySpan<char> span, int startIndex)
    {
        var index = startIndex;
        while (index < span.Length && char.IsWhiteSpace(span[index]))
        {
            index += 1;
        }

        return index;
    }

    private static int FindNextObjectStart(ReadOnlySpan<char> span, int startIndex)
    {
        for (var index = startIndex; index < span.Length; index++)
        {
            if (span[index] == '{')
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryFindBalancedJsonArray(ReadOnlySpan<char> span, int startIndex, out int endIndex)
    {
        var inString = false;
        var escape = false;
        var arrayDepth = 0;

        for (var index = startIndex; index < span.Length; index++)
        {
            var ch = span[index];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escape = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '[')
            {
                arrayDepth += 1;
                continue;
            }

            if (ch != ']')
            {
                continue;
            }

            arrayDepth -= 1;
            if (arrayDepth == 0)
            {
                endIndex = index;
                return true;
            }
        }

        endIndex = -1;
        return false;
    }

    private static bool TryFindBalancedJsonObject(ReadOnlySpan<char> span, int startIndex, out int endIndex)
    {
        var inString = false;
        var escape = false;
        var objectDepth = 0;

        for (var index = startIndex; index < span.Length; index++)
        {
            var ch = span[index];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escape = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                objectDepth += 1;
                continue;
            }

            if (ch != '}')
            {
                continue;
            }

            objectDepth -= 1;
            if (objectDepth == 0)
            {
                endIndex = index;
                return true;
            }
        }

        endIndex = -1;
        return false;
    }

    private static void Apply(LocalStockNews entity, NewsEnrichmentResult result)
    {
        entity.TranslatedTitle = NormalizeTranslatedTitle(entity.Title, result.TranslatedTitle);
        entity.AiSentiment = result.AiSentiment;
        entity.AiTarget = LocalFactAiTargetPolicy.SanitizeForStock(entity, result.AiTarget);
        entity.AiTags = SerializeTags(LocalFactAiTargetPolicy.SanitizeTagsForStock(entity, result.AiTags));
        entity.IsAiProcessed = result.IsAiProcessed;
    }

    private static void Apply(LocalSectorReport entity, NewsEnrichmentResult result)
    {
        entity.TranslatedTitle = NormalizeTranslatedTitle(entity.Title, result.TranslatedTitle);
        entity.AiSentiment = result.AiSentiment;
        entity.AiTarget = LocalFactAiTargetPolicy.SanitizeForSectorReport(entity, result.AiTarget);
        entity.AiTags = SerializeTags(LocalFactAiTargetPolicy.SanitizeTagsForSectorReport(entity, result.AiTags));
        entity.IsAiProcessed = result.IsAiProcessed;
    }

    private static string SerializeTags(IReadOnlyList<string> tags)
    {
        return JsonSerializer.Serialize(tags.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), JsonOptions);
    }

    private static string? NormalizeTranslatedTitle(string originalTitle, string? translatedTitle)
    {
        return LocalFactDisplayPolicy.SanitizeTranslatedTitle(originalTitle, translatedTitle);
    }

    private static string NormalizeSentiment(string? value)
    {
        return value is "利好" or "利空" or "中性" ? value : NeutralSentiment;
    }

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static bool IsRateLimit(Exception ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("429", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static void IncrementProcessedScope(string scope, ref int market, ref int sector, ref int stock)
    {
        if (string.Equals(scope, "market", StringComparison.OrdinalIgnoreCase))
        {
            market += 1;
            return;
        }

        if (string.Equals(scope, "sector", StringComparison.OrdinalIgnoreCase))
        {
            sector += 1;
            return;
        }

        if (string.Equals(scope, "stock", StringComparison.OrdinalIgnoreCase))
        {
            stock += 1;
        }
    }

    private static LocalFactArchiveJobEvent CreateArchiveJobEvent(
        string level,
        string type,
        string message,
        string? details = null)
    {
        return new LocalFactArchiveJobEvent(
            DateTimeOffset.UtcNow,
            level,
            type,
            message,
            TruncateArchiveEventDetails(details));
    }

    private static string? TruncateArchiveEventDetails(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= MaxArchiveEventDetailLength)
        {
            return trimmed;
        }

        return trimmed[..MaxArchiveEventDetailLength] + "...(truncated)";
    }

    private sealed record PendingNewsEnvelope(
        string Id,
        string Title,
        string Source,
        string SourceTag,
        string Scope,
        string? Category,
        string? Name,
        string? Symbol,
        string? SectorName,
        string? ArticleExcerpt,
        string? ArticleSummary,
        DateTime PublishTime,
        Action<NewsEnrichmentResult> Apply);

    private sealed record NewsEnrichmentResult(
        string? TranslatedTitle,
        string AiSentiment,
        string? AiTarget,
        IReadOnlyList<string> AiTags,
        bool IsAiProcessed);
}