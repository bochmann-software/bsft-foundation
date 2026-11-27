namespace Bsft.Tests.Shared;

/// <summary>
/// Runs a poll loop on a <see cref="ManualTimeProvider"/> and counts its passes around one interval: after
/// the first pass, one tick before the interval has passed on the clock, and once it has.
/// </summary>
/// <remarks>
/// A loop that waits on the injected clock runs its second pass only when the clock reaches the interval.
/// A loop that waits on the system clock never arms a timer on the manual one, so
/// <see cref="ManualTimeProvider.WaitForTimerAsync"/> gives up and the measurement fails.
/// </remarks>
internal sealed class PollIntervalProbe
{
    private readonly ManualTimeProvider _clock;
    private readonly TimeSpan _interval;

    public PollIntervalProbe(ManualTimeProvider clock, TimeSpan interval)
    {
        _clock = clock;
        _interval = interval;
    }

    /// <summary>Starts the loop, measures its passes around one interval and stops it again.</summary>
    /// <param name="runLoop">Starts the loop under test with the given stopping token.</param>
    /// <param name="passes">Reads how many passes the loop has run so far.</param>
    /// <returns>The pass counts after the first pass, a tick before the interval and after it.</returns>
    public async Task<PollIntervalCounts> MeasureAsync(Func<CancellationToken, Task> runLoop, Func<int> passes)
    {
        using var stopping = new CancellationTokenSource();
        var loop = runLoop(stopping.Token);
        try
        {
            await _clock.WaitForTimerAsync(_interval);
            var first = passes();
            _clock.Advance(_interval - TimeSpan.FromTicks(1));
            var beforeInterval = passes();
            _clock.Advance(TimeSpan.FromTicks(1));
            await _clock.WaitForTimerAsync(_interval);
            return new PollIntervalCounts(first, beforeInterval, passes());
        }
        finally
        {
            await stopping.CancelAsync();
            await loop;
        }
    }
}
