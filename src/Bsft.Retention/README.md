# Bsft.Retention

One hosted sweeper that drives one bounded purge pass, so a package that has to delete its own old
rows writes the delete and nothing else.

## Install

```bash
dotnet add package Bsft.Retention
```

Requires `Bsft.Core` and nothing else from the framework. That is the whole point of the package: a
purging package used to reach for `Bsft.Scheduling` to get the loop, which put a job store, a
dispatcher and a health check in front of every consumer that only wanted to delete rows.

## Register

```csharp
services.AddBsftRetention<AuditLogRetentionPass>(options =>
{
    options.RetentionPeriod = TimeSpan.FromDays(365);
    options.BatchSize = 500;
});

// or bound from configuration, section "Bsft:Retention:<pass type full name>"
services.AddBsftRetention<AuditLogRetentionPass>(configuration);
```

Nothing sweeps until `RetentionPeriod` is set. Leaving it unset is how a pass is off, so registering
one is safe in a package's own `AddBsft<Feature>` and the decision to delete stays the host's.

## Implement

```csharp
internal sealed class AuditLogRetentionPass(IAuditDbContext db) : IRetentionPass
{
    public Task<int> PurgeBatchAsync(int batchSize, DateTimeOffset olderThan, CancellationToken ct = default)
        => db.AuditLogs
            .Where(entry => entry.OccurredAt < olderThan)
            .OrderBy(entry => entry.OccurredAt)
            .Take(batchSize)
            .ExecuteDeleteAsync(ct);
}
```

Delete at most `batchSize` records older than `olderThan`, return how many went. The cap has to be
honoured: it is what keeps a first sweep against a large backlog from becoming one long-locking
transaction, and it is also how the sweeper tells a drained backlog from a full batch. A pass is
resolved from a fresh scope for every sweep, so it may take a `DbContext` through its constructor.

What the package supplies around it is the part every hand-rolled purge loop got slightly differently
wrong: the cutoff computed once per sweep rather than once per batch, the batching, the pacing between
batches, the bound on one sweep, the failure handling, and a deterministic startup offset so a host
running a dozen passes does not open with a dozen simultaneous bulk deletes against one database.

## Configure

`BsftRetentionOptions` is bound per pass as named options, keyed by the pass type's full name, so
several passes coexist with separate windows. Section: `Bsft:Retention:<pass type full name>`.

| Option | Default | Effect |
| --- | --- | --- |
| `RetentionPeriod` | `null` | How long a record is kept. Unset keeps everything and runs no sweep at all. |
| `BatchSize` | `500` | The most records deleted per batch, and the signal the sweep reads: a full batch means more is waiting. |
| `BatchDelay` | `00:00:00` | How long the sweep pauses between two batches, for the sake of the transactional workload sharing the store. |
| `SweepInterval` | `01:00:00` | The longest a sweep waits for the next one. The effective interval is the smaller of it and `RetentionPeriod`, so a record outlives its retention by at most one interval. |
| `MaxStartupStagger` | `00:01:00` | The window the deterministic first-sweep offset spreads across. Zero starts every pass at once. |
| `MaxBatchesPerSweep` | `100` | How many batches one sweep may delete before it yields. Hitting the bound is not an error: the next sweep continues where it left off. |

Validation: `ValidateOnStart()` rejects a non-positive `RetentionPeriod` that is set at all, a
`BatchSize` or `MaxBatchesPerSweep` below one, a negative `BatchDelay` or `MaxStartupStagger` and a
non-positive `SweepInterval` — every failure reported at once with the pass named in each, because
"BatchSize must be at least 1" says nothing to a host running six passes.

## Extend

`IRetentionPass` is the only seam, and replacing a policy means registering a different pass rather
than a different hosted service. `RetentionSweeper<TPass>` is public so a host may drive one sweep
itself in a test, but it is registered as a hosted service and needs no wiring.

## Documentation

Topic page: [docs/retention.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/retention.md).
