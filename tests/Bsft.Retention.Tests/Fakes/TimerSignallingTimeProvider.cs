using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;

namespace Bsft.Retention.Tests.Fakes;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that hands a test the due time of every timer created on it, in
/// order, so a test advances the clock only once the code under test has started waiting.
/// </summary>
/// <remarks>
/// Advancing before the sweeper has armed its delay would make the delay start at the advanced time
/// and the assertion race the sweeper; waiting for the timer closes that gap without a real delay.
/// </remarks>
internal sealed class TimerSignallingTimeProvider(DateTimeOffset now) : FakeTimeProvider(now)
{
    private readonly Channel<TimeSpan> _created = Channel.CreateUnbounded<TimeSpan>();

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        _created.Writer.TryWrite(dueTime);

        return timer;
    }

    /// <summary>Waits for the next timer created on this clock.</summary>
    /// <returns>The due time the timer was created with.</returns>
    public async Task<TimeSpan> NextTimerAsync() =>
        await _created.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
}
