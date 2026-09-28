namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Options for the saga timeout dispatcher.
/// </summary>
public sealed class SagaTimeoutOptions
{
    /// <summary>Gets or sets how long the dispatcher waits after an empty pass. Defaults to five seconds.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the maximum number of due timeouts read per pass. Defaults to 100.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Gets or sets how overdue timeouts are handled. Defaults to <see cref="SagaTimeoutCatchUpPolicy.FireAll"/>.</summary>
    public SagaTimeoutCatchUpPolicy CatchUpPolicy { get; set; } = SagaTimeoutCatchUpPolicy.FireAll;

    /// <summary>
    /// Gets or sets how far overdue a timeout may be before <see cref="SagaTimeoutCatchUpPolicy.SkipBeyondMaxAge"/>
    /// discards it. Defaults to one day.
    /// </summary>
    public TimeSpan MaxCatchUpAge { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Gets or sets how often a timeout may fail to fire before it is retired to the dead-letter state.
    /// Defaults to 10. Without a limit one timeout that can never fire — a payload whose event type was
    /// renamed, say — keeps its place at the head of every poll, and once a batch of them has
    /// accumulated no saga timer in the application fires again. The retired row is kept, so nothing is
    /// lost.
    /// </summary>
    public int MaxDispatchAttempts { get; set; } = 10;
}
