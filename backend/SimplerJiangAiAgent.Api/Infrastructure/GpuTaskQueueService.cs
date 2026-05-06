namespace SimplerJiangAiAgent.Api.Infrastructure;

public enum GpuTaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2
}

public enum GpuTaskState
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public interface IGpuLease : IAsyncDisposable
{
    string TaskId { get; }
    CancellationToken CancellationToken { get; }
    void ReportProgress(string? status);
    void MarkFailed();
}

public record GpuQueueSnapshot(
    GpuTaskInfo? CurrentTask,
    IReadOnlyList<GpuTaskInfo> QueuedTasks,
    int HistoryCount,
    bool IsPaused);

public record GpuTaskInfo(
    string TaskId,
    string TaskName,
    GpuTaskPriority Priority,
    GpuTaskState State,
    DateTime EnqueuedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? ProgressStatus,
    TimeSpan? Duration);

public interface IGpuTaskQueue
{
    bool IsPaused { get; }
    void Pause();
    void Resume();
    bool CancelCurrent();
    Task<IGpuLease> AcquireAsync(string taskName, GpuTaskPriority priority, CancellationToken ct = default);
    GpuQueueSnapshot GetSnapshot();
    IReadOnlyList<GpuTaskInfo> GetHistory(int count = 50);
}

public sealed class GpuTaskQueueService : IGpuTaskQueue
{
    private readonly object _lock = new();
    private readonly List<QueueEntry> _queue = new();
    private QueueEntry? _current;
    private bool _paused;
    private readonly List<GpuTaskInfo> _history = new();
    private const int MaxHistory = 200;

    public bool IsPaused
    {
        get { lock (_lock) { return _paused; } }
    }

    public void Pause()
    {
        lock (_lock) { _paused = true; }
    }

    public void Resume()
    {
        lock (_lock) { _paused = false; }
        TryScheduleNext();
    }

    public bool CancelCurrent()
    {
        CancellationTokenSource? cts;
        lock (_lock)
        {
            cts = _current?.Cts;
        }
        if (cts == null) return false;
        try { cts.Cancel(); return true; }
        catch (ObjectDisposedException) { return false; }
    }

    public async Task<IGpuLease> AcquireAsync(string taskName, GpuTaskPriority priority, CancellationToken ct)
    {
        var entry = new QueueEntry(
            Guid.NewGuid().ToString("N")[..12],
            taskName,
            priority,
            DateTime.UtcNow,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        lock (_lock)
        {
            _queue.Add(entry);
            _queue.Sort((a, b) =>
            {
                var p = b.Priority.CompareTo(a.Priority);
                return p != 0 ? p : a.EnqueuedAt.CompareTo(b.EnqueuedAt);
            });
        }

        TryScheduleNext();

        using var reg = ct.Register(() =>
        {
            lock (_lock)
            {
                if (_queue.Remove(entry))
                {
                    entry.Tcs.TrySetCanceled(ct);
                    RecordHistory(entry, GpuTaskState.Cancelled);
                }
            }
        });

        await entry.Tcs.Task;

        return new GpuLease(entry, this);
    }

    public GpuQueueSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            var current = _current != null ? ToInfo(_current, GpuTaskState.Running) : null;
            var queued = _queue.Select(e => ToInfo(e, GpuTaskState.Queued)).ToList();
            return new GpuQueueSnapshot(current, queued, _history.Count, _paused);
        }
    }

    public IReadOnlyList<GpuTaskInfo> GetHistory(int count = 50)
    {
        lock (_lock)
        {
            return _history.TakeLast(Math.Min(count, _history.Count)).Reverse().ToList();
        }
    }

    private void TryScheduleNext()
    {
        lock (_lock)
        {
            if (_paused) return;
            if (_current != null) return;
            if (_queue.Count == 0) return;

            var next = _queue[0];
            _queue.RemoveAt(0);
            _current = next;
            next.Cts = new CancellationTokenSource();
            next.StartedAt = DateTime.UtcNow;
            next.Tcs.TrySetResult();
        }
    }

    internal void ReleaseLease(QueueEntry entry, bool failed)
    {
        CancellationTokenSource? cts = null;
        lock (_lock)
        {
            if (_current == entry)
            {
                cts = entry.Cts;
                entry.Cts = null;
                entry.CompletedAt = DateTime.UtcNow;
                RecordHistory(entry, failed ? GpuTaskState.Failed : GpuTaskState.Completed);
                _current = null;
            }
        }
        cts?.Dispose();
        TryScheduleNext();
    }

    private void RecordHistory(QueueEntry entry, GpuTaskState state)
    {
        // Must be called under _lock
        var info = ToInfo(entry, state);
        _history.Add(info);
        while (_history.Count > MaxHistory)
            _history.RemoveAt(0);
    }

    private static GpuTaskInfo ToInfo(QueueEntry entry, GpuTaskState state)
    {
        TimeSpan? duration = entry.StartedAt.HasValue && entry.CompletedAt.HasValue
            ? entry.CompletedAt.Value - entry.StartedAt.Value
            : entry.StartedAt.HasValue
                ? DateTime.UtcNow - entry.StartedAt.Value
                : null;

        return new GpuTaskInfo(
            entry.TaskId,
            entry.TaskName,
            entry.Priority,
            state,
            entry.EnqueuedAt,
            entry.StartedAt,
            entry.CompletedAt,
            entry.ProgressStatus,
            duration);
    }
}

internal sealed class QueueEntry
{
    public string TaskId { get; }
    public string TaskName { get; }
    public GpuTaskPriority Priority { get; }
    public DateTime EnqueuedAt { get; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ProgressStatus { get; set; }
    public TaskCompletionSource Tcs { get; }
    public CancellationTokenSource? Cts { get; set; }

    public QueueEntry(string taskId, string taskName, GpuTaskPriority priority, DateTime enqueuedAt, TaskCompletionSource tcs)
    {
        TaskId = taskId;
        TaskName = taskName;
        Priority = priority;
        EnqueuedAt = enqueuedAt;
        Tcs = tcs;
    }
}

internal sealed class GpuLease : IGpuLease
{
    private readonly QueueEntry _entry;
    private readonly GpuTaskQueueService _queue;
    private bool _disposed;
    private bool _failed;

    public string TaskId => _entry.TaskId;
    public CancellationToken CancellationToken => _entry.Cts?.Token ?? CancellationToken.None;

    public GpuLease(QueueEntry entry, GpuTaskQueueService queue)
    {
        _entry = entry;
        _queue = queue;
    }

    public void ReportProgress(string? status) => _entry.ProgressStatus = status;

    public void MarkFailed() => _failed = true;

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _queue.ReleaseLease(_entry, _failed);
        }
        return ValueTask.CompletedTask;
    }
}
