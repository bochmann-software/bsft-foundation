using System.Runtime.ExceptionServices;

namespace Bsft.Testing.Time;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock, timestamps and timers stand still until a test moves them, so
/// that code with durable timers, such as the saga timeout dispatcher, and code that waits on this clock can
/// be driven deterministically instead of waiting for wall-clock time to pass.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Advance"/> adds to the clock and runs every timer that falls due, in due order, with the clock
/// reading the timer's due time while its callback runs. A <c>Task.Delay</c>, a
/// <see cref="CancellationTokenSource"/> with a timeout, or a background service's periodic timer created
/// over this clock completes exactly when the test moves the clock past it, and never by itself. A timer
/// that a callback creates or changes inside the same move fires in that move when it falls within it.
/// A timer created or changed with a due time of <see cref="TimeSpan.Zero"/> fires before the call returns.
/// </para>
/// <para>
/// Time is additive: concurrent and nested calls of <see cref="Advance"/> add up and the clock never
/// reads an earlier instant than it has read before, except through <see cref="SetUtcNow"/>, which is
/// absolute and, called concurrently with another move, leaves the clock at whichever call came last.
/// </para>
/// <para>
/// <see cref="TimeProvider.GetTimestamp"/> follows the clock: it grows by exactly what the clock moves
/// forward and never goes back, even when <see cref="SetUtcNow"/> sets an earlier instant, so a deadline
/// measured with <see cref="TimeProvider.GetElapsedTime(long)"/> ends when the test advances past it and
/// not before. The timestamp frequency is one tick per 100 ns.
/// </para>
/// <para>
/// A callback that throws does not stop the move: the clock still reaches its target, later timers still
/// fire, and the exception (an <see cref="AggregateException"/> when several callbacks threw) surfaces from
/// the call that ran the callbacks.
/// </para>
/// <para>
/// Callbacks run on the thread that moved the clock. When several threads move it at once, each timer runs
/// once, on one of them, and a call of <see cref="Advance"/> can return while another thread's callback is
/// still running: the clock has reached the target, but a callback that another thread picked up may not
/// have finished. A timer that a callback changes to be due at once runs nested, inside that callback. The
/// stack is not the limit of such a chain: the runtime moves the work to another thread when the stack runs low,
/// and a chain of 200000 links ran to the end. The hazard is a lock: a chain whose first caller holds a lock that
/// a later link needs deadlocks once the work has hopped threads (it did at a depth of 849), so do not advance
/// the clock, or change a timer to be due at once, while holding a lock the callbacks take.
/// </para>
/// </remarks>
public sealed class TestClock : TimeProvider, ITestClock
{
    private const long StartTimestamp = TimeSpan.TicksPerHour;

    private static readonly TimeSpan DefaultHangGuard = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly List<TestTimer> _timers = [];
    private readonly List<(DateTimeOffset Due, TaskCompletionSource Armed)> _dueWaiters = [];
    private readonly List<TaskCompletionSource> _anyWaiters = [];
    private DateTimeOffset _utcNow;
    private DateTimeOffset _horizon;
    private readonly TimeSpan _hangGuard;
    private long _timestamp = StartTimestamp;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestClock"/> class starting at
    /// <see cref="DateTimeOffset.UnixEpoch"/>.
    /// </summary>
    public TestClock()
        : this(DateTimeOffset.UnixEpoch)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestClock"/> class starting at the given instant.
    /// </summary>
    /// <param name="utcNow">The instant the clock starts at. Converted to UTC.</param>
    public TestClock(DateTimeOffset utcNow)
        : this(utcNow, DefaultHangGuard)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestClock"/> class with a hang guard of its own, which
    /// the tests of the guard shorten.
    /// </summary>
    /// <param name="utcNow">The instant the clock starts at. Converted to UTC.</param>
    /// <param name="hangGuard">How long the wait methods wait, in real time, before they give up.</param>
    internal TestClock(DateTimeOffset utcNow, TimeSpan hangGuard)
    {
        _utcNow = utcNow.ToUniversalTime();
        _horizon = _utcNow;
        _hangGuard = hangGuard;
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_gate)
                return _utcNow;
        }
    }

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>Gets the number of timers that are armed and have not been disposed.</summary>
    public int PendingTimerCount
    {
        get
        {
            lock (_gate)
                return _timers.Count;
        }
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => UtcNow;

    /// <inheritdoc />
    public override long GetTimestamp()
    {
        lock (_gate)
            return _timestamp;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="dueTime"/> or <paramref name="period"/> is negative and not
    /// <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </exception>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var timer = new TestTimer(this, callback, state);
        try
        {
            timer.Change(dueTime, period);
        }
        catch
        {
            // A timer that is due at once runs inside this call; one that throws must not stay armed
            // behind a handle nobody was given.
            timer.Dispose();
            throw;
        }

        return timer;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="by"/> is negative.</exception>
    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(by), by, "Time can only be advanced forward.");

        lock (_gate)
            _horizon += by;

        RunDueTimers();
    }

    /// <inheritdoc />
    public void SetUtcNow(DateTimeOffset utcNow)
    {
        var target = utcNow.ToUniversalTime();
        lock (_gate)
        {
            _horizon = target;
            if (target < _utcNow)
                _utcNow = target;
        }

        RunDueTimers();
    }

    /// <summary>
    /// Waits until a timer is armed that falls due exactly <paramref name="dueIn"/> from now, so a test
    /// advances the clock only once the timer it means has been armed, whatever else was armed before.
    /// </summary>
    /// <param name="dueIn">How far from the current time the awaited timer is due.</param>
    /// <param name="cancellationToken">Ends the wait before the hang guard does.</param>
    /// <returns>A task that completes when such a timer is armed.</returns>
    /// <remarks>
    /// A timer that is armed again by its period counts as armed. The wait ends with a
    /// <see cref="TimeoutException"/> when no such timer is armed within 30 seconds of real time, a hang guard
    /// that a test never reaches when the timer is armed.
    /// </remarks>
    public Task WaitForTimerAsync(TimeSpan dueIn, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource armed;
        lock (_gate)
        {
            var due = _utcNow + dueIn;
            if (_timers.Exists(timer => timer.Due == due))
                return Task.CompletedTask;

            armed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _dueWaiters.Add((due, armed));
        }

        return armed.Task.WaitAsync(_hangGuard, TimeProvider.System, cancellationToken);
    }

    /// <summary>
    /// Waits until any timer is armed, which is how a test learns that a loop which waits on this clock
    /// has finished its pass and is now waiting out its interval.
    /// </summary>
    /// <param name="cancellationToken">Ends the wait before the hang guard does.</param>
    /// <returns>A task that completes when a timer is armed.</returns>
    /// <remarks>
    /// <para>
    /// The task completes at once when a timer is armed already, so what a test may conclude depends on how
    /// the loop waits. A loop that waits with <c>Task.Delay</c> on this clock holds a timer only while it
    /// waits, so an armed timer means the pass is over. A <c>PeriodicTimer</c> is armed for as long as it
    /// exists, including while a pass is still running, so an armed timer says nothing about the pass; wait for
    /// something the pass does instead, and use this method only to learn that the loop has created its timer.
    /// The same holds for a timer that is armed again by its period after it fired.
    /// </para>
    /// <para>
    /// The wait ends with a <see cref="TimeoutException"/> when no timer is armed within 30 seconds of real
    /// time, a hang guard that a test never reaches when the loop does wait on this clock.
    /// </para>
    /// </remarks>
    public Task WaitForAnyTimerAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource armed;
        lock (_gate)
        {
            if (_timers.Count > 0)
                return Task.CompletedTask;

            armed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _anyWaiters.Add(armed);
        }

        return armed.Task.WaitAsync(_hangGuard, TimeProvider.System, cancellationToken);
    }

    private void RunDueTimers()
    {
        List<Exception>? failures = null;
        while (true)
        {
            TestTimer? next;
            lock (_gate)
            {
                next = FindEarliestDue();
                MoveForwardTo(next?.Due ?? _horizon);
                next?.Reschedule(_utcNow);
            }

            if (next is null)
                break;

            // A task run inline keeps whatever the callback throws, so every due timer still runs and the
            // failures surface together once the clock has reached its target.
            using var callback = new Task(next.Run);
            callback.RunSynchronously(TaskScheduler.Default);
            if (callback.Exception is { } fault)
                (failures ??= []).AddRange(fault.InnerExceptions);
        }

        if (failures is { Count: 1 })
            ExceptionDispatchInfo.Capture(failures[0]).Throw();

        if (failures is { Count: > 1 })
            throw new AggregateException(failures);
    }

    private TestTimer? FindEarliestDue()
    {
        TestTimer? earliest = null;
        foreach (var timer in _timers)
        {
            if (timer.Due <= _horizon && (earliest is null || timer.Due < earliest.Due))
                earliest = timer;
        }

        return earliest;
    }

    private void MoveForwardTo(DateTimeOffset instant)
    {
        if (instant <= _utcNow)
            return;

        _timestamp += (instant - _utcNow).Ticks;
        _utcNow = instant;
    }

    private void SignalArmed(DateTimeOffset due)
    {
        foreach (var waiter in _anyWaiters)
            waiter.TrySetResult();

        _anyWaiters.Clear();
        foreach (var waiter in _dueWaiters.Where(waiter => waiter.Due == due).ToArray())
        {
            _dueWaiters.Remove(waiter);
            waiter.Armed.TrySetResult();
        }
    }

    private sealed class TestTimer(TestClock owner, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;
        private bool _isDisposed;

        public DateTimeOffset Due { get; private set; } = DateTimeOffset.MaxValue;

        /// <inheritdoc/>
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            ValidateInterval(dueTime, nameof(dueTime));
            ValidateInterval(period, nameof(period));

            bool isDueNow;
            lock (owner._gate)
            {
                if (_isDisposed)
                    return false;

                _period = period;
                owner._timers.Remove(this);
                if (dueTime == Timeout.InfiniteTimeSpan)
                {
                    Due = DateTimeOffset.MaxValue;
                    return true;
                }

                Due = owner._utcNow + dueTime;
                owner._timers.Add(this);
                owner.SignalArmed(Due);
                isDueNow = Due <= owner._horizon;
            }

            if (isDueNow)
                owner.RunDueTimers();

            return true;
        }

        public void Reschedule(DateTimeOffset firedAt)
        {
            owner._timers.Remove(this);
            if (_period == Timeout.InfiniteTimeSpan || _period <= TimeSpan.Zero)
            {
                Due = DateTimeOffset.MaxValue;
                return;
            }

            Due = firedAt + _period;
            owner._timers.Add(this);
            owner.SignalArmed(Due);
        }

        public void Run() => callback(state);

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (owner._gate)
            {
                _isDisposed = true;
                owner._timers.Remove(this);
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        private static void ValidateInterval(TimeSpan interval, string name)
        {
            if (interval < TimeSpan.Zero && interval != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(name, interval, "A timer interval is not negative; use Timeout.InfiniteTimeSpan to switch it off.");
        }
    }
}
