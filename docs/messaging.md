# Bsft.Messaging

Integration events: messages that leave the process, delivered through a
transactional outbox so a published event and the state change it describes can
never disagree, and received through an inbox so a redelivery does not do the
work twice.

| Package | Contents |
| --- | --- |
| `Bsft.Messaging.Abstractions` | Contracts only — `IIntegrationEvent`, `IntegrationEventMessage`, the `IMessageBus`/`IMessageSource` transport seam, `IOutboxStore`, `IInboxStore`, `IIntegrationEventSerializer`, the correlation seams and the handler interface. |
| `Bsft.Messaging` | In-memory bus, event staging, the payload serializer, the outbox dispatcher, the inbox, the loopback transport and the consumer host. |
| `Bsft.Messaging.EFCore` | The outbox and inbox tables, the interceptor and the EF Core stores. |
| `Bsft.Messaging.Correlation` | Bridge to `Bsft.Correlation`, so the staging request's id travels with the message. |
| `Bsft.Messaging.RabbitMQ` | The RabbitMQ transport, both halves: confirmed publishing, topology by convention, and a message source feeding the consumer host with manual acks. |

`samples/Bsft.Sample.WebApi` runs the whole loop in one process and is the
shortest readable answer to what the registration order below has to be.

## Integration events are not domain events

```csharp
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}
```

An integration event is serialized, stored and delivered to other services, so
its shape is a **published contract** that must stay backwards compatible. A
domain event on the event aggregator is in-process and free to change with the
code that raises it. Do not reuse one type for both roles. See
[event-aggregator.md](event-aggregator.md).

An event that only exists to ride the outbox to its own handler — a transport
detail, not a contract anyone outside the framework should see — implements
`IInternalIntegrationEvent` (which extends `IIntegrationEvent`). It travels the
outbox and consumer machinery unchanged, but external fan-out consumers such as
the webhook strand skip it; an application that wants an outside subscriber
publishes a real domain event for that instead.

`OccurredAt` is the instant of the state change, not the instant of publication —
the two differ by however long the message waited in the outbox. This holds
everywhere the field appears and is not repeated below.

## Why an outbox

Writing to the database and publishing to a broker are two systems. Without a
shared transaction, a crash between them either loses the event or publishes one
for a change that rolled back.

The outbox removes the second system from the critical path. The event is written
to a table **in the same transaction** as the business change; a background
dispatcher reads that table afterwards and publishes. Either both land or
neither does.

The flow is: `Enqueue` → interceptor writes rows inside `SaveChanges` →
`OutboxDispatcher` leases a batch → wraps each row in an `IntegrationEventMessage`
→ publishes → marks processed.

## The wire format

What leaves the process is an `IntegrationEventMessage` — an envelope around the
serialized event, not the bare event. It is a **published contract**: a consumer
written against another framework, in another language, reads these fields.

```csharp
public sealed record IntegrationEventMessage
{
    public const int CurrentVersion = 1;

    public int MessageVersion { get; init; } = CurrentVersion;
    public Guid EventId { get; init; }
    public string EventName { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; }
    public string? CorrelationId { get; init; }
    public string Payload { get; init; } = string.Empty;
}
```

| Field | Meaning |
| --- | --- |
| `MessageVersion` | Version of *this contract*, currently `1`. A consumer refuses a version it was not written for. |
| `EventId` | Identity of the event. Stable across redeliveries — deduplicate on it. |
| `EventName` | The registry name, e.g. `orders.placed.v1`. It defaults to the CLR full type name, which is exactly why a published contract should declare one explicitly — see [naming events](#naming-events). |
| `OccurredAt` | Instant of the state change. |
| `CorrelationId` | Correlation id of the staging request; `null` when there was none. |
| `Payload` | The serialized event. Version 1 defines this as JSON. |

`MessageVersion` versions the message contract, not the payload. It goes up only
when a field changes meaning or a field consumers must understand is added. A
payload change consumers cannot absorb gets a **new event name** instead — which
is what the `.v1` suffix in the naming convention is for.

The payload is carried through byte for byte from the outbox row. Nothing
re-encodes it between the transaction and the wire, so what a consumer reads is
what the transaction committed.

A broker adapter maps these fields onto the transport's own metadata rather than
nesting the whole message in the body, so broker tooling and foreign consumers
can read them without parsing the payload. The AMQP mapping is
[in the RabbitMQ section](#envelope-to-amqp).

## Correlation

`Bsft.Messaging` does not reference `Bsft.Correlation` — that would pull the
ASP.NET Core request pipeline into a background component. The bridge is its own
package:

```csharp
services.AddBsftCorrelation();
services.AddBsftMessagingCorrelation();
```

With it, the `OutboxInterceptor` stamps the ambient correlation id onto the row
**at staging time**, because that is the last moment it exists: the dispatcher
that publishes the row runs later and knows nothing about the request. On the
consuming side the id is restored around the whole delivery, so the handler's
logging scope, audit trail and outgoing calls carry it — and an event the handler
stages itself inherits it, which keeps a chain of events one story.

Application code reads the current id through `IMessageCorrelationAccessor`.
Inside a delivery that answers with the id restored from the message, ahead of
the one the delivery's own scope would otherwise mint.
`IMessageCorrelationScope` is the writing half and belongs to the transport.

`AddBsftMessagingCorrelation` uses a plain `Add` rather than the `TryAdd` most of the
registrations below use, so it replaces the ambient-only default whichever order
the two are called in — it is not subject to the ordering rule in the next
section.

Without the bridge nothing breaks: staged events carry no id except the one of a
message currently being handled.

## Setup

```csharp
services.AddBsftMessagingLoopback();          // or a broker adapter — see below
services.AddBsftMessaging(o => o.AddEvent<OrderPlacedEvent>("orders.placed.v1"));
services.AddBsftIntegrationEventHandler<OrderPlacedEvent, OrderPlacedHandler>();
services.AddBsftMessagingCorrelation();           // optional, needs Bsft.Correlation

services.AddBsftInboxEFCore<AppDbContext>();  // before the host
services.AddBsftConsumerHost(o => o.ConsumerName = "orders");

services.AddDbContext<AppDbContext>((sp, options) => options
    .UseSqlServer(connectionString)
    .AddInterceptors(sp.GetRequiredService<OutboxInterceptor>()));

services.AddBsftMessagingEFCore<AppDbContext>(o => o.PollInterval = TimeSpan.FromSeconds(2));
```

### The registration order is load-bearing

The replaceable services register with `TryAdd`, so **the first registration of
one wins** and a call that supplies a default must come *after* any call that
replaces it. Exactly two pairs matter, and this is the sharpest edge in the whole
feature:

| Call this first | …or this takes the registration | Symptom if you get it wrong |
| --- | --- | --- |
| `AddBsftMessagingLoopback` (or a broker adapter) | `AddBsftMessaging` registers `InMemoryMessageBus` as `IMessageBus` | Publishes are handled inline, so there is no inbox and no parking — and the consumer host, which still starts, sits on a source nothing ever publishes to. |
| `AddBsftInboxEFCore<TContext>` | `AddBsftConsumerHost` registers the in-memory `IInboxStore` | **Silent.** Everything runs; the consumer merely forgets what it has processed every time the process restarts. |

The second is the one to watch, because nothing about it is loud. A durable
inbox that quietly became an in-memory one looks exactly like a working system
until a deployment replays a backlog.

Both rows are about *service* registrations. Since 1.3.5 the **options** no longer
have an order at all: every `Action<…Options>` a call takes is applied through
`services.AddOptions<BsftMessagingOptions>().Configure(…)`, so a later call refines
an earlier one instead of being discarded, and `AddBsftNotifications` no longer has
to follow `AddBsftMessaging` to declare its delivery event.

### What each call registers

| Call | Package | Registers |
| --- | --- | --- |
| `AddBsftMessaging` | `Bsft.Messaging` | The options, `TimeProvider`, the type registry, the payload serializer, the handler invoker, `InMemoryMessageBus`, the correlation seams and the scoped event collector and buffer. **Not** the outbox store or the dispatcher. |
| `AddBsftMessagingLoopback` | `Bsft.Messaging` | `LoopbackMessageTransport` as both `IMessageBus` and `IMessageSource`. |
| `AddBsftInbox` | `Bsft.Messaging` | The consumer options, the in-memory inbox and the inbox-guarded delivery. Called by `AddBsftConsumerHost`; call it directly only to use the inbox without the host. |
| `AddBsftConsumerHost` | `Bsft.Messaging` | `AddBsftInbox`, the consumer telemetry, the decoder, the consumer and the `IntegrationEventConsumerHost` hosted service. |
| `AddBsftIntegrationEventHandler<TEvent, THandler>` | `Bsft.Messaging` | One scoped handler. Several per event are allowed. |
| `AddBsftOutbox<TContext>` | `Bsft.Messaging.EFCore` | The `OutboxInterceptor` and the `IOutboxDbContext` mapping, without the dispatcher. For a host where another component owns delivery. |
| `AddBsftMessagingEFCore<TContext>` | `Bsft.Messaging.EFCore` | `AddBsftOutbox`, the leasing store, the dispatcher options and the `OutboxDispatcher` hosted service. |
| `AddBsftInboxEFCore<TContext>` | `Bsft.Messaging.EFCore` | The `IInboxDbContext` mapping, `EFCoreInboxStore` and the `InboxSweeper` hosted service, which does nothing until `InboxRetention` is set. |
| `AddBsftMessagingCorrelation` | `Bsft.Messaging.Correlation` | The bridge to `ICorrelationContext`. |
| `AddBsftRabbitMQMessaging` | `Bsft.Messaging.RabbitMQ` | Both halves of the transport seam. |

Adding the interceptor to the `DbContext` is a separate step and is easy to
forget; without it, events are staged on every request and never written.

### Naming events

```csharp
o.AddEvent<OrderPlacedEvent>("orders.placed.v1");
```

The name defaults to the CLR full type name. Give an explicit, stable name
whenever the type might later be renamed or moved: outbox rows written under the
old name are still waiting to be delivered when the new code starts, and a
registry that no longer knows the name cannot deserialize them. Two types
claiming the same name is a startup error.

Every **consumed** event must be declared here too. The decoder resolves an
incoming event name through this registry, and an event the host was not told
about is parked rather than handled. Nothing resolves an arbitrary CLR type from
the wire.

## The DbContext

```csharp
public interface IOutboxDbContext
{
    DbSet<OutboxMessageEntity> OutboxMessages { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IInboxDbContext
{
    DbSet<InboxMessageEntity> InboxMessages { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

A producing application implements the first, a consuming one the second, and
most implement both on the same context. That is the point: the outbox row is
written by the same `SaveChanges` as the change that raised it, and the inbox
mark by the same `SaveChanges` as the work that handling produced. Moving either
table into a database of its own gives up exactly that.

Apply `OutboxMessageEntityConfiguration` and `InboxMessageEntityConfiguration` in
`OnModelCreating` and add a migration for each table.

`OutboxMessageEntityConfiguration` adds the composite index
`(ProcessedAt, DeadLetteredAt, LeasedUntil, NextAttemptAt, Id)` that the claim
query runs on — without it the dispatcher table-scans on every poll.
`InboxMessageEntityConfiguration` adds the unique index `(Consumer, EventId)`,
which is what settles a race the read before a delivery cannot.

## Publishing

Staging happens through the scoped collector, in the same unit of work as the
business write:

```csharp
public sealed class PlaceOrderHandler(AppDbContext db, IIntegrationEventCollector events)
    : ICommandHandler<PlaceOrder, Guid>
{
    public async Task<Guid> HandleAsync(PlaceOrder command, CancellationToken ct = default)
    {
        var order = new Order { OrderNumber = command.OrderNumber };
        db.Orders.Add(order);

        events.Enqueue(new OrderPlacedEvent { OrderNumber = command.OrderNumber });

        await db.SaveChangesAsync(ct);   // order row and outbox row commit together
        return order.Id;
    }
}
```

Stage before the save. The interceptor drains the collector from inside
`SaveChanges`, so anything staged afterwards waits for whichever save happens
next — or for none.

## Consuming

```csharp
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken ct = default);
}
```

Handlers are scoped and resolved per delivery. **Several handlers for one event
run one after another, in registration order** — never at the same time. They
share the delivery's single scope, and with the EF Core inbox that scope's
`DbContext` is exactly what they are documented to write through; a `DbContext`
permits one operation at a time, so overlapping handlers would fail every attempt
of the delivery and park an event nobody ever handled. A handler that throws does
not prevent the ones after it from running, and the failures are reported together
as `MessagePublishException`. Only the caller withdrawing the delivery — a host
shutting down — stops the remaining handlers, and a withdrawal supersedes a
failure already recorded: it surfaces as cancellation rather than as a publish
failure, so a restart that coincides with a broken handler does not spend one of
the delivery attempts that end in parking. The redelivery meets the same handler
and the same failure. Zero handlers is not an error: an event may exist for
consumers in other services only.

The concurrency worth having is therefore between messages, not inside one. A
handler that wants to fan out does so itself, in scopes of its own.

**Delivery is at-least-once.** A message is marked processed only after it has
been published, so a crash in between redelivers it. That is what the inbox is
for.

### Two consuming paths

| | `InMemoryMessageBus` | `IntegrationEventConsumerHost` |
| --- | --- | --- |
| Where handling happens | inside `PublishAsync` | in a background service |
| Inbox | none | yes |
| Retry / parking | outbox retries the whole message | bounded attempts, then parked |
| Needs | nothing | an `IMessageSource` |

The bus is the default and stays the simplest option for a single service. The
host is what a broker deployment uses, and `AddBsftMessagingLoopback` gives a
single-process deployment the same pipeline so it behaves identically once a
broker is put in.

The loopback's queue is in memory and unbounded, so a publish returns as soon as
the message is queued: an outbox draining a large backlog pulls all of it into
memory rather than being held back, and anything queued but not yet consumed is
lost if the process stops. A broker's own flow control and durability are what
remove both, which is the point at which to stop using the loopback. Parked
messages are kept on `LoopbackMessageTransport.ParkedMessages`, bounded to the
most recent `ConsumerOptions.ParkedMessageCapacity` — a hundred by default, the
oldest dropped to stay there — which is what a transport without a dead-letter
queue can offer instead of one. The bound is there because the list lives as long
as the process does: a misrouted stream of messages would otherwise be a memory
leak with a diagnostic excuse. Raise it where a burst of parking has to stay
inspectable in full, at the cost of holding those whole messages in memory.

### The inbox, and what "effective-once" means

Delivery is at-least-once and cannot be anything else — the guarantee is a
property of the network, not of the code. What the inbox adds is **structural
idempotency**: a record, keyed by consumer and event id, of what has already been
processed, consulted before the handlers run. At-least-once delivery plus that
record is *effective-once* processing, and the two words are not
interchangeable. Nothing here makes a duplicate impossible; it makes a duplicate
harmless without every handler having to arrange that for itself.

`ConsumerName` has no default and the host refuses to start without one. It is
half of the inbox key, so it decides what counts as the same delivery: two
different services consuming one event need different names, two instances of
one service need the same name. Treat it like a database name — deriving it from
the assembly or host name would make a rename silently re-process the backlog.

```csharp
public interface IInboxStore
{
    Task<bool> TryBeginAsync(IntegrationEventMessage message, string consumer,
                             DateTimeOffset receivedAt, CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task AbandonAsync(CancellationToken ct = default);
}
```

The store is **scoped**, one instance per delivery, and resolved from the
delivery's own scope. That is the whole mechanism: `EFCoreInboxStore` reaches the
same `AppDbContext` the handlers write through, stages the mark on it in
`TryBeginAsync`, and the `CommitAsync` that follows the handlers writes the mark
and their changes in **one transaction**.

**Check before, commit after.** The inbox is consulted before the handlers run
and marked after they succeed. Where the context is shared there is no window at
all. Where it is not — the in-memory store, or handlers writing somewhere else —
a crash between the handler succeeding and the mark landing redelivers the
event, so a handler still has to tolerate running twice. That ordering is
deliberate: marking first would turn the same crash into an event nobody ever
handled, and a duplicate is recoverable where a silently dropped event is not.

**A handler may call `SaveChanges` itself**, and with two handlers or one that
saves twice it usually has to — a generated key is not available before the
insert. That save commits the staged inbox mark along with the handler's own
writes, which is atomic for everything written by then and nothing after it. If
the delivery then fails, the mark is already durable, and the EF Core store
**deletes it** when the delivery is abandoned, so the redelivery reaches the
handlers instead of being refused as a duplicate. The delete names one row by
consumer and event id, runs on the same context — an open transaction of the
handler's therefore rolls it back together with the mark it was undoing, which is
the correct outcome — and is logged at warning level, because a handler saving
mid-delivery is worth seeing even when it is deliberate.

What that costs: the work the saving handler committed is **not** rolled back,
so the redelivery meets it again. A handler that saves early has to tolerate
running twice, exactly as it would without an inbox at all. What it buys is that
the failure stays a *retry* rather than becoming silent suppression.

Two residues are worth stating plainly rather than implying. If the compensating
delete itself fails — the database has gone away between the handler's commit and
the abandon — it is logged at **error** level with the event id, the consumer and
the sentence that says what it means: the row is still there, every redelivery of
that event is now acknowledged as a duplicate, and the handlers that failed never
run again until somebody deletes the row by hand. It is not rethrown, because the
delivery fails either way and rethrowing would replace the handler failure that
explains it with a database error that explains only the cleanup. And a process
that **crashes** between a handler's mid-delivery commit and the abandon leaves
the same row behind with nobody to log anything; the window is small and the
remedy is the same. Both disappear entirely for handlers that leave saving to the
framework.

**The in-memory store's record is bounded.** It keeps the most recent
`ConsumerOptions.InboxLedgerCapacity` completed deliveries process-wide, 100 000
by default — a few megabytes — and drops the oldest to stay there, because a
record per delivery that is never dropped is a leak sized by throughput rather
than by anything going wrong. The duplicate-detection window is therefore that
many deliveries rather than the whole life of the process: an event redelivered
after that many later ones have been recorded is handled again. It is the same
trade a restart already makes and a far rarer one, and the eviction is
age-ordered precisely so that a redelivery still in flight is refused by the
record it belongs to. Lowering the capacity narrows that window in exchange for
memory; where neither the window nor the restart is acceptable, use the EF Core
store.

**The EF Core store keeps its record until a retention expires it.** Left alone
it keeps every row forever, which is a table that grows with throughput and
nothing else. Setting `ConsumerOptions.InboxRetention` starts the `InboxSweeper`
hosted service that `AddBsftInboxEFCore` registers, which deletes this consumer's
records once they are older than that.

The retention **is the duplicate-detection window**, and that is the whole of the
decision. A row is the only thing that refuses a redelivery of its event, so a
redelivery arriving more than `InboxRetention` after the delivery it repeats
finds nothing and reaches the handlers a second time. Size it against the longest
redelivery the deployment can produce — a broker's own retry topology, a message
parked and replayed by hand, a backlog drained after a long outage — and not
against what the table costs to keep. There is no default for the same reason: an
oversized table is a problem an operator notices, while a window that turned out
to be too short is a duplicate nobody notices until it has been paid for.

What the pruner deletes is very nearly the rows the table has: a row exists for a
delivery that **completed**, because the store stages the claim and only the
commit after the handlers succeeded writes it — or, for as long as one delivery
takes, for a delivery whose handler saved early. A sweep only reaches a row once
it is older than the retention, which is longer than a delivery by orders of
magnitude, so there is still nothing to check beyond the age.
Only rows carrying this consumer's `ConsumerName` are touched, because the table
may be shared and this host knows nothing about anybody else's retention.

A sweep runs at the retention itself, capped at one hour, so a row outlives its
retention by at most one sweep and a retention of days does not become a single
enormous delete. It is one set-based `DELETE` per sweep, and a failed one is
logged and retried by the next rather than taking the service down. The predicate
is led by the consumer and so served by the existing unique index; there is
deliberately no index on the age, because the sweep runs at most hourly against a
table the sweep itself keeps small, while a second index would be paid for on
every single delivery.

### Failure and parking

| Outcome | What the host does |
| --- | --- |
| Handled, or a redelivery the inbox knew | acknowledge |
| Handlers failed, attempts remain | hand back to the transport |
| Handlers failed, attempts exhausted | park with the failure |
| Unknown event name, unsupported `MessageVersion`, undecodable payload | park at once |

The three refusals are parked **immediately** rather than retried: redelivering
identical bytes to identical code cannot end differently, so spending ten
attempts on them only delays the diagnosis. They reach the host as a
`MessageDecodeException` carrying the `MessageParkReason` that says which of the
three it was; the reason a transport is given is that name followed by the
detail.

Nothing is ever dropped. Every park is logged at error level with the message's
identity and handed to the transport with its reason, which a broker maps to its
dead-letter queue. The host passes the reason on whole; bounding it is the
storing side's job, because only that side knows what it has room for. Because
the reason is derived from an exception and can quote the event, a transport must
also treat it as at least as sensitive as the payload.

Cancellation is treated exactly as on the outbox side: a delivery cancelled by a
token of its own is a **failure** and is retried, while a cancellation of the
host's stopping token exits the loop cleanly and leaves the message uncompleted
for redelivery. Without that distinction one handler timeout would stop the
consumer for the lifetime of the process.

`ConsumerOptions`:

| Property | Default | Meaning |
| --- | --- | --- |
| `ConsumerName` | *(none)* | Inbox identity. Required. |
| `MaxDeliveryAttempts` | `10` | Deliveries before parking. |
| `ReceiveRestartDelay` | 5 s | Pause before re-subscribing to the source. |
| `InboxLedgerCapacity` | `100_000` | Completed deliveries the in-memory inbox keeps. |
| `ParkedMessageCapacity` | `100` | Parked messages the loopback transport keeps. |
| `RetryDelay` | `TimeSpan.Zero` | First interval a failed message is held back for. Doubles per attempt. |
| `MaxRetryDelay` | 30 s | Ceiling of that schedule, and the clamp on what a failure may ask for. |
| `InboxRetention` | `null` | How long the EF Core inbox keeps a record. `null` keeps it forever. |

`MaxDeliveryAttempts` counts against `IMessageDelivery.DeliveryAttempt`, so it
only bounds anything where the transport counts redeliveries — see
[the seam](#the-receiving-half).

### The retry schedule, and who pays for a wait

Both halves of the pipeline compute the wait before the next attempt the same
way, through `IMessageRetrySchedule`:

* an **exponential** interval from a base, doubling once per attempt already
  made,
* **capped** at a ceiling, with a random slice of up to a fifth of each interval
  taken off it, so messages that failed together do not come back together,
* and a wait the failure itself asked for honoured as a **floor**, clamped to the
  same ceiling.

On the consuming side the base is `RetryDelay` and the ceiling is
`MaxRetryDelay`. `RetryDelay` still defaults to zero, so the default consumer
still hands a failed message back at once — the schedule changes nothing until a
base is configured or a failure asks for a wait. Zero is **not** the
recommendation, only what keeps the old behaviour: without a delay a message that
fails deterministically, because the database it needs is failing over or a
downstream service is restarting, burns all ten attempts in milliseconds and is
parked before the thing it was waiting for could have come back.

### A handler can ask to be tried later

```csharp
public interface IRetryAfter          // Bsft.Core
{
    TimeSpan? RetryAfter { get; }
}
```

An exception that implements it says when the work should be attempted again, and
the pipeline reads it off the failure — through an `AggregateException` and
through the layers that wrapped it, taking the longest ask where several were
made. `Bsft.Core`'s `RetryAfterException` is the ready-made one; code that
already has a suitable exception type implements the interface on that type
instead, which is what `Bsft.Notifications.WebPush`'s `WebPushThrottledException`
does with the `Retry-After` of a `429`.

It is a request, not an instruction: it only ever lengthens the wait, and it is
clamped to the ceiling, so a service asking to be left alone for a day does not
remove a message from the pipeline for a day.

### The consumer does not sleep, the transport decides

The consumer holds **one message at a time**, so a wait taken there is a wait for
every message behind it — one throttled recipient would stop everything. It
therefore computes the delay and hands it to the transport with the message:

```csharp
Task RetryAsync(TimeSpan delay, CancellationToken ct = default);
```

The contract is: return promptly, and do not redeliver before the delay has
passed. What that costs is a property of the transport, and each says which of
the two honest answers it gives:

| Transport | How the delay is honoured |
| --- | --- |
| `LoopbackMessageTransport` | Scheduled on a `TimeProvider` timer. Nothing is held; the consumer goes straight back to the queue. A process that stops before the timer fires loses the scheduled redelivery, exactly as it loses whatever is still queued. |
| `Bsft.Messaging.RabbitMQ` | Waited out before the nack, which holds this consumer for the duration. AMQP has no delayed requeue, and the adapter will not declare a dead-letter-plus-TTL topology on a host's broker as a side effect of a failed message. `MaxRetryDelay` bounds the stall; a deployment needing longer waits wants a retry topology. |

The delay is spent on the retry path only — an acknowledged message, a duplicate
the inbox recognised and a parked one are all completed without it.

## Dispatcher options

`OutboxOptions`:

| Property | Default | Meaning |
| --- | --- | --- |
| `PollInterval` | 5 s | Pause after a pass that found nothing. |
| `BatchSize` | `100` | Messages leased per pass. |
| `LeaseDuration` | 1 min | How long a claim is held. |
| `MaxDeliveryAttempts` | `10` | Attempts before dead-lettering. |
| `RetryBackoff` | 5 s | First interval a failed message is held back for. Doubles per attempt; zero retries on the next poll. |
| `MaxRetryDelay` | 5 min | Ceiling of that schedule, and the clamp on what a failure may ask for. |

A pass that processed messages loops immediately; only an empty or faulted pass
waits out `PollInterval`. Each value is validated in the dispatcher's
constructor.

Several instances can run at once: a claim is a conditional update matching on
the lease token and the attempt count, so exactly one instance wins and the
losers move on.

### Draining the outbox on demand

`IOutboxProcessor.ProcessOnceAsync` runs a single pass instead of waiting for the
polling loop. `AddBsftMessagingEFCore` registers it on the same `OutboxDispatcher`
instance the host runs as a background service, so a drain publishes exactly what
the application would have published, through the real lease, retry and parking
policy:

```csharp
var outbox = provider.GetRequiredService<IOutboxProcessor>();
while (await outbox.ProcessOnceAsync() > 0) { }
```

A pass drains at most one batch, which is why the loop repeats until the count is
zero. The count is a diagnostic rather than an assertion target — assert on the
effect the handler had. This is the seam a host test should take; shortening
`PollInterval` and polling until the effect appears turns a deterministic
assertion into a timeout, and the timeout is what fails on a slow machine.

### A failed message is held back, durably

A failed publish releases the lease and writes `NextAttemptAt` on the row: the
instant the schedule above produced. The claim query skips rows whose instant has
not passed, so the wait survives a restart and every instance respects it —
which is the whole reason it is a column rather than something the dispatcher
remembers. It costs nothing while it lasts: the dispatcher spends the interval
draining the rest of the outbox, so the ceiling here is generous where the
consuming side's is short.

Before 1.3.6 the lease was simply cleared, so a failing message was re-leased on
the very next poll and the default ten attempts were spent in under a minute —
long before a database failing over or a service restarting could have come
back. Setting `RetryBackoff` to zero brings that back — with the one difference this
issue exists for: a failure that asks for a wait through `IRetryAfter` is still
obeyed, because the ask is a floor and not a step of the schedule.

### Sizing the lease against the batch

`LeaseDuration` covers a whole batch, not one message. A pass leases everything
it claimed until the same instant and then publishes the messages one at a time,
so each publish gets on average `LeaseDuration / BatchSize` — **600 ms at the
defaults**. Against the loopback transport that is an eternity. Against a broker
where a publish waits for a confirm it need not be, and the tail of a batch is
what runs out of lease first.

Size the pair so that the slowest publish times `BatchSize` stays under
`LeaseDuration`: raise the lease, or lower the batch size. With a RabbitMQ
transport the slowest publish is bounded by `PublisherConfirmTimeout`, so
`BatchSize` × `PublisherConfirmTimeout` < `LeaseDuration` is the conservative
form of the rule.

Getting it wrong costs throughput, not correctness. Once the batch lease has run
out the dispatcher stops the pass instead of publishing under a lease it no
longer holds, logs a warning naming both options, and leaves the remainder
pending for the next poll to claim — the messages are untouched, so no attempt is
spent and nothing is dead-lettered early. The first message of a batch is always
attempted, so a pass makes progress even when claiming the batch itself overran
the lease.

The combination is deliberately not validated at startup. The publish duration
that would decide the threshold belongs to the transport and its broker, which
`Bsft.Messaging` cannot see, and any fixed limit would refuse configurations that
are perfectly sound on a fast bus.

### Failure handling

On a failed publish the attempt count is incremented. Below
`MaxDeliveryAttempts` the lease is released and `NextAttemptAt` is set to the
instant the retry schedule produced, so the message is skipped by the poll query
until then — see [the backoff](#a-failed-message-is-held-back-durably). At the
limit the message is dead-lettered with the failure reason recorded on the row —
truncated to the column's 1000 characters, without splitting a surrogate pair —
and is no longer retried.

A permanently failing message therefore spends its ten attempts over the
schedule's growing intervals and then lands in the dead-letter state, which is
where it should be noticed. Before 1.3.6 there was no backoff at all and the
pacing came from `PollInterval` alone, which spent the same ten attempts in under
a minute.

## Swapping the transport

`IMessageBus` sends:

```csharp
public interface IMessageBus
{
    Task PublishAsync(IntegrationEventMessage message, CancellationToken ct = default);
}
```

The seam takes the message rather than the typed event. A typed contract would
force an adapter to encode the event a second time — risking bytes on the wire
that differ from the ones the transaction agreed to — and would leave it unable
to forward an event whose CLR type it does not reference at all.

`InMemoryMessageBus` is the default. Being both ends of the wire at once, it
decodes the payload through `IIntegrationEventSerializer` and dispatches to the
typed handlers, which is what a broker adapter's consuming side and the consumer
host do too.

### The receiving half

`IMessageSource` receives. An adapter implements both halves, and needs nothing
from the framework beyond these two contracts — it is never called back into by
the host.

```csharp
public interface IMessageSource
{
    IAsyncEnumerable<IMessageDelivery> ReceiveAsync(CancellationToken ct = default);
}

public interface IMessageDelivery
{
    IntegrationEventMessage Message { get; }
    int DeliveryAttempt { get; }

    Task AcknowledgeAsync(CancellationToken ct = default);
    Task RetryAsync(TimeSpan delay, CancellationToken ct = default);
    Task ParkAsync(string reason, CancellationToken ct = default);
}
```

The three completions mirror the producing side one for one — acknowledge is
marking an outbox row processed, retry is abandoning its lease, park is retiring
it.

The host consumes one delivery at a time and completes each before asking for
the next, so the host holding a message *is* the backpressure the transport
sees. How much to prefetch, how to spread messages across instances and when to
reconnect all stay on the transport's side. A stream that simply **ends** is not
an error: the host waits out `ReceiveRestartDelay` and enumerates again, so a
source may treat the end of a stream as its reconnection point. A source that
throws instead is logged in full and restarted the same way. Re-establishing a
*lost* connection is therefore the host's restart loop, not something an adapter
has to build; what an adapter may still have is the client's own recovery within
a connection, which does not end the stream.

`DeliveryAttempt` is one-based and best effort. A transport that cannot count
redeliveries reports `1` every time — and must then bound redelivery itself,
because a host told that every delivery is the first will retry a poison message
forever. This is the one real obligation the seam places on an implementer.

`ParkAsync` must leave the message inspectable. It is the one outcome that ends
a message's life with nobody having handled it, so an adapter that implements it
by discarding turns the host's loudest failure path into the silent drop it
parks to avoid.

The division of labour is the same for every adapter: the transport moves bytes
and settles deliveries; the host decodes the payload, resolves the event name,
deduplicates through the inbox, runs the handlers and counts the attempts. An
adapter that decoded a payload or dispatched to a handler would be doing the
second half twice. The single exception is a delivery that is not an envelope at
all: there is nothing to build an `IMessageDelivery` out of, so the transport
parks it where it stands.

[The RabbitMQ transport](#rabbitmq-transport) below is this contract in AMQP.

## RabbitMQ transport

```csharp
services.AddBsftRabbitMQMessaging(o =>
{
    o.ApplicationName = "orders";
    o.HostName = configuration["Broker:Host"]!;
    o.UserName = configuration["Broker:User"]!;
    o.Password = configuration["Broker:Password"]!;
    o.Subscribe("inventory.reserved.v1");
});

services.AddBsftMessaging(o => o.AddEvent<InventoryReservedEvent>("inventory.reserved.v1"));

services.AddBsftInboxEFCore<AppDbContext>();  // before the host, as always
services.AddBsftConsumerHost(o => o.ConsumerName = "orders");
```

This is the drop-in swap for `AddBsftMessagingLoopback`: it registers **both**
halves of the seam, and the transport is the only thing that changes.
`AddBsftConsumerHost` stays a separate call because a service may publish without
consuming.

The ordering rule does not relax here, and the durable inbox is what a real
broker makes indispensable. `AddBsftInboxEFCore<TContext>` still has to come
before `AddBsftConsumerHost`, or the in-memory inbox takes the registration and
[nothing says so](#the-registration-order-is-load-bearing) — the consumer then
forgets what it has processed on every restart, and redelivery from the broker
replays the backlog against handlers that were told they would only ever see a
message twice by accident.

Each consumed event is named twice and the two mean different things:
`Subscribe` binds the queue to that routing key on the broker, `AddEvent` tells
this process which CLR type the name decodes into. An event bound but not
declared is delivered and then parked as `UnknownEventName`.

`UserName` and `Password` have no defaults. The adapter refuses to start without
them rather than falling back to the broker's well-known development account, so
a host whose configuration binding is wrong fails at startup instead of
connecting as whoever the default happens to be. Everything is validated in
`AddBsftRabbitMQMessaging` itself, not on first use: the first thing to publish
is a background dispatcher, where a missing password would look like a transient
delivery failure being retried rather than the misconfiguration it is.

### Publisher confirms are what make the outbox honest

`OutboxDispatcher` marks a row processed as soon as `PublishAsync` returns. A
publish that returned when the bytes were merely written would let the row be
forgotten while the broker still had every chance to drop the message — and the
row is the only copy.

So the publishing channel is opened with publisher confirmations, and
`PublishAsync` completes only once the broker has taken responsibility for the
message. A rejection, a channel the broker closed under the publish, or a
confirmation that does not arrive within `PublisherConfirmTimeout` is raised as
`MessagePublishException`: a failed attempt, which the outbox records and
retries.

The timeout counts as a failure even though the confirmation may merely be late.
Retrying then costs a duplicate, which at-least-once delivery permits anyway;
treating it as delivered would cost the event.

There is no buffering layer of our own. While the broker is unreachable,
publishes fail and the messages stay in the outbox, which is a durable store that
a transaction already stands behind. A queue in front of the broker's would be a
second store of undelivered messages with no such backing.

A confirmation is awaited on the channel the publish went out on, so two
publishes over one channel wait behind each other. One channel is the default
because the outbox dispatches strictly sequentially and needs no more.
`PublishChannelCount` raises it for the hosts that do publish concurrently:
parking runs on the consumer loop and shares the publishing side, so a host that
both publishes and consumes has two publishers the moment it parks anything, as
does an application calling `IMessageBus` from several requests at once. Channels
are handed out round-robin and every one of them carries the same declared
topology, so which one a publish gets changes nothing but who it waits behind. No
ordering guarantee changes with it, because there is none to change: the outbox
decides the order events are published in, and RabbitMQ only preserves order per
channel anyway.

### Envelope to AMQP

| Envelope field | AMQP |
| --- | --- |
| `EventId` | `message-id` |
| `EventName` | the routing key, and the `type` property |
| `CorrelationId` | `correlation-id` (absent when there is none) |
| `OccurredAt` | `timestamp`, **and** the `bsft-occurred-at` header |
| `MessageVersion` | the `bsft-message-version` header |
| `Payload` | the body, UTF-8 |
| — | `content-type` from `ContentType`, `content-encoding` `utf-8`, `app-id`, delivery mode persistent |

`OccurredAt` is written twice because AMQP's `timestamp` is a whole number of
seconds and the envelope is not. The property is still set, since that is the
field broker tooling displays; the header carries the full precision, so the
envelope a consumer rebuilds is the envelope the producer sent. A consumer
reading a message from a foreign producer that set only the timestamp gets second
resolution, which is the best that message contains.

Messages are persistent. The outbox row is gone the moment the broker confirms,
so a transient message dropped by a broker restart would be an event that nothing
can produce again.

`ContentType` is honoured in both directions. It is stamped onto everything
published, and a delivery that states a *different* media type is refused on the
way in rather than handed to a serializer that cannot read it — the body is not
written in an encoding this application decodes, and refusing it says so plainly
instead of letting it fail later as an undecodable payload. A delivery that states
no content type at all is accepted, because a producer that labels nothing is not
a producer that labels something else. Only the media type is compared, so a
`charset` parameter never causes a rejection.

Publishing is not `mandatory`. An integration event with no queue bound to its
name is normal in publish/subscribe — the consumers may live in services that are
not deployed, or not interested — and asking the broker to return such a message
would turn that into a publish failure retried forever.

### Topology by convention

| Object | Default name | Kind |
| --- | --- | --- |
| Exchange | `{ApplicationName}.events` | `ExchangeType`, durable |
| Dead-letter exchange | `{ExchangeName}.dlx` | topic, durable |
| Queue | `{ApplicationName}.queue` | durable, `x-queue-type` from `QueueType`, `x-dead-letter-exchange` set |
| Dead-letter queue | `{QueueName}.dlq` | durable, bound to the DLX on `#`, no queue type of its own, `x-message-ttl` / `x-max-length` when bounded |

Every name is overridable on its own; a consumer reading another application's
events points `ExchangeName` at that application's exchange. All declarations are
durable and idempotent, and they run at every start and after every reconnect
rather than being a deployment step somebody has to remember.

`ExchangeType` accepts `topic` and `direct`, and rejects everything else. The
routing key is the event name and every binding names one, so those are the two
types that route what this adapter publishes. `topic` is the default because it
is the one that leaves room — a consumer can widen a binding to a family of names
with a pattern later without the producer changing anything — while `direct`
matches the whole key and nothing else, which is all the bindings declared here
ask for. `fanout` would deliver every event to every subscribed queue and
`headers` would deliver none, so both are refused at registration rather than
turning `Subscribe` into something that no longer means what it says.

The dead-letter exchange stays a topic exchange whatever the application's own
exchange is declared as, because its queue is bound on `#` and only a topic
exchange reads that as a pattern.

The routing key is the event name, and the queue is bound once per name passed to
`Subscribe`. The queue belongs to the consuming application, not to the event:
every instance consumes from that one queue, so the instances share the backlog
instead of each receiving every message.

No `x-dead-letter-routing-key` is set, so a rejected message keeps the routing key
it was published under and the dead-letter queue still says which event each entry
was. The DLQ is bound on `#` for the same reason — binding it by event name would
drop exactly the messages whose name nobody thought to list.

A publisher declares only the exchange. Which queues exist is the consumers'
business, and a producer that declared them would recreate a queue an operator had
just deleted.

**Bounding the dead-letter queue.** It is unbounded by default, and a dead-letter
queue nobody drains grows until the broker's disk alarm stops the whole node —
including the exchanges this application publishes to. `DeadLetterQueueMessageTtl`
and `DeadLetterQueueMaxLength` map to `x-message-ttl` and `x-max-length`. Set them
well above the time anyone would take to look at a parked message: what they
discard is the last copy of an event, which is why neither has a default. A full
queue drops from the head, so the oldest parked message goes first.

A queue's arguments are fixed when it is declared, exactly like `QueueType`.
Changing either bound against a broker where the dead-letter queue already exists
fails the declaration with `PRECONDITION_FAILED` over inequivalent arguments
rather than applying it. Applying it means moving anything still in the queue,
deleting the queue, and letting the next start re-declare it.

### Consuming: push turned into pull

RabbitMQ pushes and `IMessageSource` pulls, and the adapter is where that is
reconciled. The client's consumer callback puts each delivery on an in-process
queue and returns; the source reads that queue and yields one `IMessageDelivery`
at a time. The queue is bounded at `PrefetchCount`, which is the number of
messages the broker will have outstanding anyway — so it never becomes a second
buffer in front of the broker's own, and a prefetch raised without thinking
cannot turn into unbounded memory instead of backpressure.

**One enumeration is one subscription.** Starting the stream opens the connection
and subscribes; leaving it cancels the subscription and closes both. A broker
that cannot be reached therefore comes out of the stream as a failure, which the
host's restart loop already handles. Reconnects *within* a live connection are
different: those are the client's, the adapter re-subscribes on the recovery
notification, and the stream does not end. The client's own topology recovery is
switched off, so that one component decides what the topology is.

That re-subscription is the one failure the adapter cannot retry its way out of:
the recovered connection is healthy, so the client reports no further recovery
and nothing would ask a second time. A re-subscription that fails — a refused
topology re-declaration, a dead socket, an RPC past its continuation timeout —
therefore ends the stream with that failure, which puts the restart loop back in
charge: the host waits out `ReceiveRestartDelay` and enumerates again, and that
enumeration subscribes from scratch. Deliveries that were in flight settle on
nothing, because their channel is no longer the one being consumed, and the
broker delivers them again.

Acknowledgement is manual. A message is settled after the host has decided about
it, never before, so a consumer that dies mid-handling leaves the message on the
queue instead of having already told the broker to forget it. That is the
consuming half of at-least-once delivery, and it is what makes `PrefetchCount`
meaningful: the broker counts exactly the messages this consumer has taken but
not yet finished.

| Host completion | AMQP |
| --- | --- |
| `AcknowledgeAsync` | `BasicAck` on the originating channel |
| `RetryAsync(delay)` | `BasicNack(requeue: true)`, after waiting out `delay` |
| `ParkAsync(reason)` | republish to the DLX with `x-park-reason`, then `BasicAck` — see below |

**Settlement goes to the channel the delivery arrived on.** A delivery tag means
something only on that channel and the same number is reused on the next one, so
a settlement whose channel a recovery has replaced is dropped rather than applied
to whichever unrelated message now holds that tag. The message stays unsettled
and the broker delivers it again, which at-least-once delivery already accounts
for.

The one message the host never sees is one that carries no readable envelope: no
event id, neither a `type` property nor a routing key, a timestamp that is not a
representable instant, or a content type this application does not read. There is
nothing to build an `IMessageDelivery` out of, so the transport parks it itself
under `MessageParkReason.UnreadableEnvelope` and does not retry it: the same bytes
would be exactly as unreadable next time.

That reason is a member of the same closed set the host parks under, which is why
`MessageParkReason` lives in `Bsft.Messaging.Abstractions` rather than in the
consumer host: both sides of the transport seam park messages, and a transport
that had to invent a category of its own would put a token into `x-park-reason`
that nothing could enumerate.

### Parking carries its reason

AMQP's own way to dead-letter a message — reject it without requeue — cannot carry
a sentence. All an operator would find in the dead-letter queue is *rejected*,
while the reason the host wrote about why the message can never be handled stayed
behind in a log. So a park is a **republish** of the message to the dead-letter
exchange, under its original routing key, with the reason in an `x-park-reason`
header, followed by an acknowledgement of the original.

The order matters: the copy is confirmed before the original is acknowledged,
because a park that lost the message is precisely the silent drop parking exists
to avoid. That order costs a duplicate in the dead-letter queue when the
acknowledgement is the part that fails — visible, and dismissible. If the
republish itself fails, the adapter falls back to a rejection without requeue: the
message still reaches the same exchange through the broker's own dead-lettering,
and only the reason is lost. That fallback is logged at error level.

The copy is published over the *publishing* channel, which is the one opened with
publisher confirmations, so "preserved" means the broker said so rather than that
the bytes were written. What it does not carry are the `x-death` records the
broker would have added for a rejection. The adapter does not repeat the reason in
its own log, because the consumer host already writes it once next to the
message's identity, and it cuts the header to 1000 characters without splitting a
surrogate pair.

### Counting attempts needs a quorum queue

`IMessageDelivery.DeliveryAttempt` is read from `x-delivery-count`, the header a
quorum queue stamps on a redelivery, plus one. A classic queue does not write that
header at all: every delivery then reports as attempt 1,
`ConsumerOptions.MaxDeliveryAttempts` is never reached, and a message whose
handler always fails is requeued forever instead of being parked.

That is why `QueueType` defaults to `Quorum`. It is a choice about redelivery
counting rather than about replication, and it is the only default under which the
host's attempt budget means anything.

Two things follow from it:

- **A queue's type is fixed at declaration.** Pointing the adapter at a queue that
  already exists as classic fails the declaration with `PRECONDITION_FAILED`
  rather than converting it. Switching means declaring a new queue and draining the
  old one.
- **Choosing `Classic` means bounding redelivery yourself**, with a per-queue
  policy or a delivery limit, because the host cannot. The retry schedule does not
  help here: it slows that loop down, it does not end it. Do it only where quorum
  queues are unavailable. The adapter logs a warning naming the queue every time
  it declares a classic one, because nothing else about that state is visible:
  nothing throws, nothing is dropped, and `MaxDeliveryAttempts` reads as
  configured while never being reached. It is a warning rather than a refusal —
  a node without quorum queues has no alternative, and a deployment that bounds
  redelivery broker-side is not relying on the budget in the first place. Only
  the consuming side says it; a publisher declares no queue.

RabbitMQ 4.x gives quorum queues a default delivery limit of its own, after which
the *broker* dead-letters a message. Keep it above `MaxDeliveryAttempts` so the
host parks a message with a reason before the broker retires it without one. The
adapter sets no `x-delivery-limit`: whose budget applies is the host's decision,
and a limit written into the queue arguments would be a second one, fixed at
declaration time and unchangeable afterwards.

The dead-letter queue is left at the broker's default type. Nothing redelivers
from it, so the one thing the type decides has nothing to decide there.

### Options

| Property | Default | Meaning |
| --- | --- | --- |
| `ApplicationName` | — | Required. Names the connection on the broker and every derived object. |
| `HostName` / `Port` / `VirtualHost` | `localhost` / `5672` / `/` | Where the broker is. |
| `UserName` / `Password` | — | Required; no default on purpose. |
| `UseTls` | `false` | Connect over TLS. Does **not** move `Port` — set it to 5671 yourself. |
| `TlsServerName` | `null` | The name the broker's certificate is checked against. Falls back to `HostName`. |
| `NetworkRecoveryInterval` | 5 s | Pause between reconnect attempts. |
| `Heartbeat` | 60 s | Heartbeat interval. The broker closes the connection after two missed ones. |
| `ContinuationTimeout` | 20 s | Deadline for one protocol call with the broker. Not the confirm deadline. |
| `ExchangeName` … `DeadLetterQueueName` | see above | Naming overrides. |
| `ExchangeType` | `topic` | `topic` or `direct` only; see below. |
| `Subscribe(name)` | none | One queue binding per call. |
| `QueueType` | `Quorum` | Classic queues do not count redeliveries; see above. |
| `DeadLetterQueueMessageTtl` | `null` | `x-message-ttl` on the DLQ. Unbounded when unset. |
| `DeadLetterQueueMaxLength` | `null` | `x-max-length` on the DLQ. Unbounded when unset. |
| `PrefetchCount` | `20` | Unacknowledged messages per consumer. Zero is rejected: AMQP reads it as *no limit*. |
| `PublishChannelCount` | `1` | How many channels publishes are spread over, round-robin. |
| `PublisherConfirmTimeout` | 30 s | How long a publish waits for its confirmation. |
| `ContentType` | `application/json` | What `IIntegrationEventSerializer` produces — and what a consumed message must say. |

Everything numeric is validated by `ValidateOnStart()`: a port outside the TCP
range, a prefetch or a `PublishChannelCount` below one, a non-positive timeout,
heartbeat or dead-letter bound, and an exchange type that does not route on the
routing key all fail the host at startup rather than on the first publish, and
every complaint is reported in one message. The type is `BsftRabbitMQOptions`
since 1.3.5, and `AddBsftRabbitMQMessaging` takes an `IConfiguration` overload
binding section `Bsft:RabbitMQ` — the form to reach for, since `UserName` and
`Password` have no defaults on purpose and do not belong in code.

### TLS

`UseTls` turns the connection into AMQPS and nothing else. It does not move
`Port` — AMQPS listens on 5671 — because the port is stated, not inferred, and a
deployment that binds it from configuration should not be overruled by a flag.
Set both.

`TlsServerName` selects which name the broker's certificate is verified against,
for a broker reached under a name its certificate does not carry: through a load
balancer, a service mesh, or by address. Left unset it is `HostName`. There is no
option that switches verification off.

Which protocol versions are offered and which authorities are trusted are left to
the client library, and therefore to the host's own .NET and operating-system
trust configuration. That is where an operator already manages them for every
other outbound connection the process makes, and a second set of knobs here would
only be a second place to get them wrong.

`RabbitMQMessageFormatException` is the package's one public exception type: it
reports AMQP framing that cannot be read as an envelope.

The transport's singletons are `IAsyncDisposable` only — closing a channel is a
conversation with the broker, not something to block a thread on. The generic host
disposes its provider asynchronously, so this needs no attention there; a manually
built `ServiceProvider` has to be disposed with `await using`.

### Verification limits

There is no broker in development or in CI, so the adapter is verified against the
thin `RabbitMQ.Client` surface it uses rather than against a running instance. The
AMQP property mapping is asserted on the client's own `BasicProperties`, and the
envelope round-trip goes through the byte-array shape the client actually hands
headers back in. The topology declarations, the confirm handling and its timeout,
the publish-failure translation, the settlement decisions and the re-subscribe
after a recovered connection are all covered against fakes of that surface. So is
the whole binding: a delivery pushed through a fake channel travels the real
source, the real consumer host, the real inbox and into a typed handler, and comes
back out as the acknowledgement, requeue or dead-letter publish the broker would
have been told about.

What is **not** verified here, and should be checked once against a real broker
before relying on it:

- That publisher confirmations behave as assumed — that `BasicPublishAsync` on a
  channel created with `publisherConfirmationsEnabled` really completes on the
  broker's ack and faults on a nack. That behaviour is the client's, and it is the
  single assumption the outbox's correctness rests on. Since parking republishes
  over that same channel, it is now also what "the parked copy exists" rests on.
- That the declarations are accepted as written, and that re-declaring them is the
  no-op AMQP promises rather than a `PRECONDITION_FAILED` against objects an
  earlier deployment created differently — in particular that the queue is accepted
  as a quorum queue on the target broker at all.
- That a rejection without requeue reaches the dead-letter queue with its original
  routing key, and that a parked republish reaches it with `x-park-reason` readable
  in the management UI.
- That `x-delivery-count` arrives as assumed on a redelivery from a quorum queue:
  absent on the first delivery, and one lower than the attempt number afterwards.
  The whole attempt budget reads from it.
- That the broker's own delivery limit sits above `MaxDeliveryAttempts`, so the
  host parks a message with a reason before the broker retires it without one.
- That automatic recovery raises the recovery notification this adapter
  re-subscribes on, and that re-subscribing on a recovered channel is accepted.
- Throughput and the effect of a given prefetch under real load, now that the
  prefetch also sizes the in-process queue between the client's callback and the
  consumer host.

The three adapter classes that forward onto `IChannel`, `IConnection` and
`ConnectionFactory` hold no logic beyond that forwarding and are marked
`[ExcludeFromCodeCoverage]`; a test that could reach them would be asserting the
client's behaviour rather than ours.

## Upgrading from 1.3.0

Two schema changes and three contract changes. Applications wiring up through the
extension methods need no code change; the contract changes bite code that
*implements* the seams.

This section covers messaging only. 1.3.1 also changes two things in `Bsft.Data`,
one of which stops a host that compiles from starting:
the 1.3.1 migration notes.

### Schema

**`OutboxMessages` gains one column: `CorrelationId`, `nvarchar(200)`,
nullable.** Add a migration for it; nothing else about the table changed.

Rows written by 1.3.0 have no value there and read back as `null`. They still
dispatch, and their message simply carries no correlation id — the same thing a
new row staged outside a correlated request says. There is no backfill to run and
no migration window: an old and a new dispatcher can drain the same table at the
same time.

The correlation id is bounded to 200 characters. `CorrelationIdMiddleware` accepts
at most 128, so only a custom `ICorrelationContext` can exceed it — and because
the row is written inside the business transaction, an oversized value fails that
transaction rather than being silently shortened.

**`InboxMessages` is an entirely new table.** It is needed only by a host that
adds `AddBsftConsumerHost`; without one, nothing reads or writes it and no
migration is required. A host that does consume must apply
`InboxMessageEntityConfiguration`, add the migration, and implement
`IInboxDbContext` on the context the handlers write through — the last is what
makes the mark and the handler's own changes one transaction, and the table is of
limited use without it.

The migration has to carry the **unique index on `(Consumer, EventId)`**, not
only the columns. It is not a lookup optimisation but the deduplication itself:
the read that precedes a delivery can find nothing in both of two racing
deliveries, and the unique index is what makes the database refuse the second
insert instead. A hand-written migration that leaves it out, or leaves out its
uniqueness, produces a table that looks right and deduplicates only when the two
deliveries happen not to overlap. Generating the migration from
`InboxMessageEntityConfiguration` gets it for free.

### Contracts

- **`IMessageBus.PublishAsync` takes an `IntegrationEventMessage`** instead of a
  typed event. A custom bus implementation has to be updated; application code
  that only stages events through `IIntegrationEventCollector` is untouched.
- **`OutboxInterceptor` gained two constructor parameters**,
  `IIntegrationEventSerializer` and `IMessageCorrelationAccessor`. **`OutboxDispatcher`
  lost one**, the type registry it no longer needs now that it does not deserialize
  anything. Both are registered by the extension methods.
- **`InMemoryMessageBus` gained three constructor parameters**,
  `IIntegrationEventSerializer`, `IMessageCorrelationScope` and
  `IIntegrationEventHandlerInvoker`. It went from two parameters to five. Running
  the handlers moved into that last seam so the bus and the new consumer host
  share one implementation of it rather than drifting apart. Code that constructs
  the bus by hand — a test, typically — has to be updated.

### New, and additive

The consuming side — `IMessageSource`, the inbox and `AddBsftConsumerHost` — is
new in 1.3.1 and changes nothing for a host that does not add it.

`ConsumerOptions` carries three tuning options whose defaults change nothing:
`ParkedMessageCapacity` is the bound the loopback's parked list already had as a
constant, `RetryDelay` defaults to zero and so retries exactly as before, and
`InboxRetention` defaults to unset and so keeps every inbox record as before.
`AddBsftInboxEFCore` does register one more hosted service for the last of them,
the `InboxSweeper`, which reads the retention at startup and returns immediately
while it is unset — no timer and no query.

`IIntegrationEventSerializer` is new too. It is the single definition of the
payload encoding, used by `OutboxInterceptor` when staging and by the consuming
side when decoding, so producer and consumer cannot disagree about the format.
`Deserialize` reports a malformed payload as an `InvalidOperationException`: the
consumer host parks on that contract, and it must not have to know which encoding
produced a message to recognise a broken one.

`Bsft.Messaging.RabbitMQ` is likewise new. One default in it is worth knowing
before a first deployment rather than after: **`QueueType` defaults to `Quorum`**,
because only a quorum queue writes the `x-delivery-count` the attempt budget reads
from. Nothing has been released against a classic queue, so this bites only a
deployment that points `QueueName` at a queue somebody else created as classic —
where the declaration fails with `PRECONDITION_FAILED` rather than converting it.
See [the section above](#counting-attempts-needs-a-quorum-queue).

The 1.3.1 changes outside messaging are
the 1.3.1 migration notes; upgrading from 1.2.x is
the 1.3 migration notes.

## The satellite packages

### Bsft.Messaging.Correlation

`Bsft.Messaging.Correlation` joins the correlation family to the messaging family, and exists so that
neither has to reference the other: the messaging packages must not drag the ASP.NET Core request
pipeline into a background dispatcher. `AddBsftMessagingCorrelation()` registers one service, a scoped
`IMessageCorrelationAccessor`, with a plain `Add` rather than `TryAdd` so that it replaces the
ambient-only default of `AddBsftMessaging` whichever way round the two calls are made. It has no options
type and nothing to configure. What it decides is one precedence: the ambient id of a message currently
being handled wins over the scope's own `ICorrelationContext`, because a handler that stages the next
event has to keep the correlation of the event that caused it — without that, a chain of integration
events starts a new correlation on every hop, since each delivery runs in a fresh service scope whose
context mints a fresh id.

### Bsft.Messaging.EFCore

`Bsft.Messaging.EFCore` is where messaging becomes durable. It has three entry points on one options
surface. `AddBsftOutbox<TContext>()` registers `OutboxInterceptor` and maps `IOutboxDbContext` onto the
application's context, which is the staging half on its own. `AddBsftMessagingEFCore<TContext>()` adds
the leasing `IOutboxStore` and the `OutboxDispatcher` hosted service on top of it, and takes an
`Action<OutboxOptions>` that configures `BsftMessagingOptions.Outbox` — the same object the core package
documents, bound from section `Bsft:Messaging`. `AddBsftInboxEFCore<TContext>()` registers the durable
`IInboxStore` against `IInboxDbContext` and the `InboxSweeper` hosted service, which reads
`BsftMessagingOptions.Consumer.InboxRetention` and does nothing at all while that is unset.

The package declares no options type of its own; `IOutboxDbContext` and `IInboxDbContext` are its two
seams, and they stay here rather than in the abstractions because their signatures name `DbSet<>`. Two
things a host still has to do itself: add the interceptor to the context with `AddInterceptors`, and
apply `OutboxMessageEntityConfiguration` and `InboxMessageEntityConfiguration` in `OnModelCreating`
with a migration for the `OutboxMessages` and `InboxMessages` tables. `OutboxInterceptor` and
`InboxSweeper` moved into `Bsft.Messaging.EFCore.Services` in 1.3.5, so a host that resolves the
interceptor by name needs that `using`.

### Bsft.Messaging.EFCore.Postgres

`Bsft.Messaging.EFCore.Postgres` closes the gap between a commit and the drain that follows it. On
PostgreSQL the outbox does not have to be polled to be noticed: a save that wrote outbox rows
announces itself with `pg_notify`, and a listening worker drains the outbox the moment it hears that.
`AddBsftMessagingEFCorePostgres<TContext>()` registers both halves — the `OutboxNotifyInterceptor` a
host adds to its context next to `OutboxInterceptor`, and the listening background worker — and takes
an `Action<BsftMessagingPostgresOptions>` or an `IConfiguration` binding section
`Bsft:Messaging:Postgres`.

It is opt-in, it adds nothing to the delivery guarantee, and polling stays on. The drain a
notification triggers is the dispatcher's own `IOutboxProcessor.ProcessOnceAsync`, so every message
still goes through the same lease, the same retry schedule and the same dead-letter policy, and two
instances woken by the same notification share the backlog exactly as two polling instances do. A
notification that is never sent, never received, or lost with the connection it was travelling on
costs one `PollInterval` and no message — which is why a host that wires this may lengthen that
interval and should not switch it off.

Three details are worth knowing. The announcement is made **after** the save, from the context that
wrote the rows: inside a transaction PostgreSQL queues the notification and delivers it on commit, so
a rollback takes it back, and outside one the rows are already written. The payload is empty, because
a notification says only that the outbox has something in it and the lease decides what; carrying row
identities would put a second, weaker source of truth beside the lease. And the listener opens a
connection of its own rather than borrowing one from the context's pool, because a listening
connection is held for the lifetime of the host and idle almost all of it.

The channel name is the one value that reaches the server as an identifier rather than as a parameter
— `LISTEN` takes no parameter — so it is validated at start-up against a narrow grammar: lower-case
letters, digits and underscores, starting with a letter, at most 63 characters. Every writer and
every listener of one database has to name the same channel.

### Bsft.Messaging.RabbitMQ

`Bsft.Messaging.RabbitMQ` supplies both halves of the transport seam — `IMessageBus` and `IMessageSource`
— and knows nothing else about the framework: it references only `Bsft.Messaging.Abstractions`, so the
inbox, the type registry and the handlers are invisible to it. `AddBsftRabbitMQMessaging` takes either an
`Action<BsftRabbitMQOptions>` or an `IConfiguration` binding section `Bsft:RabbitMQ`, and must be called
**before** `AddBsftMessaging`, which registers the in-process bus with `TryAdd`. Registration resolves
two singletons from the options once — `RabbitMQConnectionSettings` and `RabbitMQTopology` — so nothing
re-derives an exchange name or re-checks a port later. Its own `IRabbitMq*` interfaces are `internal` and
exist so the property mapping, the confirm handling and the acknowledgement decisions are testable
without a broker; a host that wants different behaviour replaces the whole adapter rather than one of
them.

### Bsft.Messaging.Http

`Bsft.Messaging.Http` is the read surface of the family, and the second kind of satellite: a live
implementation a dependency-light consumer must be able to do without. `Bsft.Messaging` is what a worker
references, and it carries no ASP.NET Core framework reference; an operator console needs one, so the
routes live here and the arrow goes one way. `AddBsftMessagingHttp` registers the options and the
payload read, `MapBsftMessaging` maps what the host can actually answer:

| Route | Answers | Permission |
| --- | --- | --- |
| `GET {prefix}/outbox` | A page of outbox metadata: which event, when, how often it was tried, what the last failure said. | `messaging.read` |
| `GET {prefix}/outbox/{id}` | One message with the payload it carries. | `messaging.read-content` |
| `GET {prefix}/inbox` | A page of the deduplication ledger: which consumer has already handled which event. | `messaging.read` |

The grading is [package-conventions](package-conventions.md) section 5a: the listings say what is in the
tables, the payload route says what a message contains, and those are not the same permission. The
payload route is mapped only once `ReadContentPolicy` names who may reach it, answering it records a
`messaging.outbox.read-content` activity, and `MapBsftMessaging` refuses a content policy with no
`IAuditRecorder` behind it rather than disclosing a payload unaudited. The inbox half keeps a single read
permission because the ledger holds no payload to grade apart, and its listing is mapped only where an
`IInboxMessageReader` is registered.

The parked messages are in the outbox, not the inbox. A row with a `DeadLetteredAt` is one the dispatcher
gave up on, a row with a `ProcessedAt` was published, anything else is pending — the state is derived
from those timestamps rather than stored, so the listing cannot disagree with [failure and
parking](#failure-and-parking) above. What a consumer could not handle is not parked in the inbox at all;
it goes to the transport's dead-letter queue, which is where it is inspected.

Neither table carries a tenant column — they are written inside whichever database the host's own
`DbContext` is bound to — so the database is the scope of this surface, and `ReadPolicy` is where a host
says who may see all of it. Ordering is by identity descending, which for an append-only table is newest
first, and paging is by offset through the shared `PagedRequest` / `PagedResult` vocabulary of
`Bsft.Data.Contracts`. The readers behind it, `IOutboxMessageReader` and `IInboxMessageReader`, are
declared in `Bsft.Messaging.Abstractions` and implemented in `Bsft.Messaging.EFCore`, so a host storing
the tables elsewhere serves the same routes by implementing two interfaces.

## See also

- `Bsft.Auditing` — the same outbox pattern, applied to audit events.
- [event-aggregator.md](event-aggregator.md) — the in-process alternative.
- [correlation.md](correlation.md) — where the correlation id comes from.
