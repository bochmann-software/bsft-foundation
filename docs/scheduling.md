# Scheduling

`Bsft.Scheduling` runs recurring and delayed background jobs. A recurring job is not a repeating
timer held in memory: it is **one pending row at a time in a store**, and the following occurrence is
created when the current one settles. That is what makes a schedule survive a restart, and what lets
several application instances share one schedule without any of them running the same occurrence
twice.

## Packages

| Package | Contents |
| --- | --- |
| `Bsft.Scheduling` | Job contracts, the cron/interval schedule, the polling dispatcher, the in-memory store. |
| `Bsft.Scheduling.EFCore` | Durable persistence of job runs, with the lease and the single-occurrence invariant enforced by the database. |
| `Bsft.Scheduling.Correlation` | Bridges `Bsft.Correlation`, so an enqueued job carries the correlation id of the request that scheduled it. |
| `Bsft.Scheduling.Http` | The operator read surface: a paged listing of job runs and a separately permissioned route for one run's payload. |

## Writing a job

```csharp
public sealed class NightlyReportJob : IRecurringJob
{
    private readonly IReportService _reports;

    public NightlyReportJob(IReportService reports) => _reports = reports;

    public JobSchedule Schedule => JobSchedule.Cron("0 3 * * MON-FRI");

    public Task ExecuteAsync(JobExecutionContext context, CancellationToken ct = default) =>
        _reports.GenerateAsync(context.ScheduledFor, ct);
}
```

A job is resolved from a **fresh dependency injection scope for every execution**, so it may take
scoped dependencies such as a `DbContext` through its constructor.

## Run results

A job may report what its run achieved, such as how many items it processed and how many failed, by
recording a result on the context. The result is stored with the run and returned by the run listing
of `Bsft.Scheduling.Http`, so an operator console shows a run history with the job's own figures beside
the status.

```csharp
public sealed class MailDispatchJob : IRecurringJob
{
    public JobSchedule Schedule => JobSchedule.Every(TimeSpan.FromMinutes(10));

    public async Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken = default)
    {
        var (sent, failed) = await DispatchPendingAsync(cancellationToken);

        context.ResultRecorder.Record("mail-dispatch/v1", new { Sent = sent, Failed = failed });
    }
}
```

`Record<TResult>(schema, result)` serializes the value as JSON with the web defaults (camelCase) and
stores it as a `JobRunResult`: the schema name and the JSON. The schema is a name the job owns, at most
200 characters; it is what tells a reader how to read the value, so a change to the value's shape is a
reason to change it (`mail-dispatch/v2`). The rules:

- **Optional.** A job that records nothing settles exactly as before, with no result on the run, and
  its row in the listing carries no `result` property.
- **Last call wins.** A job may record as it goes; the run keeps the latest value, so a job that fails
  after recording still settles with what it had counted.
- **The settling attempt's result is stored.** The result is written when the run settles, completed or
  failed for good. An attempt that fails and is retried leaves nothing behind.
- **Bounded.** `BsftSchedulingOptions.MaxResultBytes` (default 4096 UTF-8 bytes of JSON, at most 64 KiB)
  bounds the value. A larger one is refused in the job's own call with `Bsft.Core.PayloadTooLargeException`
  carrying `SchedulingErrorCodes.ResultTooLarge` (`SCHEDULING_RESULT_TOO_LARGE`); nothing is recorded, an
  earlier result is kept, and the run is affected only if the job lets the exception escape, in which
  case the attempt fails like any other.
- **Metadata, not content.** The listing returns results under the metadata permission
  (`scheduling.read`), so a result carries counts, sizes and identifiers of the job's own making, never
  the data it worked on (package-conventions section 5a). The value is not inspected and the listing
  is not audited; a host sets `BsftSchedulingHttpOptions.IncludeResultsInListing` to `false` to keep
  results to the audited single-run route.

A context built by hand, as in a unit test of a job, carries a recorder that throws
`InvalidOperationException` rather than silently discarding: set `ResultRecorder` with `with { ... }`
or in the initializer to a recorder of the test's own. A store of the host's own keeps the result by
persisting `JobRun.Result` in `SettleAsync`; one that ignores it settles runs as before, without results.

## Registration

```csharp
builder.Services.AddBsftScheduling(options =>
{
    options.PollInterval = TimeSpan.FromSeconds(5);
    options.LeaseDuration = TimeSpan.FromMinutes(5);
    options.MaxAttempts = 3;
    options.RecurringSeedInterval = TimeSpan.FromMinutes(5);
});

// or bound from configuration, section "Bsft:Scheduling"
builder.Services.AddBsftScheduling(builder.Configuration);

builder.Services.AddBsftSchedulingEFCore<AppDbContext>();                       // from .EfCore
builder.Services.AddBsftRecurringJob<NightlyReportJob>();                       // schedule from the job
builder.Services.AddBsftRecurringJob<CleanupJob>(JobSchedule.Every(TimeSpan.FromMinutes(15)));
builder.Services.AddBsftJob<SendInvoiceJob>("send-invoice");                    // enqueued explicitly
builder.Services.AddBsftSchedulingCorrelation();                                // from .Correlation
```

### Producer-only hosts

In a split deployment an API enqueues and a worker executes, both against the same run table. Register
the API with `AddBsftSchedulingProducer()` instead of `AddBsftScheduling()`:

```csharp
// API host: enqueues and schedules, never executes
builder.Services.AddBsftSchedulingProducer(builder.Configuration);
builder.Services.AddBsftSchedulingEFCore<AppDbContext>();
builder.Services.AddBsftStoredSchedulesEFCore<AppDbContext>();
builder.Services.AddBsftJob<SendInvoiceJob>("send-invoice");   // so the name resolves; never run here

// Worker host: claims and executes
builder.Services.AddBsftScheduling(builder.Configuration);
builder.Services.AddBsftSchedulingEFCore<AppDbContext>();
builder.Services.AddBsftStoredSchedulesEFCore<AppDbContext>();
builder.Services.AddBsftJob<SendInvoiceJob>("send-invoice");
builder.Services.AddBsftSchedulingHealthCheck();               // worker only
```

**Both hosts must share both stores.** `AddBsftSchedulingEFCore` shares the run table;
`AddBsftStoredSchedulesEFCore` shares the stored-schedule catalog. A producer left on the in-memory
catalog keeps the schedules created through `IStoredScheduleService` in its own process, so the
worker never sees them, and nothing reports it (the same holds for the in-memory run store). The
framework does not warn at start-up: the EF registrations replace the defaults after the fact and a
single-process demo legitimately runs on the in-memory ones.

`AddBsftSchedulingHealthCheck` belongs on the worker. Its heartbeats fill only where runs execute, so
on a producer it would report every recurring job as late.

The producer registers `IJobScheduler`, the store, the registry and the supporting services, and adds
no hosted service: no dispatcher, no recurring or stored-schedule seeder. It therefore never claims a
run. This matters because a host that claims a run whose job it has not registered settles the run as
`Failed` (`JobRunOutcomePolicy.RecordUnregisteredAsync`), which would break the worker's runs. It takes
the same options overloads as `AddBsftScheduling` (a delegate, or the `Bsft:Scheduling` section and a
delegate), validated the same way. Register the jobs the host enqueues with `AddBsftJob` or
`AddBsftRecurringJob`, because the scheduler checks a name against the registry; the producer never
executes them. Recurring jobs are seeded by the worker, so the producer's registration of one is only
what lets it enqueue an extra occurrence.

This mirrors `AddBsftJobs` and `AddBsftJobsWorker` in `Bsft.Jobs`, with one difference: `AddBsftScheduling`
keeps meaning the full host (it also dispatches), so the producer is the added entry point rather than
the base one. A separate entry point was chosen over an option on `AddBsftScheduling` because the role
is visible at the call site, there is no boolean to forget, and the existing call is untouched.

The configuration overload binds `Bsft:Scheduling:Jobs:<job name>` exactly as the full one does.

Registering both calls in one container is allowed in either order and is idempotent: the full
registration wins, and the registered services and hosted services equal those of `AddBsftScheduling`
alone. The options delegates of both calls accumulate and run in call order, so the later call wins for
a value both set.

Without `AddBsftSchedulingEFCore` the scheduler keeps its in-memory store. That store loses everything
when the process ends, so a job delayed until tomorrow never fires after a restart — it is for tests
and single-process demos only.

`BsftSchedulingOptions` is validated by `ValidateOnStart()`, so a bad value fails the host at startup
with every complaint listed at once rather than throwing out of `AddBsftScheduling` one value at a
time. The options are read as `IOptions<BsftSchedulingOptions>`; the raw instance is no longer
registered in the container.

The job contracts — `IJob`, `IRecurringJob`, `IJobScheduler`, `IJobStore`, `JobExecutionContext`,
`JobSchedule`, `JobRun`, `JobRunStatus`, `JobOverlapPolicy`, `JobCatchUpPolicy` — live in
`Bsft.Scheduling.Abstractions` since 1.3.5, so a file that declares a job needs that `using` as well
as `Bsft.Scheduling`. `IJobTelemetry` moved the other way, from `Bsft.Scheduling.Diagnostics` into
`Bsft.Scheduling`, because `Diagnostics/` is a structural folder that adds no namespace segment.

### Bsft.Scheduling.EFCore

Persistence for job runs, so a delayed job survives a restart and several instances coordinate their
leases through the database. `AddBsftSchedulingEFCore<TContext>()` maps `IJobDbContext` onto the
host's own `TContext` as scoped, registers the internal job-run mapper, and replaces the core's
in-memory `IJobStore` with the EF-backed one. Either registration order works — the core registers its
default with `TryAdd` and this call removes whatever is registered before adding its own — and the
`DbContext` itself stays the host's, registered with `AddDbContext<TContext>`.

The package declares no options type and no configuration section: everything an operator decides is
in `BsftSchedulingOptions`, and everything the schema decides is a constructor argument of
`JobRunEntityConfiguration` — `DefaultPendingFilter`, a provider-specific predicate, or `null` to drop
the unique pending-occurrence index and with it the single-occurrence guarantee.

`JobRunEntity` and `JobRunEntityConfiguration` are public so a host can name them in a migration;
`EFCoreJobStore` and the mapper are internal, because a host that wants different persistence writes
its own `IJobStore` against the core package rather than subclassing this one.

Nothing deletes a settled run unless a host says so twice. A run that completes, fails or is skipped is
updated in place, so a scheduler with a few jobs on a ten second cadence grows `JobRuns` without bound, on
the table the dispatcher polls. `AddBsftJobRunEFCoreRetention()` registers `JobRunRetentionPass` and the
framework's retention sweeper over it, and a window on `BsftSchedulingOptions.JobRunRetention` is what sets
it going: completed, failed and skipped runs that settled longer ago than that are deleted, oldest first,
in batches the sweeper bounds and paces. Both are needed because a settled run is the record an operator
reads to find out what ran and why it failed, and deleting it is not reversible. A pending or running run
is never touched, however old. `BsftSchedulingOptions.JobRunRetentionKeepLatest` (default `0`) spares the
latest N settled runs of each job whatever their age, so a job that rarely runs still shows its last
outcomes; it also keeps the job visible to anything that reads its last settled run. Both options are
validated at start (the window positive where set, the count not negative). The pass holds only the delete;
the batch size, the pacing and the startup offset are `BsftRetentionOptions` under the pass's name, the same
as every other retention in the framework. The pass ships no index, because
the package ships no migration: with `JobRunRetentionKeepLatest` at `0` the sweep is paced and rare, while an
index on the settle instant would be written on every settle. With keep-latest on, the pass reads the jobs
that have expired runs and then one boundary per job (that job's Nth newest settled run), which is a
descending read of the job's settled runs; a host with millions of settled rows adds an index on
`(JobName, SettledAt, Id)` in its own migration to turn that read into a seek.

### Bsft.Scheduling.Http

The operator read surface over the run table. `AddBsftSchedulingHttp()` registers the options and the
audited payload read; `MapBsftScheduling()` maps `GET {prefix}/runs` and, once `ReadContentPolicy` is
set, `GET {prefix}/runs/{id}`. What answers the listing is `IJobRunReader`, which
`AddBsftSchedulingEFCore` registers, so an existing EF-backed host gains the surface by naming the
policies.

```csharp
services.AddBsftSchedulingHttp(options =>
{
    options.ReadPolicy = "scheduling.read";
    options.ReadContentPolicy = "scheduling.read-content";
});

app.MapBsftScheduling();
```

The two permissions are the read grading of
[package-conventions](package-conventions.md) section 5a: `scheduling.read` covers the metadata a
console shows, `scheduling.read-content` covers the payload the job was handed, and answering the
second records a `scheduling.job-run.read-content` audit activity. Mapping the payload route without an
`IAuditRecorder` registered throws at startup rather than disclosing the payload untracked, and a miss
records nothing so nobody fills the audit log by walking identifiers.

It is a separate package because `Bsft.Scheduling` is what a worker running jobs references, and a
worker has no business carrying the ASP.NET Core stack. The in-memory store ships no reader on purpose:
a question about what failed last night is a question about runs that outlive the process.

Each listed run carries the result its job recorded, if any (see [Run results](#run-results)), as
`result: { "schema": ..., "value": ... }` with the value as JSON rather than as an escaped string.

What the surface cannot answer follows from the table rather than from the routes. There is no tenant
column, so the database is the scope; there is no start instant, so runs are ordered by when they were
due, when they settled or when their lease expires, never by when execution began; and there is no
stored failure text, so `status=Failed` names the runs and the correlation id on each row leads to the
logs and traces that say why.

### Bsft.Scheduling.Correlation

The adapter between the correlation and scheduling families. `AddBsftSchedulingCorrelation()`
registers a single scoped service as the `IJobCorrelationAccessor` the scheduler stamps onto every
enqueued run — so a job enqueued while handling a request carries that request's correlation id into
an execution that may happen hours later. It declares no options type and no configuration section.

Registration order does not matter: `AddBsftScheduling` registers its ambient-only default with
`TryAdd` and this one uses a plain `Add`, so the bridge wins either way. Without the package the
scheduler behaves exactly as before and runs simply carry no correlation id — the core deliberately
does not reference `Bsft.Correlation`, which would drag the ASP.NET Core request pipeline into a
background component. The ambient id of an executing job wins over the scope's own id, which is what
keeps a chain of jobs on one correlation instead of minting a new id at every hop.

## Switching a job off

Every registered job has named `BsftJobOptions` of its own, keyed by the job name
and bound from `Bsft:Scheduling:Jobs:<job name>`:

```csharp
builder.Services.AddBsftScheduling(builder.Configuration);
builder.Services.AddBsftRecurringJob<CleanupJob>(JobSchedule.Every(TimeSpan.FromMinutes(15)), "cleanup");
```

```json
{
  "Bsft": {
    "Scheduling": {
      "Jobs": {
        "cleanup": { "Enabled": false },
        "nightly-report": { "HeartbeatTolerance": "26:00:00" }
      }
    }
  }
}
```

`Enabled` is read **at runtime**, not at registration. A disabled job stays
registered, resolvable and visible in the registry; it simply gets no new
occurrences, and an occurrence still pending from before it was switched off is
settled as `Skipped` rather than executed. Switching it back on needs no restart —
the recurring seeder restores the chain on its next pass, within
`RecurringSeedInterval`.

That is the point of the flag. The alternative a host writes without it is to bind
the section a second time at startup, purely to decide whether to call
`AddBsftRecurringJob` at all. That makes the switch two sources of truth, only one
of which any validator sees, and it only takes effect on a redeploy. The job is
registered unconditionally instead and the scheduler asks the options.

The delegate overload configures the same options in code, and wins over the
section whichever order the two calls came in:

```csharp
builder.Services.AddBsftRecurringJob<CleanupJob>(
    JobSchedule.Every(TimeSpan.FromMinutes(15)),
    "cleanup",
    configure: options => options.HeartbeatToleranceFactor = 3);
```

## Job liveness

```csharp
builder.Services.AddBsftScheduling(builder.Configuration);
builder.Services.AddBsftRecurringJob<NightlyReportJob>("nightly-report");
builder.Services.AddBsftSchedulingHealthCheck();
```

The check is named `bsft-scheduling-jobs` and carries the single tag
`bsft.scheduling`. It reports every **recurring, enabled, watched** job whose last
successful run is further back than that job's own tolerance allows:

| Option | Default | Means |
| --- | --- | --- |
| `IsHeartbeatMonitored` | `true` | Whether this job is watched at all. |
| `HeartbeatTolerance` | *unset* | An outright tolerance. Where it is set, the two below are not consulted. |
| `HeartbeatToleranceFactor` | `2` | How many of the job's own intervals it may miss — two is one missed occurrence. |
| `HeartbeatGrace` | 1 min | What is added on top, so a job on a very short cadence is not permanently overdue. |

The cadence is derived from the job's own schedule: an interval schedule states it
outright, and a cron expression is measured by the gap between its next two
occurrences. A schedule with no two further occurrences — a cron that has run out
— gives nothing to derive from, so that job is left unwatched until somebody
states a tolerance for it. **Nothing about the tolerance is stated at the call
site**, which is what keeps a host from carrying a table of job names and grace
periods that goes stale the moment a job is renamed or rescheduled.

The result's `Data` carries one `JobHeartbeatSnapshot` per watched job — the job
name, the last completed run, what the silence is measured from, how long it has
been silent, its tolerance and the derived interval — so the endpoint says *which*
job is late and by how much rather than reporting a count.

Two properties of the answer matter when reading it:

- **It is per instance.** A job runs once across a deployment, and
  `IJobHeartbeatStore` records what *this* process has seen since it started. A
  job that has completed nothing yet is measured from the instant the instance
  came up, so "it has never run since we deployed" is a statement with a clock on
  it. A cluster-wide answer is a query against the run store, which is a different
  question and a different cost; the store is registered with `TryAddSingleton`,
  so a host that wants that answer replaces it without touching the check.
- **It reports `Degraded`, never `Unhealthy`**, and it does not carry the
  readiness tag. An instance whose jobs are late still serves requests correctly,
  and failing readiness on it takes a healthy instance out of rotation and hands
  its jobs to the next one — which is how one late job becomes a rolling outage.
  An operator who does want it gating readiness adds the readiness tag to the
  registration deliberately.

Registering the check twice is a no-op rather than a failure: the health check
service refuses to construct when two registrations share a name, which would take
down every host that also calls `AddHealthChecks()`.

## Retention passes

Purging expired rows moved out of this package in 1.3.34. It was never a job — a sweep is not leased,
because a delete does not need to happen exactly once — and a package that only wanted the loop was
taking a job store, a dispatcher and a health check with it. `IRetentionPass`, `RetentionSweeper<T>`
and `AddBsftRetention<TPass>` now live in `Bsft.Retention`, whose only framework references are
`Bsft.Core` and `Bsft.Hosting.StartGates`. See [retention.md](retention.md).

## Delayed and one-shot jobs

`IJobScheduler` is **scoped**, so an enqueue made while handling a request picks up that request's
correlation id and carries it into an execution that may happen hours later.

```csharp
var id = await _scheduler.EnqueueAsync("send-invoice", TimeSpan.FromHours(2), payload: invoiceId);
await _scheduler.CancelAsync(id);   // while it is still pending
```

The payload is an opaque string the scheduler never interprets; a caller that needs structure
serializes and deserializes its own.

## Persisted, per-tenant schedules

`AddBsftRecurringJob` and `IRecurringJob.Schedule` are read once at startup — the schedule is a
constant of the deployment. `IStoredScheduleService` is the runtime-editable counterpart: a catalog of
schedules, each tied to an already-registered job key, a tenant and a payload, that a host's own admin
surface can create, edit, enable, disable and delete without a redeploy.

```csharp
services.AddBsftScheduling();
services.AddBsftJob<SendReportJob>("send-report");

var schedules = scope.ServiceProvider.GetRequiredService<IStoredScheduleService>();

var created = await schedules.CreateAsync(
    tenantId: "tenant-42",
    jobKey: "send-report",
    schedule: JobSchedule.Cron("0 7 * * *"),
    payload: "{\"format\":\"pdf\"}");

await schedules.TrySetEnabledAsync("tenant-42", created.Id, enabled: false);   // pause it
await schedules.TryUpdateAsync(
    "tenant-42", created.Id, JobSchedule.Every(TimeSpan.FromHours(6)), created.Payload);
await schedules.TryDeleteAsync("tenant-42", created.Id);
```

`jobKey` is validated against `IJobRegistry` on both `CreateAsync` and `TryUpdateAsync`: a typo throws
`JobNotRegisteredException` at the call rather than producing a row that silently never runs.

Every member that addresses one schedule names the tenant as well as the identity, and answers as if a
schedule owned by anybody else did not exist. Identities are handed out by the store and are sequential,
so an id alone would otherwise be a guessable handle into every other tenant's catalog. A `null` tenant
addresses the schedules that belong to no tenant, the ones `CreateAsync` writes for a `null` tenant; it
is not a skeleton key. A host administering the catalog across tenants reads the owner from `ListAsync`
and acts under it.

A due schedule becomes an ordinary one-shot `JobRun` — the same shape `IJobScheduler.EnqueueAtAsync`
produces — the instant `StoredScheduleSeeder` (a hosted service registered by `AddBsftScheduling`,
alongside `RecurringJobSeeder`) polls it past its due instant. `JobDispatcher`, `JobExecutor` and
`JobRunOutcomePolicy` need no awareness of where a run came from: they claim, execute, retry and settle
it exactly like any code-registered job's occurrence. Two instances racing the same due schedule produce
exactly one run — `IStoredScheduleStore.AdvanceOrNullAsync` moves a schedule's `NextRunAt` forward
atomically, guarded by an optimistic concurrency token, the same shape `IJobStore.ClaimAsync` uses for a
run's lease.

`StoredScheduleSeedInterval` (default 30 seconds) and `StoredScheduleBatchSize` (default 50) on
`BsftSchedulingOptions` govern the poll. The in-memory default (`InMemoryStoredScheduleStore`) loses the
catalog on restart; call `AddBsftStoredSchedulesEFCore<TContext>()` from `Bsft.Scheduling.EFCore` for a
catalog that survives one — see [Database](#database) below. It needs its own `IStoredScheduleDbContext`
on the host's context, independent of `IJobDbContext`, so a host that only wants delayed and recurring
jobs is unaffected.

## Cron expressions

Five fields — `minute hour day-of-month month day-of-week` — with `*`, values, `a-b` ranges, `,`
lists and `/n` steps. Months accept `JAN`–`DEC` and days `SUN`–`SAT`, case-insensitively; Sunday may
be written as `0` or `7`. Where both day fields are restricted a day matches when *either* does,
which is the standard cron rule.

Deliberately **not** supported, and reported as a parse error rather than silently ignored: `L`, `W`,
`#`, and a seconds field. Wrap-around ranges such as `22-4` are rejected too — write `22-23,0-4`.

All matching happens in UTC, so a schedule does not drift across a daylight saving transition, at the
price of a local-time job moving by an hour twice a year.

## Overlap

`JobOverlapPolicy` decides whether a recurring job may have two executions in flight at once.

- **`Forbid`** (default) — the next occurrence is created only once the current one has settled. A run
  that takes longer than its interval delays the following occurrence instead of stacking a second
  one behind it.
- **`Allow`** — the next occurrence is created before the current run starts, so the job keeps its
  cadence while a slow run is still going. Only for work that is safe to run concurrently with itself.

Either way a job never has more than one *pending* occurrence. Under the EF Core store that is
enforced by a unique filtered index, not merely checked, so two instances seeding at the same moment
cannot both create one.

The chain of occurrences has one gap: under `Forbid` the successor is written after the current run
has settled, and a crash in between leaves the job with nothing pending. The recurring registrations
are therefore re-seeded every `RecurringSeedInterval` (five minutes by default) rather than only at
startup. Seeding is idempotent, so a job that still holds a live occurrence is left alone and only a
broken chain is picked back up.

## Leases, retries and catch-up

The dispatcher polls on `Bsft.Core.Polling.PollLoop` (see
[core.md](core.md#the-poll-loop)): a pass that claimed something polls again at once instead of
sleeping through a backlog, an idle pass waits `PollInterval`, and only the host's stopping token ends
the loop. A pass that failed — or one that a store cancelled by a token of its own, a command timeout
typically — is reported at error level and the next pass tries again.

Each pass claims a due run by taking a **lease** guarded by an optimistic concurrency token: exactly
one instance can win, and the losers simply move on. An instance that dies mid-run lets its lease
lapse, and the next poll re-claims the run — so **execution is at-least-once and a job must be
idempotent**. A job that outlives its lease has its cancellation token signalled, which turns a
silent double execution into a failed attempt the retry logic can see. The signal fires at the
lease's own expiry, not `LeaseDuration` after the job started: the lease begins running when the run
is claimed, and the claim, the successor occurrence and the earlier runs of the same batch all
consume part of it before the work begins. A job therefore has slightly less than `LeaseDuration`
to finish, and never more.

A failed run is retried after `RetryDelay` until `MaxAttempts` is spent, then marked failed. A
recurring job schedules its next occurrence either way: one bad night does not stop the schedule.

After an outage the pending occurrence is simply overdue. `JobCatchUpPolicy.RunAll` (the default)
executes it once — anchoring the following occurrence on the later of its due date and now, so a job
that fell behind returns to its grid instead of replaying every missed tick. `SkipBeyondMaxAge`
discards a run overdue by more than `MaxCatchUpAge` and moves straight to the next occurrence, for
work whose value expires.

## Observability

Traces and metrics are emitted through the vendor-neutral `Bsft.Scheduling` instrumentation source
(see [docs/observability.md](observability.md)): counters `bsft.job.enqueued`, `bsft.job.claimed` and
`bsft.job.skipped`, and a `job.execute` span whose duration is recorded as
`bsft.job.execution.duration` tagged with the outcome. Emission is a no-op until an exporter attaches.

## Database

The EF Core store needs the `JobRuns` table. Apply the configuration in the context and add a
migration:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder) =>
    modelBuilder.ApplyConfiguration(new JobRunEntityConfiguration());
```

Run results live in two nullable columns of `JobRuns`: `ResultSchema` (up to 200 characters) and
`Result` (the JSON text, unbounded in the schema because `MaxResultBytes` bounds it where it is
recorded). A host upgrading from a version without them adds both to its next migration; rows written
before have no result and list as before.

The default index filters use SQL Server / SQLite identifier quoting. Another provider has to pass
its own predicate — PostgreSQL, for instance, needs `"IsRecurring" = true AND "Status" = 0`. Passing
`null` drops the unique index and with it the guarantee that a recurring job has only one pending
occurrence.

The stored-schedule catalog needs its own `StoredSchedules` table, behind a separate context interface
so it stays optional for a host that only wants delayed and recurring jobs:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfiguration(new JobRunEntityConfiguration());
    modelBuilder.ApplyConfiguration(new StoredScheduleEntityConfiguration());
}
```

No filter predicate is needed here: uniqueness within `StoredSchedules` is not part of the invariant —
several schedules may legitimately target the same job key — so the table carries an ordinary index on
`(Enabled, NextRunAt, Id)` for the seeder's poll and one on `TenantId` for the management surface.
