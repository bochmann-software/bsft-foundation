# Bsft.Observability

The collection and export half of the observability family: `AddBsftObservability` wires the
OpenTelemetry SDK for traces and metrics, exports over OTLP or the console, and discovers every
instrumentation source the packages in the application advertised.

## Install

```bash
dotnet add package Bsft.Observability
```

Requires `Bsft.Instrumentation` (pulled transitively) and `Bsft.Correlation.Abstractions`,
plus the OpenTelemetry SDK with the ASP.NET Core, HttpClient, runtime and EF Core instrumentations and
the OTLP and console exporters.

## Register

```csharp
services.AddBsftObservability(options =>
{
    options.ServiceName = "orders-api";
    options.OtlpEndpoint = new Uri("http://collector:4317");
});

// or bound from configuration, section "Bsft:Observability"
services.AddBsftObservability(configuration);
```

Nothing else is required. Source discovery runs at provider-build time rather than at registration
time, so it sees every source regardless of whether a module was registered before or after this
call — the order does not matter. Unset OTLP values fall back to the standard `OTEL_*` environment
variables.

## Configure

`BsftObservabilityOptions.SectionName` is `"Bsft:Observability"`.

| Option | Default | Effect |
| --- | --- | --- |
| `BsftObservabilityOptions.ServiceName` | `null` | Resource service name; `null` leaves it to the SDK or `OTEL_SERVICE_NAME`. |
| `BsftObservabilityOptions.ServiceVersion` | `null` | Resource service version. |
| `BsftObservabilityOptions.EnableTracing` | `true` | Whether the tracer provider is configured at all. |
| `BsftObservabilityOptions.EnableMetrics` | `true` | Whether the meter provider is configured at all. |
| `BsftObservabilityOptions.EnableLogging` | `false` | Whether the OpenTelemetry logging provider is attached to `ILoggingBuilder`; opt-in because it changes the app's own logging pipeline. |
| `BsftObservabilityOptions.EnableAspNetCoreInstrumentation` | `true` | Incoming request traces and metrics. |
| `BsftObservabilityOptions.EnableHttpClientInstrumentation` | `true` | Outgoing `HttpClient` traces and metrics. |
| `BsftObservabilityOptions.EnableEntityFrameworkCoreInstrumentation` | `false` | EF Core traces; off because the instrumentation package is pre-release. |
| `BsftObservabilityOptions.EnableRuntimeMetrics` | `true` | Process runtime metrics (GC, threads). |
| `BsftObservabilityOptions.EnableOtlpExporter` | `true` | The OTLP exporter, which is the default sink. |
| `BsftObservabilityOptions.OtlpEndpoint` | `null` | OTLP endpoint; `null` leaves it to the SDK or `OTEL_EXPORTER_OTLP_ENDPOINT`. |
| `BsftObservabilityOptions.OtlpProtocol` | `null` | OTLP protocol; `null` leaves it to the SDK. |
| `BsftObservabilityOptions.EnableConsoleExporter` | `false` | Console exporter, useful in development. |
| `BsftObservabilityOptions.CorrelationHeaderName` | `X-Correlation-Id` | Request header read to tag spans with `correlation_id` (`ObservabilityDefaults.CorrelationTagName`). |
| `BsftObservabilityOptions.AdditionalActivitySources` | empty | Activity source names to collect beyond the discovered ones. |
| `BsftObservabilityOptions.AdditionalMeters` | empty | Meter names to collect beyond the discovered ones. |
| `BsftObservabilityOptions.ConfigureTracerProvider` | `null` | Hook run after the framework's tracer defaults. |
| `BsftObservabilityOptions.ConfigureMeterProvider` | `null` | Hook run after the framework's meter defaults. |
| `BsftObservabilityOptions.ConfigureLoggerProvider` | `null` | Hook run after the framework's logger defaults. |

Validation: a service name may be absent but must not be blank when it is given at all, and the
correlation header must be named. A failure names the member, and it is thrown from
`AddBsftObservability` as well as on start.

Only correlation IDs matching `^[A-Za-z0-9._-]{1,128}\z` are recorded as a span tag, mirroring
`CorrelationIdMiddleware`, so unvalidated client input never reaches telemetry.

## Extend

`ConfigureTracerProvider` and `ConfigureMeterProvider` are the seams: both run after the framework's
own defaults, so a host adds an exporter, a sampler or a processor without this package knowing
about it. A package that emits telemetry does not extend anything here — it advertises its source
with `AddBsftInstrumentationSource` from `Bsft.Instrumentation` and is discovered.

## Documentation

Topic page: [docs/observability.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/observability.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
