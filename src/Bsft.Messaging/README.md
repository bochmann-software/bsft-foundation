# Bsft.Messaging

The core of the `Bsft.Messaging` family: integration event staging, the declared event vocabulary and its
payload encoding, the transactional-outbox dispatcher, and the consuming pipeline that puts an inbox in
front of the handlers.

## Install

```bash
dotnet add package Bsft.Messaging
```

Requires `Bsft.Messaging.Abstractions` (pulled transitively), `Bsft.Core` for the polling loop the
dispatcher runs on, and `Bsft.Instrumentation` for the consumer's instrumentation source. It
holds nothing durable itself: the outbox and the durable inbox live in `Bsft.Messaging.EFCore`, and a
broker lives in `Bsft.Messaging.RabbitMQ`. Without either, the in-memory bus and the in-memory inbox are
what a host gets.

## Register

```csharp
services.AddBsftMessaging(options =>
{
    options.AddEvent<OrderPlaced>("orders.placed.v1");
    options.Consumer.ConsumerName = "orders";
});

// or bound from configuration, section "Bsft:Messaging"
services.AddBsftMessaging(configuration, options => options.AddEvent<OrderPlaced>("orders.placed.v1"));
```

The event vocabulary is declared in code because it names CLR types; everything else binds from the
section. Consuming is opt-in and needs three more calls: `AddBsftIntegrationEventHandler<TEvent, THandler>()`
per handler, `AddBsftConsumerHost()` for the background service that reads the transport, and either a
broker adapter or `AddBsftMessagingLoopback()` for the transport itself — both of them **before**
`AddBsftMessaging`, which registers the in-memory bus with `TryAdd`. `AddBsftInbox()` registers the
duplicate guard on its own where the host runs the delivery itself.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| `BsftMessagingOptions.Consumer.ConsumerName` | `""` | The identity this consumer records processed events under. Half of the inbox key, so two services sharing it swallow each other's events. The consumer host refuses to start without it. |
| `BsftMessagingOptions.Consumer.MaxDeliveryAttempts` | `10` | How often a message may be delivered before it is parked. Counted against `IMessageDelivery.DeliveryAttempt`, so it bounds nothing on a transport that cannot count redeliveries. |
| `BsftMessagingOptions.Consumer.ReceiveRestartDelay` | `00:00:05` | How long the host waits before it enumerates its message source again after the stream ended or failed. |
| `BsftMessagingOptions.Consumer.RetryDelay` | `00:00:00` | The first interval a failed message is held back for; it doubles with every further attempt. The consumer hands the interval to the transport instead of waiting it out. |
| `BsftMessagingOptions.Consumer.MaxRetryDelay` | `00:00:30` | The ceiling of that schedule, and the clamp on a wait a handler asked for. Short, because a transport that cannot schedule a redelivery waits it out and holds the consumer. |
| `BsftMessagingOptions.Consumer.InboxLedgerCapacity` | `100000` | How many completed deliveries the in-memory ledger keeps. The EF Core inbox ignores it. |
| `BsftMessagingOptions.Consumer.ParkedMessageCapacity` | `100` | How many parked messages the loopback transport keeps for inspection. A broker parks into its own dead-letter queue and ignores it. |
| `BsftMessagingOptions.Consumer.InboxRetention` | `null` | How long the EF Core inbox keeps a completed delivery. Setting it makes the retention the duplicate-detection window; `null` keeps every row. |
| `BsftMessagingOptions.Outbox.PollInterval` | `00:00:05` | The pause between polls once the outbox has run dry. |
| `BsftMessagingOptions.Outbox.BatchSize` | `100` | How many messages one poll claims. The whole batch is leased until one instant, so it and the lease together fix what a single publish may take. |
| `BsftMessagingOptions.Outbox.LeaseDuration` | `00:01:00` | How long a claim holds. A dispatcher that dies mid-delivery blocks its messages for at most this long. |
| `BsftMessagingOptions.Outbox.MaxDeliveryAttempts` | `10` | How often a message may fail before it is retired to the dead-letter state. The row is kept. |
| `BsftMessagingOptions.Outbox.RetryBackoff` | `00:00:05` | The first interval a failed message is held back for, recorded on the row; it doubles with every further attempt. Zero retries on the next poll, as releases before 1.3.6 did; a wait a handler asked for is still obeyed, because it is a floor rather than a step. |
| `BsftMessagingOptions.Outbox.MaxRetryDelay` | `00:05:00` | The ceiling of that schedule, and the clamp on a wait a handler asked for. Generous, because holding a row back costs nothing but the delay. |

Validation: `ValidateOnStart()` rejects an attempt budget below one on either side, a non-positive
receive-restart delay, poll interval or lease duration, an inbox-ledger or parked-message capacity below
one, a negative retry delay or retry backoff, a retry ceiling that is not positive on either side, and an
inbox retention that is set and not positive — all of them reported in one message rather than one restart
at a time. `ConsumerName` is deliberately not among them: a host that
publishes without consuming has no use for it, so the consumer host rejects an absent name instead.

## Extend

Every seam of the family is declared in
[`Bsft.Messaging.Abstractions`](../Bsft.Messaging.Abstractions/README.md) and listed there, so a
replacement can be written without referencing this package. What this package supplies is the default
implementation of each: `IntegrationEventTypeRegistry` and `JsonIntegrationEventSerializer` for the
vocabulary and its encoding, `IntegrationEventCollector` for both sides of the staging contract,
`InMemoryMessageBus` and `LoopbackMessageTransport` for the transport, `InMemoryInboxStore` over the
process-wide `InMemoryInboxLedger` for the inbox, `IncomingMessageDecoder`, `IntegrationEventHandlerInvoker`
and `InboxGuardedDelivery` for the consuming steps, and `IntegrationEventConsumer` for the three transport
outcomes. Two of those defaults expect replacing in anything durable: the in-memory bus handles inside the
publish and has no inbox at all, and the in-memory inbox forgets every event when the process stops.

`IOutboxProcessor` is not a replacement seam but a way in: `ProcessOnceAsync` runs one drain pass of
the transactional outbox instead of waiting for the polling loop. `AddBsftMessagingEFCore` registers it
on the same `OutboxDispatcher` the host runs as a background service, so a test drains what the
application would have published. A pass takes one batch, so repeat until it returns zero.

`IConsumerTelemetry` is the one seam this package declares itself. It stays here rather than in the
abstractions because it names `MessageParkReason` against a meter and an activity source that only the
consuming pipeline has; replace it to record deliveries somewhere other than the shipped
`Bsft.Messaging` instrumentation source.

## Documentation

Topic page: [docs/messaging.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/messaging.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
