namespace Bsft.Messaging.Tests.Fakes;

/// <summary>
/// A clock whose timers only fire when the test says so. It is what lets a delayed redelivery be
/// asserted as a schedule — created, due at the right moment, fired, disposed — instead of by waiting.
/// </summary>
public sealed class ManualTimerProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private readonly object _gate = new();

    /// <summary>The due times of the timers that are still armed and undisposed, in order.</summary>
    public IReadOnlyList<TimeSpan> PendingDelays
    {
        get
        {
            lock (_gate)
                return _timers.Where(t => t is { IsDisposed: false, DueTime: not null }).Select(t => t.DueTime!.Value)
                    .ToArray();
        }
    }

    /// <summary>How many timers were created, whatever became of them.</summary>
    public int CreatedTimerCount
    {
        get
        {
            lock (_gate)
                return _timers.Count;
        }
    }

    /// <summary>Runs the callbacks of every armed, undisposed timer.</summary>
    public void FireAll()
    {
        ManualTimer[] due;

        lock (_gate)
            due = [.. _timers.Where(t => t is { IsDisposed: false, DueTime: not null })];

        foreach (var timer in due)
            timer.Fire();
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state, dueTime);

        lock (_gate)
            _timers.Add(timer);

        return timer;
    }

    /// <summary>A timer that does nothing until the provider fires it.</summary>
    private sealed class ManualTimer : ITimer
    {
        private readonly TimerCallback _callback;
        private readonly object? _state;

        public ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime)
        {
            _callback = callback;
            _state = state;
            DueTime = dueTime == Timeout.InfiniteTimeSpan ? null : dueTime;
        }

        public TimeSpan? DueTime { get; private set; }

        public bool IsDisposed { get; private set; }

        public void Fire() => _callback(_state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            DueTime = dueTime == Timeout.InfiniteTimeSpan ? null : dueTime;

            return true;
        }

        public void Dispose() => IsDisposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
