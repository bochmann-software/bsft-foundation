namespace Bsft.Resilience.Tests.Fakes;

/// <summary>
/// A <see cref="TimeProvider"/> whose timers fire at once and whose due times are recorded. It makes the
/// backoff virtual: a test asserts the delays the retry loop asked for without spending them.
/// </summary>
internal sealed class RecordingTimeProvider : TimeProvider
{
    private readonly List<TimeSpan> _delays = [];

    /// <summary>Gets the due times requested so far, in order.</summary>
    public IReadOnlyList<TimeSpan> Delays => _delays;

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        return new ImmediateTimer(this, callback, state, dueTime);
    }

    /// <summary>Records one requested due time, ignoring the infinite one that only arms a timer.</summary>
    /// <param name="dueTime">The requested due time.</param>
    private void Record(TimeSpan dueTime)
    {
        if (dueTime >= TimeSpan.Zero)
            _delays.Add(dueTime);
    }

    /// <summary>A timer that completes immediately, so a virtual delay costs no wall-clock time.</summary>
    private sealed class ImmediateTimer : ITimer
    {
        private readonly RecordingTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;

        /// <summary>Initializes a new instance of the <see cref="ImmediateTimer"/> class.</summary>
        /// <param name="owner">The provider recording the due times.</param>
        /// <param name="callback">What the timer runs when it fires.</param>
        /// <param name="state">The state handed to the callback.</param>
        /// <param name="dueTime">The due time the caller asked for.</param>
        public ImmediateTimer(RecordingTimeProvider owner, TimerCallback callback, object? state, TimeSpan dueTime)
        {
            _owner = owner;
            _callback = callback;
            _state = state;

            Arm(dueTime);
        }

        /// <inheritdoc />
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Arm(dueTime);
            return true;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing to release: the timer has already fired.
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        /// <summary>Records the due time and fires off the thread that armed the timer.</summary>
        /// <param name="dueTime">The due time the caller asked for.</param>
        private void Arm(TimeSpan dueTime)
        {
            if (dueTime < TimeSpan.Zero)
                return;

            _owner.Record(dueTime);
            ThreadPool.QueueUserWorkItem(_ => _callback(_state));
        }
    }
}
