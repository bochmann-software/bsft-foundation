namespace Bsft.EventAggregator.Saga.Timeouts;

/// <summary>
/// A durable timer attached to a saga instance. When <see cref="DueAt"/> passes, the dispatcher
/// publishes the carried event; the saga step reacts to that event like any other.
/// </summary>
public sealed class SagaTimeout
{
    /// <summary>Gets or sets the store-assigned identity of this timeout.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the saga type this timeout belongs to.</summary>
    public string SagaType { get; set; } = string.Empty;

    /// <summary>Gets or sets the correlation key of the saga instance.</summary>
    public string CorrelationKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the name of the timeout, unique per saga instance.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the logical step index that scheduled this timeout. Used to cancel the pending
    /// timers of a step automatically once the saga has advanced past it.
    /// </summary>
    public int StepIndex { get; set; }

    /// <summary>Gets or sets the instant at which the timeout fires.</summary>
    public DateTimeOffset DueAt { get; set; }

    /// <summary>Gets or sets the JSON-serialized event payload.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>Gets or sets the assembly-qualified type name of the event payload.</summary>
    public string PayloadTypeName { get; set; } = string.Empty;

    /// <summary>Gets or sets the correlation id captured when the timeout was scheduled.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Gets or sets the number of failed dispatch attempts.</summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Gets or sets the instant at which the timeout left the pending set — by being dispatched,
    /// skipped by the catch-up policy or retired to the dead-letter state — or <c>null</c> while it is
    /// still pending. One field carries all three because every consumer of it asks the same question:
    /// whether the timer is still owed. <see cref="DeadLetteredAt"/> tells the three cases apart.
    /// </summary>
    public DateTimeOffset? DispatchedAt { get; set; }

    /// <summary>
    /// Gets or sets the instant at which the timeout was retired after failing too often, or
    /// <c>null</c> when it was never retired. A retired timeout stays readable for diagnosis but is
    /// never dispatched again.
    /// </summary>
    public DateTimeOffset? DeadLetteredAt { get; set; }

    /// <summary>
    /// Gets or sets the failure that retired the timeout, or <c>null</c> when it was never retired.
    /// </summary>
    /// <remarks>
    /// This is the message of the exception that failed the last dispatch: an exception raised while
    /// resolving or publishing a timeout payload can quote parts of that payload, so treat this as at
    /// least as sensitive as the payload itself. What is new here is the reach, not the content. The
    /// same exception is also written to the log, the way every failing background pass in this
    /// framework writes one, and the log is where an operator looks first. The difference this field
    /// makes is that the reason outlives the log's retention and is readable by anyone who can read the
    /// timeout row, which is usually a wider audience with a longer memory.
    /// </remarks>
    public string? DeadLetterReason { get; set; }

    /// <summary>Gets or sets the optimistic concurrency token that makes a claim race detectable.</summary>
    public Guid ConcurrencyToken { get; set; }
}
