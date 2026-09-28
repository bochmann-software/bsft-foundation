# Bsft.EventAggregator

An in-process publish/subscribe bus. It is the seam that lets one module react to
something another module did without either referencing the other, and it is the
transport the saga engine subscribes to.

It is deliberately **in-process only**. Nothing is persisted, nothing crosses a
process boundary, and a handler failure is not retried. For events that must
leave the process or survive a restart, use [messaging.md](messaging.md).

## Contract

```csharp
public interface IEventAggregator
{
    void Subscribe<TEvent>(Func<TEvent, Task> handler);
    void Unsubscribe<TEvent>(Func<TEvent, Task> handler);
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default);

    void Subscribe<TEvent>(Action<TEvent> handler);
    void Unsubscribe<TEvent>(Action<TEvent> handler);
    void Publish<TEvent>(TEvent payload);
}
```

The last three are synchronous conveniences. `Publish` blocks on `PublishAsync`,
so prefer the async pair anywhere blocking matters.

There is no constraint on `TEvent` — any type can be an event. Subscription is by
type: publishing a `StockReserved` invokes every handler registered for
`StockReserved`, and nothing else.

## Setup

```csharp
services.AddBsftEventAggregator();
```

The method was called `AddBsftEventAggregator` until 1.3.5. It registers
`SimpleEventAggregator` as a singleton behind `IEventAggregator` — a singleton
because subscriptions have to outlive the request that created them.

## Failure semantics

`PublishAsync` snapshots the handler list under a lock, then invokes each
handler. Every handler runs even if an earlier one threw; the failures are
collected and rethrown together as a single `AggregateException`.

Two consequences worth designing around. One subscriber cannot prevent the
others from seeing the event — which is the point of a bus. And a failure is not
silently swallowed either, so a publisher that ignores the returned task ignores
real errors.

Delivery is at-most-once and in-memory: if the process dies mid-publish, the
remaining handlers never run and nothing replays.

## Subscribing

Because the aggregator is a singleton and handlers are delegates, subscriptions
are usually made once at startup — typically from a module's `OnStartAsync`:

```csharp
public sealed class StockReservationHandler(IEventAggregator events, IInventoryService inventory)
{
    public void Subscribe() => events.Subscribe<StockReservationRequested>(OnRequested);

    private async Task OnRequested(StockReservationRequested e)
    {
        await inventory.ReserveAsync(e.Sku, e.Quantity);
    }
}
```

`Unsubscribe` matches on the delegate instance. A lambda passed inline cannot be
unsubscribed later, because the second lambda is a different object — keep a
reference to the method group if the subscription is ever removed. The
synchronous `Action<TEvent>` overloads are handled specially by the
implementation, which keeps an internal wrapper map so that unsubscribing the
same `Action` works as expected.

## Relationship to the mediator

The two buses are independent by default: publishing a mediator `INotification`
does **not** reach aggregator subscribers, and vice versa. Connect them
explicitly when a saga or a runtime subscriber has to see mediator
notifications:

```csharp
services.AddBsftEventAggregator();

services.AddBsftMediator(m => m
    .RegisterServicesFromAssembly(assembly)
    .ForwardNotifications());

services.AddBsftMediatorEventAggregator();   // the bridge package
```

`IMediator.PublishAsync` then runs the notification's own handlers and hands the
same instance to the `Bsft.Mediator.EventAggregator` bridge, which publishes it
here, so a saga waiting on that event advances.

Keep the roles distinct. A mediator notification is addressed to handlers inside
the request's own composition; the aggregator is a broadcast others may join at
runtime. See [mediator.md](mediator.md).

## Example

```csharp
// publish
await events.PublishAsync(new OrderPlaced(orderId), ct);

// subscribe, at startup
app.Services.GetRequiredService<IEventAggregator>()
    .Subscribe<OrderPlaced>(e => logger.LogInformation("Order {OrderId} placed", e.OrderId));
```

## Adapters

Other modules plug into this bus rather than inventing their own:

- `Bsft.Auditing.EventAggregator` — `AddBsftAuditEventAggregator()` publishes every audit event here. See `Bsft.Auditing`.
- `Bsft.Caching.EventAggregator` — `AddBsftCacheInvalidation()` evicts cache entries on invalidation events. See [caching.md](caching.md).
- `Bsft.Localization` — translation cache invalidation. See [localization.md](localization.md).

## See also

- [saga.md](saga.md) — the saga engine subscribes here to drive its flows.
- [messaging.md](messaging.md) — the durable, cross-process counterpart.
