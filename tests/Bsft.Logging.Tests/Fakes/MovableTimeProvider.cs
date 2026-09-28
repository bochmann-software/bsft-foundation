namespace Bsft.Logging.Tests.Fakes;

/// <summary>
/// A <see cref="TimeProvider"/> that reports the instant a test put it at, so file logging is
/// deterministic and a test can turn the day over without waiting for one.
/// </summary>
internal sealed class MovableTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    /// <summary>Initializes a new instance of the <see cref="MovableTimeProvider"/> class.</summary>
    /// <param name="now">The instant it starts at.</param>
    public MovableTimeProvider(DateTimeOffset now) => _now = now;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock to another instant.</summary>
    /// <param name="now">The instant it reports from now on.</param>
    public void MoveTo(DateTimeOffset now) => _now = now;
}
