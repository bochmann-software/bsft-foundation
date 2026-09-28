using Bsft.Messaging;

namespace Bsft.Sample.Orders.Core;

/// <summary>
/// The integration event the order module publishes when an order has been created. Unlike
/// <see cref="OrderCreatedNotification"/>, which is in-process and free to change with the code that
/// raises it, this one is serialized into the outbox and would leave the process against a broker, so
/// its shape is a published contract. It travels under the stable name <c>orders.placed.v1</c>,
/// declared in the host's <c>AddBsftMessaging</c> call.
/// </summary>
/// <remarks>
/// The two coexist on purpose: the notification is what the saga engine reacts to inside this process,
/// this event is what another service would consume. Reusing one type for both roles would tie a
/// published contract to a type that changes whenever the local workflow does.
/// </remarks>
public sealed record OrderPlacedIntegrationEvent : IIntegrationEvent
{
    /// <summary>
    /// Gets the identity of this event. It survives into the message, and the inbox deduplicates on it.
    /// </summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the instant the order was created — not the instant the event was published, which is
    /// however much later the dispatcher got to the row.
    /// </summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// Gets the identifier of the order that was created.
    /// </summary>
    public Guid OrderId { get; init; }

    /// <summary>
    /// Gets the name of the ordered product, as it stood when the order was placed.
    /// </summary>
    public string ProductName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the quantity ordered.
    /// </summary>
    public int Quantity { get; init; }
}
