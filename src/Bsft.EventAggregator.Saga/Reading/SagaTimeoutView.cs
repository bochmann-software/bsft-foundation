namespace Bsft.EventAggregator.Saga.Reading;

/// <summary>
/// One durable timeout as it hangs off the instance it belongs to.
/// </summary>
/// <remarks>
/// Read beside the instance rather than as a listing of its own: a timeout is only meaningful as
/// "what this saga is waiting for", and a table of every timeout in the installation answers no
/// question an operator has. It carries the payload and the dead-letter reason, both of which quote
/// what the saga was doing, so it is part of the content read and not of the listing.
/// </remarks>
/// <param name="Id">The row's identity.</param>
/// <param name="Name">The timeout's name, unique per pending instance.</param>
/// <param name="StepIndex">The waiting step whose reminders it belongs to.</param>
/// <param name="DueAt">When it fires.</param>
/// <param name="PayloadTypeName">The event type it publishes.</param>
/// <param name="PayloadJson">The serialized event.</param>
/// <param name="CorrelationId">The correlation captured when it was scheduled.</param>
/// <param name="AttemptCount">How many dispatches have failed.</param>
/// <param name="DispatchedAt">When it left the pending set, or <see langword="null"/> while pending.</param>
/// <param name="DeadLetteredAt">When it was retired, or <see langword="null"/>.</param>
/// <param name="DeadLetterReason">What retired it, or <see langword="null"/>.</param>
public sealed record SagaTimeoutView(
    long Id,
    string Name,
    int StepIndex,
    DateTimeOffset DueAt,
    string PayloadTypeName,
    string PayloadJson,
    string? CorrelationId,
    int AttemptCount,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? DeadLetteredAt,
    string? DeadLetterReason);
