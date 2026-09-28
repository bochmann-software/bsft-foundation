# Bsft.Scheduling

The core of the `Bsft.Scheduling` family: recurring (cron or interval) and delayed background jobs on a
leased dispatcher, so that one occurrence runs once across a whole deployment rather than once per
instance.

## Install

```bash
dotnet add package Bsft.Scheduling
```

Requires `Bsft.Instrumentation` (pulled transitively) for the vendor-neutral instrumentation
source, and no further Bsft packages. It has no `.Abstractions` sibling — the contracts live in the
`Abstractions/` folder of this package. Runs are kept in memory unless `Bsft.Scheduling.EFCore` is
added, which means a delayed job scheduled for tomorrow does not survive a restart.

## Register

```csharp
services.AddBsftScheduling(options =>
{
    options.PollInterval = TimeSpan.FromSeconds(2);
    options.LeaseDuration = TimeSpan.FromMinutes(10);
});

// or bound from configuration, section "Bsft:Scheduling"
services.AddBsftScheduling(configuration);
```

Every registered job also has named `BsftJobOptions` of its own, bound from
`Bsft:Scheduling:Jobs:<job name>`, and the liveness health check is one call:

```csharp
services.AddBsftRecurringJob<CleanupJob>(JobSchedule.Every(TimeSpan.FromMinutes(15)), "cleanup");
services.AddBsftSchedulingHealthCheck();
```

`Enabled` is honoured at runtime, so a host switches a job off from configuration instead of binding
the section a second time to decide whether to register it at all: a disabled job stays registered and
simply gets no occurrences. `AddBsftSchedulingHealthCheck` contributes `bsft-scheduling-jobs`, which
reports `Degraded` for a recurring job whose last successful run is further back than that job's own
cadence allows — the tolerance comes from the job's schedule and its own options, never from a
constant at the call site.

Purging expired rows is not part of this package. `IRetentionPass` and its sweeper moved to
`Bsft.Retention` in 1.3.34, because a purge is not a leased job and a package that only wanted the
loop should not take a dispatcher with it.

The jobs themselves are registered afterwards: `AddBsftJob<TJob>()` for work that only runs when it is
enqueued through `IJobScheduler`, `AddBsftRecurringJob<TJob>()` for a job that carries its own
`JobSchedule`, and `AddBsftRecurringJob<TJob>(schedule)` when the recurrence comes from configuration
instead. Two background services are hosted by the call — the dispatcher and the recurring seeder — so
a worker or a web host needs nothing else. Persistence is opt-in through
`AddBsftSchedulingEFCore<TContext>()`, correlation through `AddBsftSchedulingCorrelation()`.

`AddBsftScheduling` also registers `IStoredScheduleService`: a persisted, per-tenant catalog of
schedules, each tied to an already-registered job key, editable at runtime without a redeploy.

```csharp
var schedules = provider.GetRequiredService<IStoredScheduleService>();
var created = await schedules.CreateAsync("tenant-42", "cleanup", JobSchedule.Cron("0 3 * * *"), payload: null);
await schedules.TrySetEnabledAsync("tenant-42", created.Id, enabled: false);
```

A due schedule is turned into an ordinary one-shot run by the `StoredScheduleSeeder` hosted service, so
the existing dispatcher, executor and outcome policy need no awareness of it. See
[Persisted, per-tenant schedules](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/scheduling.md#persisted-per-tenant-schedules).

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| `BsftSchedulingOptions.PollInterval` | `00:00:05` | How long the dispatcher waits after a pass that claimed nothing. A pass that claimed something polls again at once. |
| `BsftSchedulingOptions.BatchSize` | `50` | The most claimable runs read per pass. |
| `BsftSchedulingOptions.RecurringSeedInterval` | `00:05:00` | How often the recurring registrations are re-seeded, which is what repairs a schedule whose chain broke between settling a run and writing its successor. |
| `BsftSchedulingOptions.LeaseDuration` | `00:05:00` | How long a claim holds a run before another instance may take it over. It has to comfortably exceed the longest expected execution: a job still running when its lease lapses is executed a second time in parallel. |
| `BsftSchedulingOptions.MaxAttempts` | `3` | How many times a run is attempted before it is marked failed. |
| `BsftSchedulingOptions.RetryDelay` | `00:00:30` | How long a failed run waits before the next attempt. |
| `BsftSchedulingOptions.CatchUpPolicy` | `RunAll` | Whether work that fell behind is executed however late, or discarded past `MaxCatchUpAge`. |
| `BsftSchedulingOptions.MaxCatchUpAge` | `1.00:00:00` | How far overdue a run may be before `SkipBeyondMaxAge` discards it. |
| `BsftSchedulingOptions.InstanceId` | machine name plus a per-process suffix | The identifier recorded as the lease owner, so an operator can tell which process is sitting on a stuck run. |
| `BsftSchedulingOptions.StoredScheduleSeedInterval` | `00:00:30` | How often the persisted schedule catalog is polled for due occurrences. |
| `BsftSchedulingOptions.StoredScheduleBatchSize` | `50` | The most due stored schedules read per pass. |

Validation: `ValidateOnStart()` rejects a `BatchSize` or `MaxAttempts` below one, a non-positive
`PollInterval`, `RecurringSeedInterval` or `LeaseDuration`, a negative `RetryDelay` or `MaxCatchUpAge`,
and a blank `InstanceId` — all of them reported in one message rather than one restart at a time. A
zero `RetryDelay` and a zero `MaxCatchUpAge` are legitimate and pass.

## Extend

`IJobStore` is the seam that matters: it persists runs and owns the atomic claim that decides which
instance executes an occurrence. The default is `InMemoryJobStore`, which is registered with `TryAdd`
so a persistence package or a host store of its own wins whichever order the calls are made in.

The rest of the pipeline is replaceable in the same way, each registered with `TryAdd`:
`IJobScheduler` enqueues and cancels, `IJobRegistry` resolves a job name to its registration,
`IJobRunner` takes a claimed run from start to settlement, `IJobExecutor` resolves and invokes the job
inside its own scope and lease boundary (decorate this one to add a resilience pipeline or an audit
hook), `IJobRunOutcomePolicy` decides retry, failure, skip and catch-up, `IRecurringJobPlanner`
creates the next occurrence of a recurring job, and `IJobTelemetry` emits the counters and the
per-run span. `IJobCorrelationAccessor` supplies the correlation id stamped onto an enqueued run; the
default knows only the ambient id of an executing job, and `Bsft.Scheduling.Correlation` replaces it
with one that also reads the current request's id.

`IStoredScheduleStore` is the persistence seam behind `IStoredScheduleService`, in the same shape as
`IJobStore`: it owns the atomic advance that decides which instance turns a due slot into a run. The
default is `InMemoryStoredScheduleStore`, registered with `TryAdd` so `Bsft.Scheduling.EFCore`'s store
wins whichever order the calls are made in.

## Documentation

Topic page: [docs/scheduling.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/scheduling.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
