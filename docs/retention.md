# Retention

`Bsft.Retention` is one hosted sweeper and one interface. A package or a host that has to delete its
own expired rows writes the delete; the loop, the cutoff, the batching, the pacing, the bound on a
single sweep and the startup offset come from here.

## The pass

Deleting expired rows on a timer is the same job written over and over: a loop, a cutoff, a bounded
batch, a pause so the delete does not starve the transactional workload, and a first sweep that must
not land at the same instant as every other purge in the deployment. `IRetentionPass` is that job with
everything but the delete taken away.

```csharp
public sealed class AuditLogRetentionPass : IRetentionPass
{
    private readonly IAuditLogDbContext _db;

    public AuditLogRetentionPass(IAuditLogDbContext db) => _db = db;

    public async Task<int> PurgeBatchAsync(int batchSize, DateTimeOffset olderThan, CancellationToken ct = default)
    {
        var ids = await _db.AuditLog
            .Where(e => e.OccurredAt < olderThan)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .Take(batchSize)
            .Select(e => e.Id)
            .ToListAsync(ct);

        return ids.Count == 0
            ? 0
            : await _db.AuditLog.Where(e => ids.Contains(e.Id)).ExecuteDeleteAsync(ct);
    }
}
```

That is the whole contract. The caller supplies the cutoff — computed from the configured window and
a `TimeProvider`, so nothing in a pass reads the clock — and the batch size, and reads the returned
count as the signal for what to do next: a call that returns `batchSize` means more is waiting and the
next batch follows, anything less ends the sweep until the next interval.

```csharp
builder.Services.AddBsftRetention<AuditLogRetentionPass>(options =>
{
    options.RetentionPeriod = TimeSpan.FromDays(365);
    options.BatchSize = 500;
    options.BatchDelay = TimeSpan.FromSeconds(1);
});

// or bound from configuration, section "Bsft:Retention:<pass type full name>"
builder.Services.AddBsftRetention<AuditLogRetentionPass>(builder.Configuration);
```

The call registers the pass as scoped — so it may take a `DbContext` — and one hosted sweeper for it.
It needs nothing else registered, and in particular no scheduler: a sweep is its own loop and uses
neither a job store nor a leased dispatcher. See [why retention is not a job](#why-retention-is-not-a-job).

| Option | Default | Effect |
| --- | --- | --- |
| `RetentionPeriod` | `null` | How long a record is kept. Null keeps everything and runs no sweep: deleting data is a decision a host makes explicitly, so "off" needs no second flag. |
| `BatchSize` | `500` | The most records deleted per batch, and the signal that tells a drained backlog from a full one. |
| `BatchDelay` | `00:00:00` | How long the sweep pauses between batches. Pacing exists for the store: a backlog of millions of rows deleted flat out can starve the workload sharing that database. |
| `SweepInterval` | `01:00:00` | The longest wait between sweeps. The effective interval is the smaller of this and `RetentionPeriod`, so a record outlives its retention by at most one interval. |
| `MaxStartupStagger` | `00:01:00` | The window the first sweep is spread across. Zero starts every pass at once. |
| `MaxBatchesPerSweep` | `100` | How many batches one sweep may delete before it yields to the loop. Hitting it is not an error: the sweep reports that work remains and the next one continues. |

The options are **named** after the pass type's full name, so a host running several passes configures
each independently, and each is validated by `ValidateOnStart()` with the pass named in every failure.
`BsftRetentionOptions.NameFor<TPass>()` is the same name a configuration section uses.

## The startup stagger

Every pass is registered at the same startup and would otherwise take its first sweep at the same
instant, so a host running a dozen of them opens with a dozen simultaneous bulk deletes against one
database. The offset is an FNV-1a hash of the pass name reduced into `MaxStartupStagger`, not a random
draw: the property that matters is that two passes differ, not that either is unpredictable. A hash
also gives the same answer on every instance and across restarts, so the spread stays where an
operator last saw it instead of reshuffling on every deploy.

The cutoff is computed once per sweep rather than once per batch, so a long drain deletes the set it
set out to delete instead of a target creeping forward under it — which is what makes a sweep
terminate on a table receiving writes faster than one batch clears.

## Why retention is not a job

A retention pass is a hosted loop rather than an `IRecurringJob` on `Bsft.Scheduling`'s dispatcher. A
recurring job is leased, so exactly one instance runs an occurrence; that is the right shape for work
that must not happen twice, and the wrong one here. A purge is idempotent by construction — a deleted
row is not deleted again — so the lease buys nothing, while the job model costs the drain: one
occurrence is one execution, and a backlog would then trickle out one occurrence at a time instead of
draining. It would also make every package that purges depend on a registered scheduler, turning an
always-on chore into something a host can silently forget to switch on.

## Why it is a package of its own

The primitive lived in `Bsft.Scheduling` until 1.3.34, and the reason it moved is the closure. A
purging package needs a loop, not a scheduler, and taking `Bsft.Scheduling` for it put a job store, a
leased dispatcher, a stored-schedule catalog and a health check in front of every consumer that only
wanted to delete rows. That was tolerable while two packages used it and untenable at the point the
rest of the tree was meant to adopt it, because the alternative was every purging package in the
framework hanging off the scheduler.

`Bsft.Core` — where `PollLoop` itself lives, and which every package already references — would have
needed no new arrow at all, but it carries **no package references whatsoever**, and that empty
closure is what lets an `.Abstractions` package reference it (see
[package-conventions.md](package-conventions.md)). A hosted service with options, DI and logging would
widen the closure of the entire framework to pay for one loop. So the sweeper sits in a package of its
own whose only Bsft reference is `Bsft.Core`, which is what the conventions page named as the exit
from the exception it recorded in 1.3.32.

## Packages

| Package | Contains |
| --- | --- |
| `Bsft.Retention` | `IRetentionPass`, `RetentionSweeper<TPass>`, `BsftRetentionOptions` and `AddBsftRetention<TPass>`. |

Related: [scheduling.md](scheduling.md) for leased recurring and delayed jobs, which is a different
question and a different package.
