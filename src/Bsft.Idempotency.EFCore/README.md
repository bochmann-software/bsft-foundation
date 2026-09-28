# Bsft.Idempotency.EFCore

The shared idempotency record store: one table on the application's own context, whose unique index
is what makes a duplicate request a duplicate across every replica rather than within one process.

## Install

```bash
dotnet add package Bsft.Idempotency.EFCore
```

Requires `Bsft.Idempotency` (pulled transitively), `Bsft.Core` for the framework's `PollLoop` the
pruner runs on, and `Microsoft.EntityFrameworkCore`.

## Register

Implement `IIdempotencyDbContext` on the application's context and apply the configuration:

```csharp
public DbSet<IdempotencyRecordEntity> IdempotencyRecords => Set<IdempotencyRecordEntity>();

protected override void OnModelCreating(ModelBuilder modelBuilder)
    => modelBuilder.ApplyConfiguration(new IdempotencyRecordEntityConfiguration());
```

```csharp
services.AddBsftIdempotency(configuration);
services.AddBsftIdempotencyEFCore<AppDbContext>();
```

The call replaces the in-memory store rather than adding to it, so the order of the two lines does
not matter. Do not leave the configuration to convention: without it the key columns are mapped with
no unique index and no length, and the store is there and protects nothing.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| — | — | The package declares no options of its own: everything an operator decides is on `BsftIdempotencyOptions`, and everything the schema decides is in `IdempotencyRecordEntityConfiguration`. |

Validation: none of its own. The retention and the lease that shape the table come from
`BsftIdempotencyOptions`, which `AddBsftIdempotency` validates at start-up.

## Extend

None. The store and the pruner are `internal`; a host that wants different persistence implements
`IIdempotencyStore` from `Bsft.Idempotency` and does not take this package.

## Documentation

Topic page: [docs/idempotency.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/idempotency.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
