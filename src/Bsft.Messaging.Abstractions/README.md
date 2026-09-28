# Bsft.Messaging.Abstractions

The contracts of the `Bsft.Messaging` family: what an integration event is, what goes on the wire, and
every seam a transport, a store or a consumer is written against — without the implementation.

## Install

```bash
dotnet add package Bsft.Messaging.Abstractions
```

No further Bsft packages, and no third-party dependencies at all. Its dependency closure is the BCL and
nothing else, which is what lets a client assembly, a broker adapter or a second store reference the
contracts without pulling the pipeline in.

## Register

```csharp
// Nothing to register: this package has no DI entry point.
```

An `.Abstractions` package registers nothing by design. Reference `Bsft.Messaging` and call
`AddBsftMessaging` to get the implementations of everything declared here.

## Configure

None. Options types live in the implementation package, so an assembly that only holds contracts cannot
drag configuration binding in with them.

## Extend

The seams a host or a second implementation may supply, one line each:

- `IMessageBus` — publishes an `IntegrationEventMessage`; the sending half of the transport. Defaults to
  the in-process bus, replaced by a broker adapter.
- `IMessageSource` — streams `IMessageDelivery` instances; the receiving half. The two halves are what a
  broker adapter implements and nothing else.
- `IMessageDelivery` — one delivery and the three ways to finish with it: acknowledge, retry, park. A
  retry carries the delay before which the message must not be delivered again; an implementation
  schedules that rather than sleeping, or documents that it cannot.
- `IOutboxStore` — leases pending messages and records their outcome; never publishes anything itself.
  Abandoning a message records when it may be leased again, so a wait survives a restart.
- `IOutboxProcessor` — runs one drain pass of the outbox on demand rather than waiting for the polling
  loop. Implemented by the dispatcher the host already runs, so a test drains what the application would
  have published; a pass takes one batch, so repeat until it returns zero.
- `IInboxStore` — the record of which events this consumer has finished with, split into a claim and a
  commit so that an implementation sharing the application's unit of work can be atomic.
- `IIntegrationEventCollector` / `IIntegrationEventBuffer` — the staging area, split into the side
  application code writes and the side the persistence layer drains.
- `IIntegrationEventSerializer` — how an event is encoded into a payload and back.
- `IIntegrationEventTypeRegistry` — the explicit mapping between an event type and the stable name it
  travels under.
- `IIntegrationEventHandler<TEvent>` — what an application implements to consume an event. Delivery is
  at-least-once, so it must be idempotent.
- `IIntegrationEventHandlerInvoker` — runs the handlers of one decoded event, in registration order,
  reporting their failures together.
- `IIncomingMessageDecoder` — turns wire data into a typed event or refuses it with a
  `MessageParkReason`.
- `IInboxGuardedDelivery` — one delivery through the inbox: claim, handlers, commit.
- `IIntegrationEventConsumer` — one delivery end to end, including telling the transport what became of
  it.
- `IMessageCorrelationAccessor` / `IMessageCorrelationScope` — where a staged event's correlation id
  comes from, and how a handled message's is restored around the handlers.
- `IInternalIntegrationEvent` — the marker that keeps a framework-internal event off external fan-out
  such as webhooks.

The contract types beside them are `IntegrationEventMessage` (the wire envelope and its version),
`LeasedOutboxMessage`, `EventDeliveryOutcome`, `MessageParkReason`, `MessageReasonLimits` and the two
exceptions in `Exceptions/`, `MessagePublishException` and `MessageDecodeException`.

## Documentation

Topic page: [docs/messaging.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/messaging.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
