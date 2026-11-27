namespace Bsft.Idempotency.EFCore.Tests.Support;

/// <summary>
/// A <see cref="TimeProvider"/> test double that always reports a fixed UTC instant.
/// </summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;
}
