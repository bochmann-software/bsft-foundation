namespace Bsft.Data.Tests.MultiTenancy.Sandboxes;

/// <summary>A clock that does not move, so a timestamp assertion is an equality.</summary>
internal sealed class FixedClock : TimeProvider
{
    private readonly DateTimeOffset _now;

    /// <summary>Initializes a new instance of the <see cref="FixedClock"/> class.</summary>
    /// <param name="now">What the time always is.</param>
    public FixedClock(DateTimeOffset now)
    {
        _now = now;
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;
}
