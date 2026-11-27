namespace Bsft.Core.Tests.Support;

/// <summary>
/// A controllable time provider for tests, allowing the clock to be advanced or set to specific times.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private long _ticks;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeTimeProvider"/> class.
    /// </summary>
    /// <param name="initialTime">The initial time for the fake provider.</param>
    public FakeTimeProvider(DateTimeOffset initialTime)
    {
        _ticks = initialTime.UtcTicks;
    }

    /// <summary>Gets the current UTC time.</summary>
    public override DateTimeOffset GetUtcNow()
    {
        return new DateTimeOffset(new DateTime(_ticks, DateTimeKind.Utc));
    }

    /// <summary>Advances the clock by the specified time span.</summary>
    /// <param name="delta">The amount to advance the clock by (can be negative to go backward).</param>
    public void Advance(TimeSpan delta)
    {
        _ticks += delta.Ticks;
    }

    /// <summary>Sets the clock to a specific time.</summary>
    /// <param name="time">The time to set the clock to.</param>
    public void SetTime(DateTimeOffset time)
    {
        _ticks = time.UtcTicks;
    }
}
