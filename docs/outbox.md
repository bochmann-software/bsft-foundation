# Bsft.Outbox

The transactional outbox, as a kernel rather than as part of one feature. A row is written in the same
transaction as the state change that raised it, and something else applies its side effect afterwards:
that is the whole shape, and it is the same whether the side effect is publishing an integration event,
calling a payment provider, or provisioning a tenant.

| Package | Contents |
| --- | --- |
| `Bsft.Outbox.Abstractions` | Contracts only — `IOutboxRow`, `LeasedOutboxRow<TPayload>`, `IOutboxStore<TPayload>`, `IOutboxSideEffect<TPayload>`, `IOutboxRetrySchedule`, `IOutboxDrainer<TPayload>`, `IOutboxWakeSignal`. |
| `Bsft.Outbox` | The drain pass, the retry schedule, the registration and the optional hosted drain worker. |
| `Bsft.Outbox.EFCore` | One EF Core implementation of the lease protocol, generic over the row it leases, plus the `OutboxRow` base entity. |
| `Bsft.Outbox.Postgres` | A PostgreSQL `LISTEN` implementation of the wake signal, for any outbox table. |

`Bsft.Messaging` runs its integration-event outbox on exactly these packages, so the arrangement is
proven by the one outbox the framework already shipped rather than only by the sample.

## What an outbox is for

A state change and the thing it causes elsewhere cannot be made atomic across two systems. Writing the
intent into the same database transaction as the change, and carrying it out afterwards, moves the
problem to one nobody can lose: the row exists exactly when the change exists.

The price is duplicates. The side effect is applied before the row is marked, so a crash in between
means it is applied again. **Every `IOutboxSideEffect<TPayload>` has to be idempotent.** What the
arrangement rules out is a lost effect, never a repeated one.

## The three seams

```csharp
public interface IOutboxStore<TPayload>
{
    Task<IReadOnlyList<LeasedOutboxRow<TPayload>>> LeaseAsync(
        DateTimeOffset now, TimeSpan leaseDuration, int batchSize, CancellationToken ct = default);

    Task<bool> TryMarkProcessedAsync(long id, Guid leaseToken, DateTimeOffset processedAt, CancellationToken ct = default);

    Task AbandonAsync(long id, Guid leaseToken, DateTimeOffset nextAttemptAt, CancellationToken ct = default);

    Task DeadLetterAsync(long id, Guid leaseToken, DateTimeOffset deadLetteredAt, string reason, CancellationToken ct = default);
}

public interface IOutboxSideEffect<TPayload>
{
    Task ApplyAsync(TPayload payload, CancellationToken ct = default);

    string Describe(TPayload payload);
}
```

The store is where the rows live, the side effect is what a row means, and `IOutboxRetrySchedule`
decides how long a failed row waits. Only the first two are yours: the schedule ships, shared by every
outbox in the host, and a host that replaces it replaces it for all of them.

`Describe` is what a log line and a dead-letter reason name the row by. Keep it short and free of
anything you would not want in a log.

## One pass

`IOutboxDrainer<TPayload>.DrainOnceAsync` leases a batch, applies each row's side effect in order,
marks what succeeded, hands back what failed with the instant it may be tried again, and retires what
has used up its attempts. It returns how many rows it applied.

The loop around it is optional. Run it with the hosted drain worker (below), from a hosted service of
your own with `Bsft.Core.Polling.PollLoop`, which is what `OutboxDispatcher` in `Bsft.Messaging` does,
or call the pass on demand.

Two details worth knowing before you tune the options:

- **The whole batch is leased until one instant** and applied one row at a time. Once that instant has
  passed the pass stops rather than applying under a lease it no longer holds, and the untouched
  remainder is claimed by the next pass. `LeaseDuration` therefore has to cover a batch, not a row.
- **A side effect cancelled by a token of its own** — an HTTP client timeout is the usual one — is a
  failed attempt, not a shutdown. Only the token passed into the pass ends it.

## The hosted drain worker

```csharp
services.AddBsftOutboxDrainWorker<string>("provisioning", options => options.BatchSize = 50);
```

A host that drained from its own hosted service passed its stopping token straight into the pass, and a
shutdown then cancelled it in the middle of a row: a row whose side effect had already run (a mail that
was sent) never got its processed mark and was applied again by the next instance. The worker closes
that gap, and nothing changes for a host that does not register it.

**Shutdown grace.** The host's stop decides only whether another row is started: the drainer checks it
before it leases and between rows (`DrainOnceAsync(mayStartRow, cancellationToken)`), so after the stop no
new row begins and the rest of the leased batch stays pending. Those rows, and the whole batch when a stop lands during the lease call, stay invisible to other instances until `BsftOutboxOptions.LeaseDuration` (default one minute) expires, so a host with frequent rolling deploys should set a short `LeaseDuration`. The row in flight runs on a token of its
own, cancelled only `BsftOutboxDrainWorkerOptions.ShutdownGrace` (default five seconds) after the stop,
so it can finish its side effect and its processed mark. A row whose effect outlasts the grace is
cancelled, stays leased, and is claimed again once the lease expires; that is the duplicate the contract
already permits, and the grace is what makes it rare. No new pass starts after the stop, and the grace
has to stay under the host's shutdown timeout, which ends the process regardless: the host refuses to
start when `ShutdownGrace` is not shorter than `HostOptions.ShutdownTimeout` (a failed start rather than a
clamp, because the timeout is shared by every hosted service). A name maps one to one to a payload type:
the drain pass is registered once per payload type, so `AddBsftOutboxDrainWorker` for a second name of the
same payload type throws.

**Start gates.** The worker is registered as a gated hosted service, like the other framework loops, so it
starts only after every registered `IHostedWorkStartGate` has opened: a worker that drains in the second the
API applies its migrations reads tables in their old shape. The worker itself knows nothing about gates. See
[hosting.md](hosting.md#start-gates-for-hosted-loops).

**The loop.** A pass repeats while it applies rows, because one wake can stand behind more rows than a
batch holds. Then the worker waits for the poll interval or a wake signal. A failing pass is logged and
retried after the poll interval; only the stopping token ends the worker. The interval and the batch are
the outbox's own `PollInterval` and `BatchSize`, validated at start with the rest of
`BsftOutboxOptions`, so one outbox has one spelling of each. `ShutdownGrace` is the only option the
worker adds; it must not be negative, and zero cancels with the stop. Bound them from
`Bsft:Outbox:Instances:<name>` and `Bsft:Outbox:DrainWorkers:<name>` with the `IConfiguration` overload.

**Wake signal.** An `IOutboxWakeSignal` ends the idle wait early. Register it as a keyed service under
the outbox's name, or without a key in a host with one outbox. A signal raised while the worker is busy
is kept, and several collapse into one. The signal is a hint: the poll interval stays the guarantee, so a
signal that is lost costs the interval and nothing else.

**A seam around each pass.** Implement `IOutboxDrainPass<TPayload>` and register it with
`AddBsftOutboxDrainPass<TPayload, TPass>()` to run something around every lease-and-apply round: open
a connection before the batch is leased, so that an unreachable server costs no delivery attempt, and
close it in `finally`, or purge after the round. The worker creates a service scope per round, resolves the
pass from it and hands it that scope's `IServiceProvider`, the worker's pass token and a `drainOnce`
delegate. The delegate is the worker's own round with every semantic intact (start gate, lease, attempts,
retry schedule, shutdown grace); it returns the rows applied, and the worker keeps drawing while that is
positive, so return what it returned. A pass that returns zero without calling it leases nothing. An
exception from the pass is handled like one from the drain: logged, retried after the poll interval. The
order of the two registration calls does not matter, and a worker without a pass drains exactly as before
and creates no scope.

```csharp
public sealed class SmtpDrainPass(ISmtpConnection smtp) : IOutboxDrainPass<MailPayload>
{
    public async Task<int> RunAsync(
        IServiceProvider serviceProvider, Func<CancellationToken, Task<int>> drainOnce, CancellationToken cancellationToken)
    {
        await smtp.ConnectAsync(cancellationToken);

        try { return await drainOnce(cancellationToken); }
        finally { await smtp.DisconnectAsync(CancellationToken.None); }
    }
}

services.AddBsftOutboxDrainWorker<MailPayload>("mail");
services.AddBsftOutboxDrainPass<MailPayload, SmtpDrainPass>();
```

### Wake on PostgreSQL

`Bsft.Outbox.Postgres` implements the signal on `LISTEN`:

```csharp
services.AddBsftOutboxDrainWorker<string>("provisioning");
services.AddBsftOutboxPostgresWake("provisioning", options =>
{
    options.NotificationChannel = "provisioning_outbox";
    options.ConnectionString = listenerConnectionString;
});
```

It works for any outbox table because it only listens; the writer announces rows with a trigger on the
table or with `pg_notify` in the writing transaction, on the channel named in the options. The listener
holds a connection of its own, reconnects after `ReconnectDelay`, and raises the signal once for every
session it starts, so rows written while nothing was listening are drained without waiting for the next
write. `Bsft.Messaging.EFCore.Postgres` listens through the same `IPostgresListenSession`.

## Persisting one with EF Core

An outbox is one entity that derives from `OutboxRow`, and one `IOutboxRowSource` saying where those
rows live and how a payload is read off one:

```csharp
public sealed class ProvisioningIntentRow : OutboxRow
{
    public string TenantId { get; set; } = string.Empty;
}

internal sealed class ProvisioningIntentRowSource : IOutboxRowSource<ProvisioningIntentRow, string>
{
    public int FailureReasonMaxLength => 512;

    public DbSet<ProvisioningIntentRow> Rows(IServiceProvider scopedServices)
        => scopedServices.GetRequiredService<ProvisioningDbContext>().Intents;

    public string PayloadOf(ProvisioningIntentRow row) => row.TenantId;
}
```

`OutboxRow` carries the lifecycle columns — `ProcessedAt`, `LeasedUntil`, `LeaseToken`,
`AttemptCount`, `NextAttemptAt`, `DeadLetteredAt`, `FailureReason` — as mapped properties an entity
inherits. It is a base class rather than only the interface because the lease protocol builds its
predicates against these members, and a provider translates a property of the entity where it would
have to resolve an interface member by name. The class itself is not mapped: one entity is one table.

Then register the pass and the two seams:

```csharp
services.AddBsftOutbox<string>("provisioning", options => options.BatchSize = 50);
services.AddSingleton<IOutboxRowSource<ProvisioningIntentRow, string>, ProvisioningIntentRowSource>();
services.AddSingleton<IOutboxStore<string>>(sp => new EFCoreOutboxStore<ProvisioningIntentRow, string>(
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<IOutboxRowSource<ProvisioningIntentRow, string>>()));
services.AddSingleton<IOutboxSideEffect<string>, TenantProvisioning>();
```

A second outbox is a second name and a second entity. It is never a second lease protocol, which is
the point of the arrangement: the race between two application instances is decided in one place.

## How the lease holds

Every transition is a single conditional statement rather than a load-modify-save, so the database
decides the race and an affected count of zero is the answer that this caller lost it. A claim carries
the previous lease token *and* the attempt count into its condition: another instance abandoning the
row raises the count without touching the token, and claiming on a count read before that increment
would buy the row extra attempts past the configured limit.

A retired row is kept. `DeadLetteredAt` and `FailureReason` say when and why, and the row stays
queryable but is never leased again. The recorded reason is cut to the column width without splitting
a surrogate pair, because failing that write would leave the row in the very retry loop it was
supposed to leave.

`FailureReason` is an exception message, and an exception raised while applying a side effect can
quote parts of the payload. Treat the column as at least as sensitive as the payload, and keep
personal data out of the exceptions a side effect throws.

## Options

Named per outbox, so a second outbox is a second name rather than a second configuration shape. See
the table in [`src/Bsft.Outbox/README.md`](../src/Bsft.Outbox/README.md) and for the PostgreSQL wake-up [`src/Bsft.Outbox.Postgres/README.md`](../src/Bsft.Outbox.Postgres/README.md); the drainer refuses an
unusable bound at construction, naming the option and the value it was given.

## Related

[messaging.md](messaging.md) for the integration-event outbox built on these packages, and
`Bsft.Jobs` for the other thing a table of pending work can be — a job queue is scheduled and
retried, an outbox is caused by a transaction.
