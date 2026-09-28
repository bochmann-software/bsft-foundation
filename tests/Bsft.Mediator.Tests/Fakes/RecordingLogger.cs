using Microsoft.Extensions.Logging;

namespace Bsft.Mediator.Tests.Fakes;

/// <summary>A logger that keeps the formatted messages it was handed.</summary>
/// <typeparam name="TCategory">The category the logger is typed to.</typeparam>
public sealed class RecordingLogger<TCategory> : ILogger<TCategory>
{
    /// <summary>Gets the messages logged so far, in order.</summary>
    public List<string> Messages { get; } = [];

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

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
        ArgumentNullException.ThrowIfNull(formatter);

        Messages.Add(formatter(state, exception));
    }
}
