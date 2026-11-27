namespace Bsft.Tests.Shared;

/// <summary>
/// A time provider whose clock and timers move only when a test calls <see cref="Advance"/>: a timer
/// fires exactly when the clock reaches its due time, never earlier, never by itself.
/// </summary>
/// <remarks>
/// This is the source-linked twin of <c>Bsft.Testing.Time.TestClock</c> for test projects that must not
/// reference <c>Bsft.Testing</c>: that library builds on <c>Bsft.Data</c>, <c>Bsft.Mediator</c> and the saga
/// engine, so the tests of those packages and of the layers below them link this file instead of depending on
/// something built on top of what they test. It shares the parts a test of those packages relies on: moves
/// that add up, timestamps that follow the clock, the signals that a timer is armed (also when a period
/// arms it again), and timers that cannot be changed after they were disposed. It is not the same fake:
/// it does not fire a zero due time at once and does not collect the exceptions of callbacks. <see cref="RecordingTimeProvider"/> fires every timer at once, which cannot
/// show that something happens <em>after</em> an interval and not before it; this one can. A test that has to know the code
/// under test has armed its timer calls <see cref="WaitForTimerAsync"/> instead of sleeping. Elapsed
/// time measured with <see cref="TimeProvider.GetTimestamp"/> moves with the clock as well, so code that
/// measures a deadline that way sees the time a test advances and no other.
/// </remarks>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly List<(DateTimeOffset Due, TaskCompletionSource Signal)> _waiters = [];
    private readonly List<TaskCompletionSource> _anyWaiters = [];
    private DateTimeOffset _now;
    private DateTimeOffset _horizon;

    public ManualTimeProvider(DateTimeOffset now)
    {
        _now = now;
        _horizon = now;
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _now;
    }

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override long GetTimestamp()
    {
        lock (_gate)
            return _now.UtcTicks;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>The number of timers that are armed and have not been disposed.</summary>
    public int PendingTimerCount
    {
        get
        {
            lock (_gate)
                return _timers.Count;
        }
    }

    /// <summary>Moves the clock forward and runs every timer that falls due, in due order.</summary>
    /// <param name="by">How far to move the clock.</param>
    /// <remarks>
    /// Moves add up: a call made from a callback, or from another thread while one is running, adds to the
    /// clock and the clock never reads an earlier instant than it did. Unlike <c>TestClock</c>, a timer that
    /// is created or changed to be due now waits for the next call, and a callback that throws ends the call
    /// at that timer.
    /// </remarks>
    public void Advance(TimeSpan by)
    {
        lock (_gate)
            _horizon += by;

        while (true)
        {
            ManualTimer? next;
            lock (_gate)
            {
                next = _timers.Where(timer => timer.Due <= _horizon).OrderBy(timer => timer.Due).FirstOrDefault();
                if (next is null)
                {
                    if (_horizon > _now)
                        _now = _horizon;

                    return;
                }

                if (next.Due > _now)
                    _now = next.Due;

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
    public Task WaitForTimerAsync(TimeSpan dueIn) => WaitForTimerAsync(dueIn, CancellationToken.None);

    /// <summary>
    /// Waits until a timer is pending that falls due exactly <paramref name="dueIn"/> from now, or until
    /// <paramref name="cancellationToken"/> is cancelled, so a test that may finish first can drop the
    /// wait instead of leaving it to time out unobserved.
    /// </summary>
    /// <param name="dueIn">How far from the current time the awaited timer is due.</param>
    /// <param name="cancellationToken">Ends the wait.</param>
    /// <returns>A task that completes when such a timer is pending.</returns>
    public async Task WaitForTimerAsync(TimeSpan dueIn, CancellationToken cancellationToken)
    {
        TaskCompletionSource waiter;
        lock (_gate)
        {
            if (_timers.Any(timer => timer.Due == _now + dueIn))
                return;

            waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((_now + dueIn, waiter));
        }

        await waiter.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits until any timer is pending, which is how a test learns that a loop which waits on this clock
    /// has finished its pass and is now waiting out its interval.
    /// </summary>
    /// <returns>A task that completes when a timer is pending.</returns>
    public Task WaitForAnyTimerAsync() => WaitForAnyTimerAsync(CancellationToken.None);

    /// <summary>
    /// Waits until any timer is pending, or until <paramref name="cancellationToken"/> is cancelled, so a
    /// test that may finish first can drop the wait instead of leaving it to time out unobserved.
    /// </summary>
    /// <param name="cancellationToken">Ends the wait.</param>
    /// <returns>A task that completes when a timer is pending.</returns>
    public async Task WaitForAnyTimerAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource waiter;
        lock (_gate)
        {
            if (_timers.Count > 0)
                return;

            waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _anyWaiters.Add(waiter);
        }

        await waiter.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
    }

    private void SignalArmed(DateTimeOffset due)
    {
        foreach (var anyWaiter in _anyWaiters)
            anyWaiter.TrySetResult();

        _anyWaiters.Clear();
        foreach (var waiter in _waiters.Where(waiter => waiter.Due == due).ToArray())
        {
            _waiters.Remove(waiter);
            waiter.Signal.TrySetResult();
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private TimeSpan _period = Timeout.InfiniteTimeSpan;
        private bool _isDisposed;

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
                if (_isDisposed)
                    return false;

                _period = period;
                _owner._timers.Remove(this);
                if (dueTime == Timeout.InfiniteTimeSpan)
                {
                    Due = DateTimeOffset.MaxValue;
                    return true;
                }

                Due = _owner._now + dueTime;
                _owner._timers.Add(this);
                _owner.SignalArmed(Due);
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
            _owner.SignalArmed(Due);
        }

        public void Run() => _callback(_state);

        public void Dispose()
        {
            lock (_owner._gate)
            {
                _isDisposed = true;
                _owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
