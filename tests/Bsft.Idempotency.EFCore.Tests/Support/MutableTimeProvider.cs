namespace Bsft.Idempotency.EFCore.Tests.Support;

/// <summary>
/// A <see cref="TimeProvider"/> test double reporting an instant the test moves by hand, so a lease or
/// a retention can be walked past its own expiry without waiting for it.
/// </summary>
internal sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public MutableTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
