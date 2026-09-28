# Bsft.EventAggregator.Saga.EFCore

The relational persistence of the `Bsft.EventAggregator.Saga` family: saga state and durable timeouts
in the application's own `DbContext`, with the state advance of a step and the reminders it scheduled
committed in one `SaveChangesAsync`.

## Install

```bash
dotnet add package Bsft.EventAggregator.Saga.EFCore
```

Requires `Bsft.EventAggregator.Saga` (pulled transitively) and `Microsoft.EntityFrameworkCore` plus
`Microsoft.EntityFrameworkCore.Relational`. It brings no provider of its own: the application supplies
the `DbContext` and therefore the provider.

## Register

```csharp
public class AppDbContext : DbContext, ISagaTimeoutDbContext
{
    public DbSet<SagaStateEntity> SagaStates => Set<SagaStateEntity>();
    public DbSet<SagaTimeoutEntity> SagaTimeouts => Set<SagaTimeoutEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ISagaDbContext).Assembly);
}

services.AddBsftSaga();
services.AddBsftSagaEFCoreWithTimeouts<AppDbContext>();

// state only, timers stay in memory
services.AddBsftSagaEFCore<AppDbContext>();
```

Both methods replace registrations `AddBsftSaga` made, so they are called after it. The context must
implement `ISagaDbContext` for `AddBsftSagaEFCore<TContext>` and `ISagaTimeoutDbContext` for
`AddBsftSagaEFCoreWithTimeouts<TContext>`, and its model must carry `SagaStateEntityConfiguration` and
`SagaTimeoutEntityConfiguration` — the indexes they declare are not cosmetic, they are what makes the
pending-name rule and the dispatch claim work. Until 1.3.5 the same wiring was reached through
`UseDbProvider<TContext>()` on the options object; that made the choice of store a property of a
configuration record and is gone.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| — | — | The package has no options type. What it persists and how it commits follows from which of the two registration methods is called. |
| `SagaTimeoutEntityConfiguration(string? pendingFilter)` | `"[DispatchedAt] IS NULL"` | The SQL predicate that selects pending rows for the filtered indexes. The default quoting matches SQL Server and SQLite; PostgreSQL needs `"DispatchedAt" IS NULL`. Passing `null` drops the filter and with it the uniqueness of a pending timeout name. |

Validation: none of its own — the constructor guards reject a null service collection, and a context
that does not implement the required interface is a compile error rather than a startup one.

## Extend

`IEFCoreSagaStore` and `IEFCoreSagaTimeoutStore` name the EF-backed implementations behind
`ISagaStore` and `ISagaTimeoutStore`, so a host can resolve or replace exactly the relational store
without touching the engine's provider-agnostic contracts. `IEFCoreSagaTimeoutStore.IsStagingIntoWindow`
reports whether the next write would join an open persistence window, which is what makes the atomic
path observable from the outside.

`ISagaDbContextAccessor` exposes the context of the open persistence window. `EFCoreSagaPersistenceWindow`
implements it and `ISagaPersistenceWindow` at once, on purpose: whoever opens the window is the only
component that can know which context is current, and splitting the two would force the stores to
depend on a concrete class to reach the setter. Replace the pair to stage saga writes into a unit of
work the application already owns.

## Documentation

Topic page: [docs/saga.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/saga.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
