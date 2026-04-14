using System.Collections.Concurrent;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs.ForumScraping;

public interface IBackfillStatusTracker
{
    bool IsBackfilling(string symbol);
    void SetBackfilling(string symbol, bool isBackfilling);
}

public sealed class BackfillStatusTracker : IBackfillStatusTracker
{
    private readonly ConcurrentDictionary<string, bool> _status = new(StringComparer.OrdinalIgnoreCase);

    public bool IsBackfilling(string symbol) =>
        !string.IsNullOrEmpty(symbol) && _status.TryGetValue(symbol, out var v) && v;

    public void SetBackfilling(string symbol, bool isBackfilling)
    {
        if (string.IsNullOrEmpty(symbol)) return;

        if (isBackfilling)
            _status[symbol] = true;
        else
            _status.TryRemove(symbol, out _);
    }
}
