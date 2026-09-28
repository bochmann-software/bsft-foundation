namespace Bsft.Testing.Time;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock stands still until a test moves it, so that code with
/// durable timers — the saga timeout dispatcher above all — can be driven deterministically instead
/// of waiting for wall-clock time to pass.
/// </summary>
/// <remarks>
/// Only <see cref="GetUtcNow"/> is overridden. Timers created through the base implementation still
/// run on the real system clock, which is deliberate: the components this clock is written for read
/// the current time and never create a timer, and a half-emulated timer queue would hide races
/// rather than expose them.
/// </remarks>
public sealed class TestClock : TimeProvider, ITestClock
{
    private DateTimeOffset _utcNow;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestClock"/> class starting at
    /// <see cref="DateTimeOffset.UnixEpoch"/>.
    /// </summary>
    public TestClock()
        : this(DateTimeOffset.UnixEpoch)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestClock"/> class starting at the given instant.
    /// </summary>
    /// <param name="utcNow">The instant the clock starts at. Converted to UTC.</param>
    public TestClock(DateTimeOffset utcNow)
    {
        _utcNow = utcNow.ToUniversalTime();
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow => _utcNow;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _utcNow;

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="by"/> is negative.</exception>
    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(by), by, "Time can only be advanced forward.");

        _utcNow += by;
    }

    /// <inheritdoc />
    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow.ToUniversalTime();
}
