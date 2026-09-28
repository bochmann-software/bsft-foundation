namespace Bsft.EventAggregator.Saga.Timeouts;

/// <summary>
/// A timeout operation recorded by a saga step, applied only once the step has finished and its
/// state is persisted. Collecting the operations first keeps the state and its timers consistent:
/// a step that throws leaves neither behind.
/// </summary>
internal sealed record PendingSagaTimeout
{
    /// <summary>What the step asked for: schedule, cancel one, or cancel all.</summary>
    public PendingSagaTimeoutKind Kind { get; init; }

    /// <summary>The timeout name, empty for a cancel-all.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The business-time delay, when the step scheduled a relative timeout.</summary>
    public TimeSpan? Delay { get; init; }

    /// <summary>The absolute due date, when the step scheduled one directly.</summary>
    public DateTimeOffset? DueAt { get; init; }

    /// <summary>The event to publish when the timeout fires.</summary>
    public object? Payload { get; init; }

    /// <summary>The business calendar the delay is measured in, or <c>null</c> for the default.</summary>
    public string? CalendarKey { get; init; }

    /// <summary>
    /// The correlation id captured while the step was running. It has to be taken there and not in
    /// the scheduler: the scheduler is a singleton and no longer sees the step's service scope.
    /// </summary>
    public string? CorrelationId { get; init; }
}
