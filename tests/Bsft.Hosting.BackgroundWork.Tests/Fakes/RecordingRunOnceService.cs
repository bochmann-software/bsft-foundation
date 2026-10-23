using Bsft.Hosting.BackgroundWork;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.BackgroundWork.Tests.Fakes;

/// <summary>
/// A run-once operation whose every attempt is scripted, so a test states the sequence of outcomes
/// and asserts what the base class did with it.
/// </summary>
internal sealed class RecordingRunOnceService : RunOnceWithRetryBackgroundServiceBase
{
    private readonly Queue<Func<CancellationToken, Task>> _attempts = new();

    public RecordingRunOnceService(
        IOptionsMonitor<BsftRunOnceOptions> options,
        TimeProvider timeProvider,
        ILogger<RecordingRunOnceService> logger)
        : base(options, timeProvider, logger)
    {
    }

    /// <summary>How many attempts have been made.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Scripts the next attempt to fail with the given exception.</summary>
    /// <param name="failure">What the attempt throws.</param>
    /// <returns>This service, for chaining.</returns>
    public RecordingRunOnceService ThenFails(Exception failure)
    {
        _attempts.Enqueue(_ => Task.FromException(failure));

        return this;
    }

    /// <summary>Scripts the next attempt to throw before it ever returns a task.</summary>
    /// <param name="failure">What the attempt throws.</param>
    /// <returns>This service, for chaining.</returns>
    public RecordingRunOnceService ThenThrowsSynchronously(Exception failure)
    {
        _attempts.Enqueue(_ => throw failure);

        return this;
    }

    /// <summary>Scripts the next attempt to end cancelled by a token of its own.</summary>
    /// <returns>This service, for chaining.</returns>
    public RecordingRunOnceService ThenCancelsItself()
    {
        _attempts.Enqueue(_ => Task.FromCanceled(new CancellationToken(true)));

        return this;
    }

    /// <summary>Scripts the next attempt to run the given delegate.</summary>
    /// <param name="attempt">What the attempt does.</param>
    /// <returns>This service, for chaining.</returns>
    public RecordingRunOnceService Then(Func<CancellationToken, Task> attempt)
    {
        _attempts.Enqueue(attempt);

        return this;
    }

    /// <inheritdoc />
    protected override Task RunOnceAsync(CancellationToken ct)
    {
        AttemptCount++;

        // An unscripted attempt succeeds, which is what "and then it works" looks like in a test that
        // only cares about how many failures came before it.
        return _attempts.Count == 0 ? Task.CompletedTask : _attempts.Dequeue()(ct);
    }
}
