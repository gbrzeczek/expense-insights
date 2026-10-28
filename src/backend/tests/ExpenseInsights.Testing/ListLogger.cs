using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ExpenseInsights.Testing;

/// <summary>Collects every formatted log message, to assert on what gets logged.</summary>
public sealed class ListLogger : ILoggerProvider, ILoggerFactory
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages;

    public ILogger CreateLogger(string categoryName) => new Logger(_messages, categoryName);

    public ILogger<T> CreateLogger<T>() => new Logger<T>(this);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class Logger(ConcurrentQueue<string> messages, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages.Enqueue($"{category}: {formatter(state, exception)} {exception}");
    }
}
