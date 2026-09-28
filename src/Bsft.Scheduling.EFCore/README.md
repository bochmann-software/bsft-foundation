# Bsft.Scheduling.EFCore

The persistence half of the `Bsft.Scheduling` family: job runs in the application's own DbContext, so a
delayed job survives a restart and several instances coordinate their leases through the database.

## Install

```bash
dotnet add package Bsft.Scheduling.EFCore
```

Requires `Bsft.Scheduling` (pulled transitively), `Microsoft.EntityFrameworkCore` and
`Microsoft.EntityFrameworkCore.Relational`. The database provider and the `DbContext` itself belong to
the host; this package only supplies the entity, its configuration and the store.

## Register

```csharp
services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));

services.AddBsftScheduling();
services.AddBsftSchedulingEFCore<AppDbContext>();
```

`AppDbContext` implements `IJobDbContext` and applies `JobRunEntityConfiguration`, through
`ApplyConfigurationsFromAssembly(typeof(JobRunEntityConfiguration).Assembly)` or an explicit
`ApplyConfiguration` call, and the resulting `JobRuns` table needs a migration. Either registration
order works: the core registers its in-memory store with `TryAdd` and this call removes whatever is
registered before adding the EF Core one, so neither can silently win over the other.

The persisted schedule catalog is a separate, independent registration:

```csharp
services.AddBsftStoredSchedulesEFCore<AppDbContext>();
```

`AppDbContext` implements `IStoredScheduleDbContext` and applies `StoredScheduleEntityConfiguration`,
and the resulting `StoredSchedules` table needs a migration too. The interface is deliberately separate
from `IJobDbContext`: a host that already implements it for delayed and recurring jobs is not broken by
a new required member the moment it adopts the catalog, and a host that only wants the job store carries
no table it never queries.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| `JobRunEntityConfiguration.DefaultPendingFilter` | `[IsRecurring] = 1 AND [Status] = 0` | The SQL predicate behind the unique index that allows at most one *pending* occurrence per recurring job. Written for SQL Server and SQLite, whose identifier quoting matches. |
| `new JobRunEntityConfiguration(pendingFilter)` | the default filter | A provider whose quoting differs supplies its own — PostgreSQL needs `"IsRecurring" = true AND "Status" = 0`. Passing `null` leaves a plain index: no filter and no uniqueness, and with the uniqueness goes the guarantee that two instances seeding at the same moment cannot both create an occurrence of the same recurring job. |

This package declares no options type of its own and therefore no configuration section: everything an
operator decides lives in `BsftSchedulingOptions`, and everything the schema decides is a constructor
argument of the entity configuration. Validation: none beyond what the core validates.

## Extend

`IJobDbContext` is the only seam this package declares, and it is the one the host implements: the
`DbSet<JobRunEntity>` the store reads and writes, plus `SaveChangesAsync`. It stays here rather than in
the core package because its signature names EF Core types.

`EFCoreJobStore` itself is internal — it is the `IJobStore` implementation this package registers, and
a host that wants different persistence writes its own `IJobStore` against the core package instead of
subclassing this one. `JobRunEntity` and `JobRunEntityConfiguration` are public so that a host can
name them in a migration, a query of its own or a model customisation.

`IStoredScheduleDbContext` is the matching seam for the stored-schedule catalog: the
`DbSet<StoredScheduleEntity>` the store reads and writes, plus `SaveChangesAsync`. `EFCoreStoredScheduleStore`
is internal for the same reason `EFCoreJobStore` is; `StoredScheduleEntity` and
`StoredScheduleEntityConfiguration` are public for the same reason `JobRunEntity` and its configuration
are.

## Documentation

Topic page: [docs/scheduling.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/scheduling.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
