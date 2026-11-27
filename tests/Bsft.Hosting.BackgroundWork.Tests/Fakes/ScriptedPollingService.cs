using Microsoft.Extensions.Logging;

namespace Bsft.Hosting.BackgroundWork.Tests.Fakes;

/// <summary>
/// A polling service whose passes are scripted by the test: each pass runs the next scripted step, and
/// once the script is used up a pass waits until the host stops.
/// </summary>
internal sealed class ScriptedPollingService : PollingBackgroundServiceBase
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly Queue<Func<CancellationToken, Task<bool>>> _script = new();
    private readonly SemaphoreSlim _tickStarted = new(0);
    private readonly TimeSpan _interval;
    private readonly bool _enabled;
    private readonly Exception? _startingFailure;

    public ScriptedPollingService(
        ILogger logger,
        TimeProvider? timeProvider,
        TimeSpan interval,
        bool enabled = true,
        Exception? startingFailure = null)
        : base(logger, timeProvider)
    {
        _interval = interval;
        _enabled = enabled;
        _startingFailure = startingFailure;
    }

    /// <summary>Gets how many passes have started.</summary>
    public int Ticks { get; private set; }

    /// <summary>Gets how many times the one-time start work ran.</summary>
    public int Startings { get; private set; }

    /// <inheritdoc />
    protected override TimeSpan PollInterval => _interval;

    /// <inheritdoc />
    protected override bool Enabled => _enabled;

    /// <summary>Scripts the next pass to end with the given result.</summary>
    public ScriptedPollingService Then(bool didWork) => Then(_ => Task.FromResult(didWork));

    /// <summary>Scripts the next pass to run the given step.</summary>
    public ScriptedPollingService Then(Func<CancellationToken, Task<bool>> step)
    {
        _script.Enqueue(step);

        return this;
    }

    /// <summary>Waits until one more pass has started than the test has waited for so far.</summary>
    public Task NextTickAsync() => _tickStarted.WaitAsync(Patience);

    /// <inheritdoc />
    protected override Task OnStartingAsync(CancellationToken cancellationToken)
    {
        Startings++;

        return _startingFailure is null ? Task.CompletedTask : Task.FromException(_startingFailure);
    }

    /// <inheritdoc />
    protected override async Task<bool> TryTickAsync(CancellationToken cancellationToken)
    {
        Ticks++;
        _tickStarted.Release();

        if (_script.TryDequeue(out var step))
            return await step(cancellationToken).ConfigureAwait(false);

        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);

        return false;
    }
}
