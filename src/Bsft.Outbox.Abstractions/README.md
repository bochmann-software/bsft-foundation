# Bsft.Outbox.Abstractions

The contracts a transactional outbox is built on: the lease protocol a durable row answers, the
payload a drain pass carries, and the side effect it applies. Part of the `Bsft.Outbox` family.

## Install

```bash
dotnet add package Bsft.Outbox.Abstractions
```

Requires no further Bsft packages. Reference it directly only to implement one of the seams below;
`Bsft.Outbox` and `Bsft.Outbox.EFCore` pull it transitively.

## Register

```csharp
// This package registers nothing. Register the drain loop with Bsft.Outbox:
services.AddBsftOutbox<MyPayload>("my-outbox", options => options.BatchSize = 50);
```

Nothing else is required.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| — | — | This package carries no options type. The drain loop's options live in `Bsft.Outbox` as `BsftOutboxOptions`, resolved per outbox as named options. |

Validation: none — there is nothing here to configure.

## Extend

`IOutboxStore<TPayload>` (where the rows live and how a lease is won; `Bsft.Outbox.EFCore` implements
it once, generically). `IOutboxSideEffect<TPayload>` (what a row means — publish it, send it, apply
it; a host writes one per outbox). `IOutboxRetrySchedule` (how long a failed row waits;
`Bsft.Outbox` ships an exponential one that honours a wait the failure asked for). `IOutboxRow` (the
lifecycle a stored row exposes to the lease protocol, implemented by the entity that holds it).

## Documentation

Topic page: [docs/outbox.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/outbox.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
