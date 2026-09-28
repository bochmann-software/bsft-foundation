# Bsft.Messaging.EFCore.Postgres

The PostgreSQL wake-up for the `Bsft.Messaging` EF Core outbox. A commit that wrote outbox rows
announces itself with `pg_notify`; a listening worker hears it and drains the outbox at once, instead
of the dispatcher waiting out its poll interval.

It is an optimisation and nothing more. The polling dispatcher stays registered and stays the
guarantee: a notification that is never sent, never received, or lost with the connection it was
travelling on costs one poll interval and no message. The lease, the retry schedule and the
dead-letter policy are untouched — the drain a notification triggers is the dispatcher's own
`IOutboxProcessor.ProcessOnceAsync`, so two instances woken by the same notification share the
backlog exactly as two polling instances do.

## Install

```bash
dotnet add package Bsft.Messaging.EFCore.Postgres
```

Requires `Bsft.Messaging.EFCore` and `Npgsql.EntityFrameworkCore.PostgreSQL`. The listener needs no
schema of its own: `LISTEN`/`NOTIFY` is a server feature, so there is no migration to add.

## Register

```csharp
services.AddBsftMessaging(configuration, options => options.AddEvent<OrderPlaced>("orders.placed.v1"));
services.AddBsftMessagingEFCore<AppDbContext>();
services.AddBsftMessagingEFCorePostgres<AppDbContext>(configuration);

services.AddDbContext<AppDbContext>((sp, options) => options
    .UseNpgsql(connectionString)
    .AddInterceptors(
        sp.GetRequiredService<OutboxInterceptor>(),
        sp.GetRequiredService<OutboxNotifyInterceptor>()));
```

Call it **alongside** `AddBsftMessagingEFCore`, never instead of it: that call registers the
dispatcher that delivers. The order the two interceptors are added in does not matter — the notify
interceptor reads the change tracker after the save has completed, not the buffer the outbox
interceptor drains.

Once it is wired, a longer `Bsft:Messaging:Outbox:PollInterval` costs nothing in the common case: the
poll becomes the safety net rather than the delivery path. Switching polling off is not an option the
framework offers, and deliberately so.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| `BsftMessagingPostgresOptions.NotificationChannel` | `bsft_outbox` | The channel the commit is announced on. Every writer and every listener of one database has to name the same channel. Lower-case letters, digits and underscores, starting with a letter, at most 63 characters — it is quoted into a `LISTEN` statement, which takes an identifier and not a parameter. |
| `BsftMessagingPostgresOptions.ConnectionString` | `null` | The connection the listener opens. Unset means the `DbContext`'s own. A listening connection is held for the lifetime of the host and idle almost all of it, so a deployment that sizes its pool for request traffic may prefer to give the listener a string of its own. |
| `BsftMessagingPostgresOptions.ReconnectDelay` | `00:00:05` | How long the listener waits before reconnecting after its session ended. Has to be positive: reconnecting without a pause turns a server that is restarting into a connection storm. |

Bound from `Bsft:Messaging:Postgres` when the `IConfiguration` overload is used, and validated on
start.

## Extend

| Seam | Default | Replace it when |
| --- | --- | --- |
| `IOutboxNotifier` | `PostgresOutboxNotifier` — `SELECT pg_notify(channel, '')` through the context that wrote the rows | The announcement has to travel some other way, or has to carry something. Implementations are best-effort by contract: the rows are committed by the time this runs, so a failed announcement is reported and swallowed rather than thrown at the caller's save. |

The listening session behind the worker is internal. It is one Npgsql connection that subscribes,
waits and calls back; everything with a decision in it — when to reconnect, what to drain, what to
log — is in the worker, which is what the tests exercise.

## How it fits together

A notification is sent from the context that wrote the rows, which puts it on the same connection and
therefore inside the same transaction whenever the caller opened one. PostgreSQL delivers a
notification raised inside a transaction when that transaction commits and discards it when it rolls
back, so a listener is never woken for rows that do not exist. Outside a transaction the rows are
already written by the time the announcement goes out. Either way the wake-up cannot run ahead of
what it is about.

The payload is empty on purpose: a notification says only that the outbox has something in it, and
the lease decides what. Carrying row identities would put a second, weaker source of truth beside the
lease.

See [docs/messaging.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/messaging.md)
for the outbox itself, and ADR-0005 for why the drain kernel is still the integration-event one.
