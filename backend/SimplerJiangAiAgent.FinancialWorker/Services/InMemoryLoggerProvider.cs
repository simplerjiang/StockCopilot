using Microsoft.Extensions.Logging;

namespace SimplerJiangAiAgent.FinancialWorker.Services;

public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly InMemoryLogStore _store;

    // 只捕获这些类别的日志（避免框架噪音）
    private static readonly HashSet<string> IgnoredPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.AspNetCore",
        "Microsoft.Hosting",
        "Microsoft.Extensions",
        "System.Net.Http"
    };

    public InMemoryLoggerProvider(InMemoryLogStore store)
    {
        _store = store;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new InMemoryLogger(_store, categoryName);
    }

    public void Dispose() { }

    private sealed class InMemoryLogger : ILogger
    {
        private readonly InMemoryLogStore _store;
        private readonly string _category;

        public InMemoryLogger(InMemoryLogStore store, string category)
        {
            _store = store;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            // 过滤框架噪音
            foreach (var prefix in IgnoredPrefixes)
            {
                if (_category.StartsWith(prefix))
                    return;
            }

            var message = formatter(state, exception);
            if (exception != null)
                message += $"\n{exception}";

            var levelStr = logLevel switch
            {
                LogLevel.Information => "INFO",
                LogLevel.Warning => "WARN",
                LogLevel.Error => "ERROR",
                LogLevel.Critical => "CRIT",
                _ => logLevel.ToString().ToUpperInvariant()
            };

            // 简化 category 名 — 只取最后一段
            var shortCategory = _category.Contains('.')
                ? _category[(_category.LastIndexOf('.') + 1)..]
                : _category;

            _store.Add(levelStr, shortCategory, message);
        }
    }
}
