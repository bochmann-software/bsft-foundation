using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bsft.Messaging.EFCore.Tests;

/// <summary>
/// A logger that completes a task the first time the thing under test logs at a chosen level, so a test
/// can wait for a background loop to have reached a point instead of guessing at how long that takes.
/// </summary>
/// <remarks>
/// A loop that is stopped by a timer is asserted against whatever it managed in that time, which on a
/// loaded runner is a different amount every run — and where the assertion is that nothing was thrown, a
/// loop that never started passes it. The log line the loop already writes when a pass has finished is a
/// signal it publishes on its own account, so waiting for that instead makes the same test a statement
/// about the pass rather than about the machine.
/// </remarks>
/// <typeparam name="T">The category the logger is for.</typeparam>
internal sealed class SignallingLogger<T> : ILogger<T>
{
    private readonly LogLevel _awaited;
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Initializes a new instance of the <see cref="SignallingLogger{T}"/> class.</summary>
    /// <param name="awaited">The level whose first entry completes <see cref="Reached"/>.</param>
    public SignallingLogger(LogLevel awaited) => _awaited = awaited;

    /// <summary>A task that completes once something has been logged at the awaited level.</summary>
    public Task Reached => _reached.Task;

    /// <inheritdoc />
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull =>
        NullLogger.Instance.BeginScope(state);

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
        if (logLevel == _awaited)
            _reached.TrySetResult();
    }
}
