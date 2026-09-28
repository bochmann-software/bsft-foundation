namespace Bsft.Messaging;

/// <summary>
/// Marks a framework-internal transport or plumbing event: one that participates in the outbox and
/// consumer machinery like any other integration event, but must not be exposed to external fan-out
/// consumers such as webhooks.
/// </summary>
/// <remarks>
/// <para>
/// An integration event is a published contract, and the moment one is declared into
/// <c>BsftMessagingOptions</c> every open-generic consumer sees it — the webhook fan-out included. Some
/// events are declared only to ride that machinery internally: an outbox transport event that carries
/// one unit of work from where it is staged to where it is handled, and that has no meaning outside
/// the framework strand that raised it. Handing such an event to an external subscriber turns internal
/// plumbing into an external contract, which is a leak even when the payload is harmless.
/// </para>
/// <para>
/// Applying this marker keeps the event on the internal path — the outbox, the inbox guard, the retries
/// and the framework's own handler — while the webhook fan-out skips it. External subscribers that want
/// to react to the same state change should be given a real domain event to subscribe to instead.
/// </para>
/// </remarks>
public interface IInternalIntegrationEvent : IIntegrationEvent
{
}
