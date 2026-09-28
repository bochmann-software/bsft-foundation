namespace Bsft.Outbox.Tests.Support;

/// <summary>A clock a test moves by hand, so a lease can be made to expire without waiting for it.</summary>
public sealed class MovableTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    /// <summary>Initializes a new instance of the <see cref="MovableTimeProvider"/> class.</summary>
    /// <param name="utcNow">The instant the clock starts at.</param>
    public MovableTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _utcNow;

    /// <summary>Moves the clock forward.</summary>
    /// <param name="by">How far to move it.</param>
    public void Advance(TimeSpan by) => _utcNow = _utcNow.Add(by);
}
