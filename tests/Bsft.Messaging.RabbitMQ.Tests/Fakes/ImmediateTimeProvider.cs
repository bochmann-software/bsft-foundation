namespace Bsft.Messaging.RabbitMQ.Tests.Fakes;

/// <summary>
/// A clock whose timers fire at once and that records what each of them was asked to wait. It lets a
/// test assert that a delay was taken without the test taking it.
/// </summary>
internal sealed class ImmediateTimeProvider : TimeProvider
{
    private readonly List<TimeSpan> _waits = [];
    private readonly object _gate = new();

    /// <summary>The due times the timers were created with, in order.</summary>
    public IReadOnlyList<TimeSpan> Waits
    {
        get
        {
            lock (_gate)
                return _waits.ToArray();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate)
            _waits.Add(dueTime);

        return new ImmediateTimer(callback, state);
    }

    /// <summary>A timer that runs its callback as soon as it exists and does nothing afterwards.</summary>
    private sealed class ImmediateTimer : ITimer
    {
        public ImmediateTimer(TimerCallback callback, object? state) =>
            ThreadPool.QueueUserWorkItem(_ => callback(state));

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
