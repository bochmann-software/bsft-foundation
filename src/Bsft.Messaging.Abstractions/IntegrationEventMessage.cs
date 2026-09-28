namespace Bsft.Messaging;

/// <summary>
/// The wire format of an integration event: the metadata a transport and a consumer need, wrapped
/// around the serialized event. It is the contract that leaves the process, so — unlike the internals
/// of the outbox — its shape is published and versioned.
/// </summary>
/// <remarks>
/// <para>
/// It is an envelope, and it separates two things that used to be one. The payload is the event as its
/// author defined it and only its consumers understand it; the fields around it are understood by
/// every transport, every consumer and every operator looking at a message, without any of them having
/// to deserialize the payload. That is what makes routing on the event name, deduplicating on the event
/// id and following a correlation id across services possible for a broker adapter that knows nothing
/// about the application's types.
/// </para>
/// <para>
/// A broker adapter maps the fields onto its own message metadata rather than nesting this type
/// inside the body — for AMQP, <see cref="EventId"/> onto <c>message-id</c>,
/// <see cref="CorrelationId"/> onto <c>correlation-id</c>, <see cref="OccurredAt"/> onto
/// <c>timestamp</c>, <see cref="EventName"/> onto the routing key and <see cref="MessageVersion"/>
/// onto a header — so that broker tooling and consumers written against other frameworks can read
/// them.
/// </para>
/// </remarks>
public sealed record IntegrationEventMessage
{
    /// <summary>
    /// The version of the message contract this type currently produces.
    /// </summary>
    /// <remarks>
    /// It is raised only when the meaning of a message field changes or a field a consumer must
    /// understand is added, so a consumer can refuse a version it was not written for. Evolving the
    /// <em>payload</em> does not touch it: a payload change that consumers cannot absorb gets a new
    /// event name instead, which is why the name carries a version suffix by convention.
    /// </remarks>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Gets the version of the message contract this message was written under. Defaults to
    /// <see cref="CurrentVersion"/>; a message read off the wire carries whatever its producer wrote.
    /// </summary>
    public int MessageVersion { get; init; } = CurrentVersion;

    /// <summary>
    /// Gets the identity of the integration event. Delivery is at-least-once, so a consumer
    /// deduplicates on this id; it is stable across every redelivery of the same event.
    /// </summary>
    public Guid EventId { get; init; }

    /// <summary>
    /// Gets the stable name the event travels under, as registered with
    /// <see cref="IIntegrationEventTypeRegistry"/>. It — not a CLR type name — is what a consumer
    /// resolves, so producer and consumer can run different assemblies.
    /// </summary>
    public string EventName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC instant of the state change that raised the event. It is not the instant the
    /// message was published: the two differ by however long the message waited in the outbox.
    /// </summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// Gets the correlation id of the request that staged the event, or <see langword="null"/> when
    /// none was ambient at that moment — a message staged by a background worker, or one written
    /// before the producing host had the correlation bridge registered.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Gets the serialized integration event. Message version 1 defines this as JSON, and it is
    /// carried through verbatim: the bytes a consumer sees are the bytes the producer committed to its
    /// outbox in the same transaction as the state change.
    /// </summary>
    public string Payload { get; init; } = string.Empty;
}
