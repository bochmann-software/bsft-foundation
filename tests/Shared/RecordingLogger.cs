using Microsoft.Extensions.Logging;

namespace Bsft.Tests.Shared;

/// <summary>
/// Keeps what was logged, so a test can assert that a specific log statement was actually reached.
/// </summary>
/// <remarks>
/// Linked into each test project that needs it rather than shipped from a package: it is test scaffolding
/// and nothing a consumer of the framework would ever ask for. A substitute would only prove that some
/// logging extension method was reached, not what the message said.
/// </remarks>
/// <typeparam name="TCategory">The logger's category.</typeparam>
internal sealed class RecordingLogger<TCategory> : ILogger<TCategory>
{
    private readonly List<Entry> _entries = [];

    /// <summary>The entries logged so far, in order, each carrying its level, message and any exception.</summary>
    internal IReadOnlyList<Entry> Entries
    {
        get
        {
            lock (_entries)
                return _entries.ToArray();
        }
    }

    /// <summary>The formatted messages logged so far, in order, at every level.</summary>
    internal IReadOnlyList<string> Messages
    {
        get
        {
            lock (_entries)
                return _entries.Select(entry => entry.Message).ToArray();
        }
    }

    /// <summary>The levels logged so far, in order, so a test can assert how loudly something was reported.</summary>
    internal IReadOnlyList<LogLevel> Levels
    {
        get
        {
            lock (_entries)
                return _entries.Select(entry => entry.Level).ToArray();
        }
    }

    /// <summary>The formatted messages logged at <see cref="LogLevel.Warning"/>, in order.</summary>
    internal IReadOnlyList<string> Warnings
    {
        get
        {
            lock (_entries)
                return _entries.Where(entry => entry.Level == LogLevel.Warning)
                    .Select(entry => entry.Message)
                    .ToArray();
        }
    }

    /// <inheritdoc />
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => NullScope.Instance;

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
        if (formatter is null)
            throw new ArgumentNullException(nameof(formatter));

        lock (_entries)
            _entries.Add(new Entry(logLevel, formatter(state, exception), exception));
    }

    /// <summary>One line the logger kept.</summary>
    /// <param name="Level">The level it was logged at.</param>
    /// <param name="Message">The formatted message.</param>
    /// <param name="Exception">The exception it carried, if any.</param>
    internal sealed record Entry(LogLevel Level, string Message, Exception? Exception);

    /// <summary>A scope that records nothing, because no test asserts on scopes.</summary>
    private sealed class NullScope : IDisposable
    {
        internal static readonly NullScope Instance = new();

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
