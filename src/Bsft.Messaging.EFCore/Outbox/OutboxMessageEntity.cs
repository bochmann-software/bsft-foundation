using Bsft.Outbox.EFCore;

namespace Bsft.Messaging.EFCore.Outbox;

/// <summary>
/// Durable outbox row holding an integration event until a dispatcher has published it. The row is
/// written in the same transaction as the state change that raised the event, so the event exists
/// exactly when the change it describes exists.
/// </summary>
/// <remarks>
/// What it adds to <see cref="OutboxRow"/> is the event itself. The lifecycle around it — the lease,
/// the attempt count, the retry instant, the retirement — is inherited, because it is the same in
/// every outbox and the drain kernel reads and writes it without knowing what a row carries.
/// </remarks>
public sealed class OutboxMessageEntity : OutboxRow
{
    /// <summary>The identity of the integration event, carried through to the consumer.</summary>
    public Guid EventId { get; set; }

    /// <summary>The registered type name the event travels under.</summary>
    public string EventTypeName { get; set; } = string.Empty;

    /// <summary>The integration event serialized as JSON.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>The UTC instant at which the event occurred.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>
    /// The correlation id of the request that staged the event, or <see langword="null"/> when none was
    /// ambient at that moment.
    /// </summary>
    /// <remarks>
    /// The column is nullable rather than defaulted, because <see langword="null"/> is the honest value
    /// for both cases that produce it: an event staged outside any correlated request, and a row
    /// written before this column existed. It is bounded by
    /// <see cref="OutboxMessageEntityConfiguration.CorrelationIdMaxLength"/>; the correlation
    /// middleware accepts at most 128 characters, so only a custom
    /// <c>ICorrelationContext</c> can produce a value that does not fit, and that value would fail the
    /// very transaction it belongs to rather than be silently shortened.
    /// </remarks>
    public string? CorrelationId { get; set; }
}
