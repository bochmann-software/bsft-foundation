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
| `RetentionPeriod` | `null` | In age mode, how long a record is kept. Null keeps everything and runs no sweep: deleting data is a decision a host makes explicitly, so "off" needs no second flag. |
| `UseAbsoluteExpiry` | `false` | Opt-in absolute-expiry mode: the cutoff is the current time, read per batch, and `RetentionPeriod` is not used. See below. |
| `BatchSize` | `500` | The most records deleted per batch, and the signal that tells a drained backlog from a full one. |
| `BatchDelay` | `00:00:00` | How long the sweep pauses between batches. Pacing exists for the store: a backlog of millions of rows deleted flat out can starve the workload sharing that database. |
| `SweepInterval` | `01:00:00` | The longest wait between sweeps. In age mode the effective interval is the smaller of this and `RetentionPeriod`, so a record outlives its retention by at most one interval. |
| `MaxStartupStagger` | `00:01:00` | The window the first sweep is spread across. Zero starts every pass at once. |
| `MaxBatchesPerSweep` | `100` | How many batches one sweep may delete before it yields to the loop. Hitting it is not an error: the sweep reports that work remains and the next one continues. |

The options are **named** after the pass type's full name, so a host running several passes configures
each independently, and each is validated by `ValidateOnStart()` with the pass named in every failure.
`BsftRetentionOptions.NameFor<TPass>()` is the same name a configuration section uses.

### Absolute-expiry mode

By default a pass is handed `olderThan = now - RetentionPeriod` (age mode): the cutoff is read once
per sweep, and a record at or after it is left alone. When the records carry their own expiry
instant, set `UseAbsoluteExpiry = true` instead:

```csharp
services.AddBsftRetention<ExpiredGrantPass>(options => options.UseAbsoluteExpiry = true);
```

- The cutoff handed to `PurgeBatchAsync` is the current time of the `TimeProvider`, read again for
  **every batch** (age mode: once per sweep). A pass that re-reads it per batch therefore sees a
  record that expires mid-drain purged by the same sweep.
- The rule is `ExpiresAt <= olderThan`: a record expiring exactly now goes, one a tick later stays.
  Age mode is strictly older.
- `RetentionPeriod` is not used and not validated, so it may be unset (the pass is swept every
  `SweepInterval` anyway) or zero. In age mode a non-positive period is still rejected.
- The interval is `SweepInterval` alone, so a record can wait up to `SweepInterval` between its expiry
  and its purge.
- The flag is per pass, because the options are named per pass: a host that needs both modes registers
  separate pass types. It is invalid for a disk watermark pass, whose `RetentionPeriod` is a minimum
  age; setting it there fails the host at start.
- It is a flag rather than a second pass contract because the batch signature is identical; existing
  `IRetentionPass` implementations and the disk-watermark policy are unaffected.

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

## Start and stop

The sweeper takes part in the host's lifecycle (`IHostedLifecycleService`) rather than starting from
`ExecuteAsync` alone. Its first sweep waits for `StartedAsync`, which the host calls once the
`StartingAsync` and `StartAsync` of **every** hosted service have completed, so a hosted service that
creates or migrates the store a pass deletes from has finished before the first batch, whatever the
order of registration and whatever `MaxStartupStagger` says.

- The stagger is measured from that point and keeps its meaning: zero runs the first sweep right after
  start-up completed, never before.
- A host that never completes its start-up never sweeps. A sweeper started on its own, with
  `StartAsync` but no host around it, does not sweep either.
- A host that stops before it has started ends the sweeper without a sweep and without an error.

Before this, the first sweep began in `ExecuteAsync` after the stagger, and with a stagger of zero it
could run while a later hosted service was still initialising the database.

A stop **drains** rather than aborts, by default and without an option:

- From `StoppingAsync` (or `StopAsync`, for a host that does not call `StoppingAsync`) the batch in
  flight runs to completion, and no further batch or sweep starts. A pause between two batches
  (`BatchDelay`) and the wait between sweeps end at once.
- The cancellation token a batch observes is the sweeper's own, not the `stoppingToken` of
  `BackgroundService`, which the base class cancels at the very start of `StopAsync`. It is cancelled
  only when the token the host hands to `StopAsync` fires, which is `HostOptions.ShutdownTimeout`
  elapsing. The sweeper signals that cancellation without waiting for the token's callbacks, which are a
  store's command cancellation and can block (Npgsql opens a connection for the cancel request), so its
  `StopAsync` returns at the host's shutdown timeout even with a pass that ignores its token or a store
  that is slow to cancel. The batch may still be unwinding in the background then.
- Whatever a batch ends in once its token was cancelled that way is the shutdown cancellation, decided
  by the sweeper's state rather than by the exception type: an `OperationCanceledException` and a
  store's own exception alike (SqlClient reports a cancelled command as `SqlException`, and EF Core
  rethrows it) are logged by the sweeper at debug, not as an error. A failure before the timeout,
  during the drain included, is still logged as an error. The store or EF Core may log the cancelled
  command on their own; that is outside the sweeper.
- Disposing a sweeper that was not stopped cancels the batch in flight, as before.

Before this, a stop cancelled the batch in flight immediately, and EF Core logged the cancelled delete
as an error. A host that wants batches cut short sooner lowers `HostOptions.ShutdownTimeout`.

## Disk watermark

Some records are not kept for a time but for a space: an archive of generated files may grow to a
limit, and the volume it lives on has to keep a floor of free space, whatever the age of what is in it.
`AddBsftRetentionDiskWatermark<TSource>` keeps such an archive in bounds by deleting its oldest items,
and it is an ordinary retention pass on the model above rather than a second mechanism: the loop, the
batching, the pacing, the bound per sweep, the stagger and the failure handling are the sweeper's.

The application supplies the archive, as an `IDiskWatermarkSource`: every item oldest first with its
size, and the delete of one item.

```csharp
internal sealed class ArchiveFileSource(IArchiveDbContext db, IArchiveFiles files) : IDiskWatermarkSource
{
    public async IAsyncEnumerable<DiskWatermarkItemInfo> ListOldestFirstAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var archived = db.ArchivedFiles.AsNoTracking()
            .Where(file => file.State == ArchiveState.Archived)
            .OrderBy(file => file.CreatedAt).ThenBy(file => file.Id)
            .Select(file => new DiskWatermarkItemInfo(file.Id.ToString(), file.FileBytes, file.CreatedAt))
            .AsAsyncEnumerable();

        await foreach (var item in archived.WithCancellation(cancellationToken))
            yield return item;
    }

    public async Task DeleteAsync(DiskWatermarkItemInfo item, CancellationToken cancellationToken = default)
    {
        await db.ArchivedFiles.Where(file => file.Id == Guid.Parse(item.Key))
            .ExecuteUpdateAsync(set => set.SetProperty(file => file.State, ArchiveState.Deleted), cancellationToken);
        files.Delete(item.Key);
    }
}
```

```csharp
builder.Services.AddBsftRetentionDiskWatermark<ArchiveFileSource>(
    watermark =>
    {
        watermark.MaxArchiveBytes = 20L * 1024 * 1024 * 1024;   // the archive holds at most 20 GiB
        watermark.MinAvailableBytes = 5L * 1024 * 1024 * 1024;  // and the volume keeps 5 GiB free
        watermark.VolumePath = archiveDirectory;
        watermark.TargetFillRatio = 0.9;
    },
    retention =>
    {
        retention.RetentionPeriod = TimeSpan.FromMinutes(15);   // never evict anything younger
        retention.SweepInterval = TimeSpan.FromMinutes(15);
    });

// from Bsft.Retention.HealthChecks
builder.Services.AddBsftRetentionHealthCheck<ArchiveFileSource>();

// or both options from one section, "Bsft:Retention:<DiskWatermarkOptions.NameFor<TSource>()>"
builder.Services.AddBsftRetentionDiskWatermark<ArchiveFileSource>(builder.Configuration);
```

| Option | Default | Effect |
| --- | --- | --- |
| `MaxArchiveBytes` | `null` | The most the archive may hold, the sum of the sizes the source lists. Null sets no limit. |
| `MinAvailableBytes` | `null` | The space the volume under `VolumePath` has to keep available to this process. Null sets no floor and measures no volume. |
| `VolumePath` | `null` | A path on the measured volume, typically the archive's directory. Required with `MinAvailableBytes`. |
| `TargetFillRatio` | `0.9` | How far below the limits a crossing drains, in `(0, 1]`. One means no hysteresis. |

**The sweep's cutoff keeps its meaning.** An item created at or after it is counted towards the archive
but never deleted, so `RetentionPeriod` is the minimum age before an item may go for space, and leaving
it unset runs no sweep, as for every other pass. The effective interval is still the smaller of it and
`SweepInterval`.

**Hysteresis.** Once the archive holds more than `MaxArchiveBytes` or the volume less than
`MinAvailableBytes`, the pass deletes until the archive holds at most `MaxArchiveBytes × TargetFillRatio`
and the volume keeps at least `MinAvailableBytes ÷ TargetFillRatio`. Drained to exactly the limit, the
next write would cross it again and every sweep would trim one item. A drain the sweep bound cuts short
carries over: until the target holds, the next sweep drains towards the target rather than waiting for
the limit, and between target and limit nothing is deleted while no drain is under way. The state is
kept per instance in memory; a restart forgets at most one unfinished drain.

**One measurement per sweep.** The first batch of a sweep measures the volume, reads the whole
enumeration once, sums the archive and chooses the oldest items that free enough; the following batches
delete that plan, at most `BatchSize` at a time. The enumeration is read to its end before the first
delete, so the two never share a `DbContext` at the same time. Deleting an item is assumed to free its
listed size on the volume. A watermark with only a volume floor does not list the archive while the floor
holds and no drain is under way, so its status then carries no archive size.

**Unreachable.** When deleting every item old enough still would not reach the target — the archive is
young, or something other than the archive fills the volume — the pass deletes what it may, logs a
warning, and keeps draining on the following sweeps. `IDiskWatermarkStatusReader` answers where each
watermark stood at its last measurement on this instance. The optional health check reads it; it lives
in the satellite `Bsft.Retention.HealthChecks`, so that the packages taking `Bsft.Retention` only to
purge rows do not carry a health check reference, one of the things this package was extracted from
`Bsft.Scheduling` to avoid (see below). Named `bsft-retention-watermark:<pass name>` and tagged
`bsft.retention`, it reports `Degraded` while the watermark cannot be reached and while the archive or
the volume cannot be measured. It never reports `Unhealthy`: an instance with a full archive still
serves requests, and taking it out of rotation frees nothing. Unlike `Bsft.Retention`, which is a
Foundation package, the satellite belongs to the framework tier and ships on the internal feed only.

**Measuring.** `IVolumeSpaceProvider` is the seam. The registered default reads
`DriveInfo.AvailableFreeSpace` of the volume `VolumePath` lives on: on Windows the drive of the path's
root (a UNC path is refused), on Unix the file system of the path itself. A host registers its own
implementation before the call to measure something `DriveInfo` cannot, and a test registers
`Bsft.Retention.Testing.FakeVolumeSpaceProvider`. `Bsft.Storage.FileSystem` measures nothing of a
volume, only the length of the files it stores, and it is a framework package a Foundation package may
not reference.

The pass is named `Bsft.Retention.Watermarks.DiskWatermarkRetentionPass<Your.Source>`, which
`DiskWatermarkOptions.NameFor<TSource>()` returns; it names both options and the configuration section.
`BsftRetentionOptions.NameFor<TPass>()` writes every constructed generic pass that way, since the
runtime's full name of one carries the versioned assembly name of each type argument.

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
own whose only Bsft references are `Bsft.Core` and `Bsft.Hosting.StartGates`, which is what the conventions page named as the exit
from the exception it recorded in 1.3.32.

## Packages

| Package | Contains |
| --- | --- |
| `Bsft.Retention` | `IRetentionPass`, `RetentionSweeper<TPass>`, `BsftRetentionOptions` and `AddBsftRetention<TPass>`; the disk watermark (`IDiskWatermarkSource`, `DiskWatermarkOptions`, `IVolumeSpaceProvider`, `IDiskWatermarkStatusReader`, `AddBsftRetentionDiskWatermark<TSource>`) and `Testing/FakeVolumeSpaceProvider`. |
| `Bsft.Retention.HealthChecks` | The disk watermark health check and `AddBsftRetentionHealthCheck<TSource>`. |

Related: [scheduling.md](scheduling.md) for leased recurring and delayed jobs, which is a different
question and a different package.
