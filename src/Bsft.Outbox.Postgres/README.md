# Bsft.Outbox.Postgres

The PostgreSQL wake-up for any `Bsft.Outbox` outbox. A writer announces new rows on a notification
channel; a listening session hears it and the hosted drain worker of `Bsft.Outbox` drains at once,
instead of waiting out its poll interval.

It is an optimisation and nothing more. The worker's poll interval stays the guarantee: a notification
that is never sent, never received, or lost with the connection it was travelling on costs one poll
interval and no row. The lease, the retry schedule and the dead-letter policy are untouched.

It works for any outbox table because it knows nothing about the table. Whoever writes the rows
announces them, and this package only listens.

## Install

```bash
dotnet add package Bsft.Outbox.Postgres
```

Requires `Bsft.Outbox.Abstractions` (pulled transitively) and `Npgsql`. The listener needs no schema of
its own: `LISTEN`/`NOTIFY` is a server feature. This package only supplies the signal; the worker that
consumes it, drains the outbox and awaits the signal is the hosted drain worker of `Bsft.Outbox`
(`AddBsftOutboxDrainWorker`), which is what you register alongside.

## Register

```csharp
services.AddBsftOutboxDrainWorker<MyPayload>("my-outbox");
services.AddBsftOutboxPostgresWake("my-outbox", options =>
{
    options.NotificationChannel = "my_outbox";
    options.ConnectionString = connectionString;
});
```

The name is the outbox's name: the signal is registered as a keyed `IOutboxWakeSignal` under it, and the
drain worker of the same name picks it up. The signal is also a hosted service, which keeps the
listening session alive and reconnects after the connection dropped.

The writer is yours: a trigger on the outbox table, or `pg_notify` in the transaction that wrote the
row.

```sql
CREATE FUNCTION my_outbox_notify() RETURNS trigger AS $$
BEGIN PERFORM pg_notify('my_outbox', ''); RETURN NULL; END $$ LANGUAGE plpgsql;
CREATE TRIGGER my_outbox_notify AFTER INSERT ON my_outbox
  FOR EACH STATEMENT EXECUTE FUNCTION my_outbox_notify();
```

A notification is delivered at commit, so a drain that runs on it sees the row.

## Configure

Bound from `Bsft:Outbox:Postgres:Instances:<name>` when the registration is handed an `IConfiguration`.

| Option | Default | Effect |
| --- | --- | --- |
| `BsftOutboxPostgresOptions.NotificationChannel` | none, required | The channel the writer announces on. Lower-case letters, digits and underscores, starting with a letter, at most 63 characters: it is quoted into a `LISTEN` statement, which takes an identifier and not a parameter. |
| `BsftOutboxPostgresOptions.ConnectionString` | none, required | The connection the listener opens for itself, taken from the pool of the connection string it is given and held for the host's lifetime (one pool slot: use a different connection string or `Pooling=false` to keep it out of the request pool). It has to reach the primary. |
| `BsftOutboxPostgresOptions.ReconnectDelay` | `00:00:05` | How long the listener waits before it reconnects. Must be positive. |

Every new session raises the signal once as soon as its subscription is in place, so rows written while
nothing was listened (start-up, a reconnect) are drained without waiting for the next write.

## Shared with Bsft.Messaging.EFCore.Postgres

`IPostgresListenSession` is the one listening session: it opens the connection, subscribes, calls back
once after the subscription and again for every notification. `PostgresListenLoop` is the reconnecting
loop around it, and `PostgresNotificationChannel` the validated channel name both take. The wake-up of
`Bsft.Messaging.EFCore.Postgres` listens through the same three.

The channel is handed to the server as a value and quoted there (`format('%I')`), so the `LISTEN`
statement is never built from the name. The grammar stays as a start-up check: lower-case letters,
digits and underscores, starting with a letter, at most 63 characters, because that is what makes the
listening and the `pg_notify` side name the same channel.

## Documentation

Topic page: [docs/outbox.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/outbox.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
