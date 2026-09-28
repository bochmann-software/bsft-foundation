# Bsft.Scheduling.Correlation

The adapter between the `Bsft.Correlation` and `Bsft.Scheduling` families: a job enqueued while
handling a request carries that request's correlation id into an execution that may happen hours later.

## Install

```bash
dotnet add package Bsft.Scheduling.Correlation
```

Requires `Bsft.Correlation` and `Bsft.Scheduling` (both pulled transitively) and no further Bsft
packages. It exists as a package of its own because the scheduler must not drag the ASP.NET Core
request pipeline into a background component: without this bridge the scheduler runs exactly as before
and runs simply carry no correlation id.

## Register

```csharp
services.AddBsftCorrelation();
services.AddBsftScheduling();
services.AddBsftSchedulingCorrelation();
```

Nothing else is required, and the order does not matter: the core registers its ambient-only default
with `TryAdd` while this registers the richer accessor with a plain `Add`, so the bridge wins either
way.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| — | — | — |

None. The package declares no options type and no configuration section; what it registers is a single
scoped service. Validation: none.

## Extend

None. The package supplies one implementation of the core's `IJobCorrelationAccessor` seam,
`CorrelationContextJobAccessor`, which reads `ICorrelationContext` and lets the ambient id of an
executing job win over the scope's own — without that precedence a chain of jobs, one enqueuing the
next, would start a new correlation on every hop, because each run gets a fresh service scope whose
`CorrelationContext` mints a new id. A host that wants different behaviour registers its own
`IJobCorrelationAccessor` and leaves this package out.

## Documentation

Topic page: [docs/scheduling.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/scheduling.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
