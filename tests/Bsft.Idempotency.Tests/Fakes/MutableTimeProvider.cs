namespace Bsft.Idempotency.Tests.Fakes;

/// <summary>
/// A <see cref="TimeProvider"/> whose instant a test moves by hand, so a lease or a retention can be
/// walked past without waiting for it.
/// </summary>
internal sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public MutableTimeProvider()
        : this(DateTimeOffset.UnixEpoch)
    {
    }

    public MutableTimeProvider(DateTimeOffset now) => _now = now.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
