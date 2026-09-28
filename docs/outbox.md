# Bsft.Outbox

The transactional outbox, as a kernel rather than as part of one feature. A row is written in the same
transaction as the state change that raised it, and something else applies its side effect afterwards:
that is the whole shape, and it is the same whether the side effect is publishing an integration event,
calling a payment provider, or provisioning a tenant.

| Package | Contents |
| --- | --- |
| `Bsft.Outbox.Abstractions` | Contracts only — `IOutboxRow`, `LeasedOutboxRow<TPayload>`, `IOutboxStore<TPayload>`, `IOutboxSideEffect<TPayload>`, `IOutboxRetrySchedule`, `IOutboxDrainer<TPayload>`. |
| `Bsft.Outbox` | The drain pass, the retry schedule and the registration. |
| `Bsft.Outbox.EFCore` | One EF Core implementation of the lease protocol, generic over the row it leases, plus the `OutboxRow` base entity. |

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

The loop around it is not in this package. Run it from a hosted service with
`Bsft.Core.Polling.PollLoop`, which is what `OutboxDispatcher` in `Bsft.Messaging` does, or call the
pass on demand.

Two details worth knowing before you tune the options:

- **The whole batch is leased until one instant** and applied one row at a time. Once that instant has
  passed the pass stops rather than applying under a lease it no longer holds, and the untouched
  remainder is claimed by the next pass. `LeaseDuration` therefore has to cover a batch, not a row.
- **A side effect cancelled by a token of its own** — an HTTP client timeout is the usual one — is a
  failed attempt, not a shutdown. Only the token passed into the pass ends it.

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
the table in [`src/Bsft.Outbox/README.md`](../src/Bsft.Outbox/README.md); the drainer refuses an
unusable bound at construction, naming the option and the value it was given.

## Related

[messaging.md](messaging.md) for the integration-event outbox built on these packages, and
`Bsft.Jobs` for the other thing a table of pending work can be — a job queue is scheduled and
retried, an outbox is caused by a transaction.
