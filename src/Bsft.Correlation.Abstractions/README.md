# Bsft.Correlation.Abstractions

The correlation contract of the `Bsft.Correlation` family: the request-scoped ID a component reads,
and the header name it travels under. Reference this rather than `Bsft.Correlation` from anything that
only needs to *read* a correlation ID.

## Install

```bash
dotnet add package Bsft.Correlation.Abstractions
```

Requires nothing at all. The empty dependency list is the point of the package and is pinned by a test:
`Bsft.Correlation` carries a `FrameworkReference` to `Microsoft.AspNetCore.App` for its middleware, and
a Blazor WebAssembly bundle or a console tool must not acquire the server stack just to read an ID.

## Register

```csharp
// Nothing. An .Abstractions package has no DI entry point.
services.AddBsftCorrelation(); // in Bsft.Correlation, which supplies the implementation
```

Nothing else is required. A host registers the implementation from `Bsft.Correlation`; a library that
consumes the contract references only this package and takes `ICorrelationContext` in its constructor.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| — | — | No options type. `CorrelationDefaults.CorrelationId` (`X-Correlation-Id`) is a constant rather than a setting, so that the middleware, the outbound handler and every adapter package cannot disagree about it. |

Validation: none — the package has no configuration and registers nothing.

## Extend

`ICorrelationContext` — supplies the correlation ID of the current scope. `Bsft.Correlation` ships
`CorrelationContext`, a scoped holder the middleware writes on each request; replace it where the ID
comes from somewhere other than the inbound header, for instance a message envelope in a worker with no
HTTP pipeline (which is what `Bsft.Messaging.Correlation` and `Bsft.Scheduling.Correlation` do).

`CorrelationIdDelegatingHandler` used to live here and now lives in
[`Bsft.Correlation.Http`](../Bsft.Correlation.Http/README.md): it is a live implementation, which
`docs/package-conventions.md` keeps out of an `.Abstractions` package by name. That package is
client-safe in the same way this one is — its closure is this package and the DI abstractions — so a
client that wants the outbound half takes it and still never sees ASP.NET Core.

## Documentation

Topic page: [docs/correlation.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/correlation.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
