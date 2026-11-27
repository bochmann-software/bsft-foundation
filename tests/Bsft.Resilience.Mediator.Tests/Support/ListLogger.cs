using Microsoft.Extensions.Logging;

namespace Bsft.Resilience.Mediator.Tests.Support;

/// <summary>A test fake that captures all logged entries.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _entries = new();

    /// <summary>Gets the captured log entries.</summary>
    public IReadOnlyList<LogEntry> Entries => _entries.AsReadOnly();

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var formattedMessage = formatter(state, exception);

        var stateList = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];

        _entries.Add(new LogEntry
        {
            LogLevel = logLevel,
            EventId = eventId,
            State = stateList,
            Exception = exception,
            FormattedMessage = formattedMessage,
        });
    }

    /// <summary>Represents a single log entry.</summary>
    internal sealed class LogEntry
    {
        /// <summary>The log level.</summary>
        public required LogLevel LogLevel { get; init; }

        /// <summary>The event ID.</summary>
        public required EventId EventId { get; init; }

        /// <summary>The structured state (key-value pairs).</summary>
        public required IReadOnlyList<KeyValuePair<string, object?>> State { get; init; }

        /// <summary>The exception, if any.</summary>
        public Exception? Exception { get; init; }

        /// <summary>The formatted message.</summary>
        public required string FormattedMessage { get; init; }
    }
}
