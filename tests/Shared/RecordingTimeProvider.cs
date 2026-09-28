namespace Bsft.Tests.Shared;

/// <summary>
/// A time provider whose clock only moves when a test moves it and whose timers fire at once, while
/// recording the delay each of them was asked for.
/// </summary>
/// <remarks>
/// <para>
/// The framework ships no fake time provider and this repository takes no test-only package for one,
/// so the two things a test of a waiting service has to observe are built here: <em>how long</em> a
/// delay was asked for (a retention pacing, a startup stagger and a retry backoff are assertions about
/// exactly that) and that waiting for it costs a test no wall-clock time. Firing immediately rather
/// than on a virtual clock keeps the fake small; nothing under test depends on a timer that does not
/// fire.
/// </para>
/// <para>
/// Linked into each test project that needs it rather than shipped from a package: it is test
/// scaffolding, it belongs to no assembly's closure, and two copies of a fake clock drift apart.
/// </para>
/// </remarks>
internal sealed class RecordingTimeProvider : TimeProvider
{
    private readonly List<TimeSpan> _delays = [];

    public RecordingTimeProvider(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }

    /// <summary>The finite delays that have been asked for, in the order they were asked for.</summary>
    public IReadOnlyList<TimeSpan> Delays
    {
        get
        {
            lock (_delays)
                return _delays.ToList();
        }
    }

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            lock (_delays)
                _delays.Add(dueTime);
        }

        return new ImmediateTimer(callback, state, dueTime);
    }

    /// <summary>A timer that runs its callback as soon as it is given a finite due time.</summary>
    private sealed class ImmediateTimer : ITimer
    {
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private int _fired;

        public ImmediateTimer(TimerCallback callback, object? state, TimeSpan dueTime)
        {
            _callback = callback;
            _state = state;

            Fire(dueTime);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Fire(dueTime);

            return true;
        }

        public void Dispose()
        {
            // Nothing to release: the callback has either run or was never scheduled.
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        /// <summary>Runs the callback once, off the caller's stack so the waiter is wired up first.</summary>
        private void Fire(TimeSpan dueTime)
        {
            if (dueTime == Timeout.InfiniteTimeSpan)
                return;

            if (Interlocked.Exchange(ref _fired, 1) == 0)
                _ = Task.Run(() => _callback(_state));
        }
    }
}
