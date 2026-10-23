namespace Bsft.Tests.Shared;

/// <summary>
/// A time provider whose clock and timers move only when a test calls <see cref="Advance"/>: a timer
/// fires exactly when the clock reaches its due time, never earlier, never by itself.
/// </summary>
/// <remarks>
/// The framework ships no fake time provider and this repository takes no test-only package for one.
/// <see cref="RecordingTimeProvider"/> fires every timer at once, which cannot show that something
/// happens <em>after</em> an interval and not before it; this one can. A test that has to know the code
/// under test has armed its timer calls <see cref="WaitForTimerAsync"/> instead of sleeping.
/// </remarks>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly List<(DateTimeOffset Due, TaskCompletionSource Signal)> _waiters = [];
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _now;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves the clock forward and runs every timer that falls due, in due order.</summary>
    /// <param name="by">How far to move the clock.</param>
    public void Advance(TimeSpan by)
    {
        DateTimeOffset target;
        lock (_gate)
            target = _now + by;

        while (true)
        {
            ManualTimer? next;
            lock (_gate)
            {
                next = _timers.Where(timer => timer.Due <= target).OrderBy(timer => timer.Due).FirstOrDefault();
                if (next is null)
                {
                    _now = target;
                    return;
                }

                _now = next.Due > _now ? next.Due : _now;
                next.Reschedule(_now);
            }

            next.Run();
        }
    }

    /// <summary>
    /// Waits until a timer is pending that falls due exactly <paramref name="dueIn"/> from now, so a test
    /// advances the clock only once the timer it means has been armed, whatever else was armed before.
    /// </summary>
    /// <param name="dueIn">How far from the current time the awaited timer is due.</param>
    /// <returns>A task that completes when such a timer is pending.</returns>
    public async Task WaitForTimerAsync(TimeSpan dueIn)
    {
        TaskCompletionSource waiter;
        lock (_gate)
        {
            if (_timers.Any(timer => timer.Due == _now + dueIn))
                return;

            waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((_now + dueIn, waiter));
        }

        await waiter.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
        }

        public DateTimeOffset Due { get; private set; } = DateTimeOffset.MaxValue;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_owner._gate)
            {
                _period = period;
                _owner._timers.Remove(this);
                if (dueTime == Timeout.InfiniteTimeSpan)
                {
                    Due = DateTimeOffset.MaxValue;
                    return true;
                }

                Due = _owner._now + dueTime;
                _owner._timers.Add(this);
                foreach (var waiter in _owner._waiters.Where(waiter => waiter.Due == Due).ToArray())
                {
                    _owner._waiters.Remove(waiter);
                    waiter.Signal.TrySetResult();
                }
            }

            return true;
        }

        public void Reschedule(DateTimeOffset firedAt)
        {
            _owner._timers.Remove(this);
            if (_period == Timeout.InfiniteTimeSpan)
            {
                Due = DateTimeOffset.MaxValue;
                return;
            }

            Due = firedAt + _period;
            _owner._timers.Add(this);
        }

        public void Run() => _callback(_state);

        public void Dispose()
        {
            lock (_owner._gate)
                _owner._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
