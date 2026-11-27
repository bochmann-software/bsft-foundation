using Microsoft.Extensions.Logging;

namespace Bsft.Data.Tests.Fakes;

/// <summary>A logger that keeps the exceptions of the warnings it was given.</summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    public List<Exception?> Warnings { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning)
            Warnings.Add(exception);
    }
}
