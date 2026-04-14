using System.Collections.Concurrent;

namespace SimplerJiangAiAgent.FinancialWorker.Services;

public record RuntimeLogEntry(
    long Id,
    DateTime Timestamp,
    string Level,
    string Category,
    string Message
);

public sealed class InMemoryLogStore
{
    private readonly ConcurrentQueue<RuntimeLogEntry> _entries = new();
    private long _nextId;
    private const int MaxEntries = 1000;

    public void Add(string level, string category, string message)
    {
        var entry = new RuntimeLogEntry(
            Interlocked.Increment(ref _nextId),
            DateTime.UtcNow,
            level,
            category,
            message
        );
        _entries.Enqueue(entry);

        // 超出上限就丢弃旧的
        while (_entries.Count > MaxEntries && _entries.TryDequeue(out _)) { }
    }

    public List<RuntimeLogEntry> GetEntries(long afterId = 0, int count = 200)
    {
        return _entries
            .Where(e => e.Id > afterId)
            .OrderByDescending(e => e.Id)
            .Take(count)
            .OrderBy(e => e.Id)
            .ToList();
    }

    public List<RuntimeLogEntry> GetLatest(int count = 100)
    {
        return _entries
            .OrderByDescending(e => e.Id)
            .Take(count)
            .OrderBy(e => e.Id)
            .ToList();
    }
}
