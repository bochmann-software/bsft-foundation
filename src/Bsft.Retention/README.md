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
| `RetentionPeriod` | `null` | In age mode, how long a record is kept. Unset keeps everything and runs no sweep at all. |
| `UseAbsoluteExpiry` | `false` | Opt-in absolute-expiry mode: the cutoff is the current time, read per batch, and `RetentionPeriod` is not used. See below. |
| `BatchSize` | `500` | The most records deleted per batch, and the signal the sweep reads: a full batch means more is waiting. |
| `BatchDelay` | `00:00:00` | How long the sweep pauses between two batches, for the sake of the transactional workload sharing the store. |
| `SweepInterval` | `01:00:00` | The longest a sweep waits for the next one. In age mode the effective interval is the smaller of it and `RetentionPeriod`, so a record outlives its retention by at most one interval. |
| `MaxStartupStagger` | `00:01:00` | The window the deterministic first-sweep offset spreads across. Zero starts every pass at once. |
| `MaxBatchesPerSweep` | `100` | How many batches one sweep may delete before it yields. Hitting the bound is not an error: the next sweep continues where it left off. |

Validation: `ValidateOnStart()` rejects, in age mode, a non-positive `RetentionPeriod` that is set at all, a
`BatchSize` or `MaxBatchesPerSweep` below one, a negative `BatchDelay` or `MaxStartupStagger` and a
non-positive `SweepInterval` — every failure reported at once with the pass named in each, because
"BatchSize must be at least 1" says nothing to a host running six passes.

## Absolute-expiry mode

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

## Disk watermark

```csharp
services.AddBsftRetentionDiskWatermark<ArchiveFileSource>(
    watermark =>
    {
        watermark.MaxArchiveBytes = 20L * 1024 * 1024 * 1024;
        watermark.MinAvailableBytes = 5L * 1024 * 1024 * 1024;
        watermark.VolumePath = archiveDirectory;
    },
    retention => retention.RetentionPeriod = TimeSpan.FromMinutes(15));
```

An archive kept by space rather than by age. The source implements `IDiskWatermarkSource`: list every
item oldest first with its size, delete one item. Once the archive exceeds `MaxArchiveBytes` or the
volume under `VolumePath` keeps less than `MinAvailableBytes`, the oldest items are deleted until the
archive holds at most `MaxArchiveBytes × TargetFillRatio` and the volume keeps
`MinAvailableBytes ÷ TargetFillRatio` (default ratio `0.9`), so the archive does not flap at its limit.
It is a retention pass like any other: `RetentionPeriod` is the minimum age before an item may be
evicted, and leaving it unset runs no sweep. `IDiskWatermarkStatusReader` answers where each watermark
stood at its last measurement, and is the seam a host builds its own health check or metric on. Within
the commercial BSFT framework, the framework-tier satellite `Bsft.Retention.HealthChecks` turns it into a
health check that reports `Degraded` when the watermark cannot be reached or measured.

## Lifecycle

**Start.** The sweeper implements `IHostedLifecycleService` and takes its first sweep only after the
host has called `StartedAsync`, that is once the `StartingAsync` and `StartAsync` of every hosted
service have completed. A hosted service that creates or migrates the database a pass deletes from has
therefore finished before the first batch, even with `MaxStartupStagger` at zero. The stagger is
measured from that point, and zero means the first sweep runs right after start-up completed. A host
that never completes its start-up never sweeps, and a host that stops before it has started ends the
sweeper without a sweep and without an error. A sweeper started on its own (only `StartAsync`, no
host) does not sweep either.

**Stop.** A stop drains, and this is the default. From `StoppingAsync` (or `StopAsync`) the batch in
flight runs to completion and no further batch or sweep starts; a pause between batches ends at once.
The token handed to `PurgeBatchAsync` is cancelled only when the host's shutdown token fires, that is
when `HostOptions.ShutdownTimeout` has elapsed, and the sweeper signals that cancellation without
waiting for the token's callbacks (a store's command cancellation), so its `StopAsync` returns at that
timeout. Whatever a batch ends in once its token was cancelled that way, an `OperationCanceledException`
or a store's own exception such as SqlClient's `SqlException`, the sweeper logs at Debug, not as an
error: cancellation on shutdown is not a failure. A failure before the timeout is still logged as an
error, and the store or EF Core may log the cancelled command themselves. A host that wants batches cut short
sooner lowers `ShutdownTimeout`.

## Extend

`IRetentionPass` is the seam for a policy of your own, and replacing a policy means registering a
different pass rather than a different hosted service. The disk watermark measures its volume through
`IVolumeSpaceProvider` (`DriveInfo` by default); register another implementation before
`AddBsftRetentionDiskWatermark` to measure something `DriveInfo` cannot, or
`Bsft.Retention.Testing.FakeVolumeSpaceProvider` in a test. `RetentionSweeper<TPass>` is public so a host may drive one sweep
itself in a test, but it is registered as a hosted service and needs no wiring.

## Documentation

Topic page: [docs/retention.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/retention.md).
