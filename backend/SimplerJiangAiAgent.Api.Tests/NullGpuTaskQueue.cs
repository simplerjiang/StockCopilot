using SimplerJiangAiAgent.Api.Infrastructure;

namespace SimplerJiangAiAgent.Api.Tests;

internal sealed class NullGpuTaskQueue : IGpuTaskQueue
{
    public static readonly NullGpuTaskQueue Instance = new();

    public bool IsPaused => false;
    public void Pause() { }
    public void Resume() { }
    public bool CancelCurrent() => false;

    public Task<IGpuLease> AcquireAsync(string taskName, GpuTaskPriority priority, CancellationToken ct = default)
        => Task.FromResult<IGpuLease>(new NullLease());

    public GpuQueueSnapshot GetSnapshot()
        => new(null, [], 0, false);

    public IReadOnlyList<GpuTaskInfo> GetHistory(int count = 50)
        => [];

    private sealed class NullLease : IGpuLease
    {
        public string TaskId => "null";
        public CancellationToken CancellationToken => CancellationToken.None;
        public void ReportProgress(string? status) { }
        public void MarkFailed() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
