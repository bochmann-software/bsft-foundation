namespace Bsft.Tests.Shared;

/// <summary>
/// A time provider whose timestamp moves only when a test calls <see cref="Advance"/>, for code that
/// measures a duration with <see cref="TimeProvider.GetTimestamp"/> and
/// <see cref="TimeProvider.GetElapsedTime(long)"/>.
/// </summary>
/// <remarks>
/// <see cref="ManualTimeProvider"/> leaves the timestamp on the machine's clock on purpose: a launcher
/// that polls with a timestamp would spin forever on a clock nobody moves. A duration test needs the
/// opposite, a measured duration that is exactly what the test advanced, so it is a type of its own.
/// </remarks>
internal sealed class ManualTimestampProvider : TimeProvider
{
    private long _ticks;

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    /// <summary>Moves the timestamp forward.</summary>
    /// <param name="by">How far to move it.</param>
    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
