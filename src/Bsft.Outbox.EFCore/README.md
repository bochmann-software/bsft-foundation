# Bsft.Outbox.EFCore

One EF Core implementation of the outbox lease protocol, generic over the row it leases. A second
outbox in a host is a second table and a second registration, never a second protocol.

## Install

```bash
dotnet add package Bsft.Outbox.EFCore
```

Requires `Bsft.Outbox.Abstractions` (pulled transitively) and `Microsoft.EntityFrameworkCore`.

## Register

```csharp
services.AddSingleton<IOutboxRowSource<MyOutboxRowEntity, MyPayload>, MyOutboxRowSource>();
services.AddSingleton<IOutboxStore<MyPayload>, EFCoreOutboxStore<MyOutboxRowEntity, MyPayload>>();
```

The entity derives from `OutboxRow`, which brings the lifecycle columns the lease protocol reads and
writes; its own columns are whatever the payload needs. Configure the entity as usual, including the
index over the claim predicate, and add the set to a context interface of your own.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| — | — | This package carries no options type. What a pass is bounded by belongs to the drain loop and lives in `Bsft.Outbox` as `BsftOutboxOptions`. |

Validation: none — there is nothing here to configure.

## Extend

`IOutboxRowSource<TEntity, TPayload>` (where this outbox's rows live, how one is read, and the column
length a recorded failure is cut to). The lease protocol itself is deliberately not a seam: the audit
that found it sound was about this one implementation.

## Documentation

Topic page: [docs/outbox.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/outbox.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
