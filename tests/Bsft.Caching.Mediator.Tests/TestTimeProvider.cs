namespace Bsft.Caching.Mediator.Tests;

/// <summary>A controllable time source for the expiry-sensitive tests.</summary>
public sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan by) => _utcNow += by;
}
