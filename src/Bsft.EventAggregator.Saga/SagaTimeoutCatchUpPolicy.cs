namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Determines what happens to timeouts that came due while the application was down.
/// </summary>
public enum SagaTimeoutCatchUpPolicy
{
    /// <summary>Every overdue timeout fires, oldest first. The default.</summary>
    FireAll = 0,

    /// <summary>
    /// Of the overdue timeouts of one saga instance, only the one with the latest due date fires;
    /// the others are skipped. Prevents a burst of stale reminders for the same saga after a long
    /// outage.
    /// </summary>
    FireLatestPerSaga = 1,

    /// <summary>
    /// Timeouts overdue by more than <see cref="SagaTimeoutOptions.MaxCatchUpAge"/> are skipped
    /// instead of fired. Timeouts within that window still fire.
    /// </summary>
    SkipBeyondMaxAge = 2,
}
