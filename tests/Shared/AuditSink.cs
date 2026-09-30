using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace RoadOps.Tests.Shared;

/// <summary>Captures the messages written to the RoadOps.Audit log category, in order.</summary>
public sealed class AuditSink : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName == "RoadOps.Audit" ? Messages : null);

    public void Dispose() { }

    private sealed class Logger(ConcurrentQueue<string>? messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => messages is not null;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages?.Enqueue(formatter(state, exception));
    }
}
