# Bsft.Messaging.EFCore

The durable half of the `Bsft.Messaging` family: the transactional outbox written by an EF Core
interceptor in the same `SaveChanges` as the state change, the leasing dispatcher store behind it, and the
inbox whose mark commits together with what the handlers wrote.

## Install

```bash
dotnet add package Bsft.Messaging.EFCore
```

Requires `Bsft.Messaging` (and its abstractions transitively), `Bsft.Core` for the polling loop, and
`Microsoft.EntityFrameworkCore` with `Microsoft.EntityFrameworkCore.Relational`. The application's own
`DbContext` implements `IOutboxDbContext` and `IInboxDbContext`; that is the whole point of this package,
because a row in the application's context is a row in the application's transaction.

## Register

```csharp
services.AddBsftMessaging(configuration, o => o.AddEvent<OrderPlaced>("orders.placed.v1"));

// producing side: interceptor, leasing store and the dispatcher
services.AddBsftMessagingEFCore<AppDbContext>(outbox => outbox.PollInterval = TimeSpan.FromSeconds(2));

// consuming side: the durable inbox and its pruner, before AddBsftConsumerHost
services.AddBsftInboxEFCore<AppDbContext>();
services.AddBsftConsumerHost(consumer => consumer.ConsumerName = "orders");
```

Two more things are required. The interceptor has to be added to the context —
`options.AddInterceptors(sp.GetRequiredService<OutboxInterceptor>())` — or staged events are silently
never written; and `OutboxMessageEntityConfiguration` and `InboxMessageEntityConfiguration` have to be
applied in `OnModelCreating` with a migration for the `OutboxMessages` and `InboxMessages` tables.
`AddBsftOutbox<TContext>` registers the staging side alone, for a host where another component owns
delivery. `AddBsftInboxEFCore` must come before `AddBsftConsumerHost`, which registers the in-memory
inbox with `TryAdd`; getting that order wrong is not loud, the consumer simply forgets what it has
processed on every restart.

## Configure

This package has no options type of its own. `AddBsftMessagingEFCore` configures
`BsftMessagingOptions.Outbox` and the inbox pruner reads `BsftMessagingOptions.Consumer.InboxRetention`,
both documented in [`Bsft.Messaging`](../Bsft.Messaging/README.md) and bound from section
`Bsft:Messaging`. The two column widths a host may need when writing a migration by hand are
`OutboxMessageEntityConfiguration.EventTypeNameMaxLength` (500),
`OutboxMessageEntityConfiguration.FailureReasonMaxLength` (1000, the shared
`MessageReasonLimits.MaxReasonLength`), `OutboxMessageEntityConfiguration.CorrelationIdMaxLength` (200),
`InboxMessageEntityConfiguration.ConsumerMaxLength` (200) and
`InboxMessageEntityConfiguration.EventNameMaxLength` (500).

Validation: none of its own; the shared messaging options are validated by `ValidateOnStart()`, and the
pruner additionally refuses a retention that is set and not positive when it is constructed outside the
container.

## Extend

`IOutboxDbContext` and `IInboxDbContext` are the two seams this package declares, and they stay here
rather than in `Bsft.Messaging.Abstractions` because their signatures name `DbSet<>`, which that
package's dependency closure does not allow. Implement them on the application's `DbContext`; that is the
only extension point this package expects a host to use.

Everything else it supplies is a default implementation of a contract declared in
[`Bsft.Messaging.Abstractions`](../Bsft.Messaging.Abstractions/README.md): `EFCoreOutboxStore` for
`IOutboxStore`, whose every state transition is one conditional statement so the database settles the
race between two instances, and `EFCoreInboxStore` for `IInboxStore`, which stages the mark on the
handlers' own context so one `SaveChanges` commits both. `OutboxInterceptor` and `InboxSweeper` are
concrete by design and are replaced by not registering them.

## Documentation

Topic page: [docs/messaging.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/messaging.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
