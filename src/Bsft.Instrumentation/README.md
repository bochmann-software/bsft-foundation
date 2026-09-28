# Bsft.Instrumentation

The vendor-neutral instrumentation layer every emitting `Bsft.*` package uses: a named pairing of an
`ActivitySource` and a `Meter`, the provider that hands one out, and the registration record a
collector discovers a source through — with no OpenTelemetry dependency of any kind.

## Install

```bash
dotnet add package Bsft.Instrumentation
```

No Bsft packages at all. It takes `Microsoft.Extensions.Diagnostics` for the meter factory,
`System.Diagnostics.DiagnosticSource` for the activity source and
`Microsoft.Extensions.DependencyInjection.Abstractions` for the registration surface. `Bsft.Observability` reads what this
package advertises; a host that never installs a collector still compiles and runs, because emission
is a no-op while nobody is listening.

## Register

```csharp
services.AddBsftInstrumentationSource("MyApp.Orders");

// traces only
services.AddBsftInstrumentationSource("MyApp.Orders", ObservabilitySignals.Traces);
```

Nothing else is required. The call advertises one source and idempotently ensures the meter factory
and `IInstrumentationSourceProvider` are present, so there is no separate package-wide entry point to
call first. Calling it twice for the same name registers the advertisement once, so a collector's
discovery is free of duplicates.

## Configure

None. The package binds no configuration section and exposes no options type; what is collected is
decided by `BsftObservabilityOptions` in `Bsft.Observability`.

| Option | Default | Effect |
| --- | --- | --- |
| — | — | — |

Validation: `AddBsftInstrumentationSource` rejects a blank source name, and so does
`ObservabilitySourceRegistration`. Neither is configuration.

## Extend

`IInstrumentationSourceProvider` (resolves a name to a source, caching one per name) is registered
with `TryAdd`, so a host that registered its own provider keeps it. `IInstrumentationSource` (starts
activities and caches counter and histogram instruments on a meter) is the type a `*Telemetry` class
injects. `ObservabilitySignals` decides whether a source is advertised for traces, metrics or both.

Every name these take is a static one the code decides. A source is cached for the life of the
provider and an instrument name becomes a series in whatever backend collects it, so a name built from
a tenant, a user or an entity id grows both without bound. Request data goes on an activity as a tag,
where the deployment decides what a tag may carry.

## Documentation

Topic page: [docs/observability.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/observability.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
