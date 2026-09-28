# Observability

BSFT separates telemetry into two layers:

- **Emit (`Bsft.Instrumentation`)** — vendor-neutral. Modules and application code
  emit through `IInstrumentationSource`, a named pairing of a `System.Diagnostics.ActivitySource`
  and a `Meter` (created via `IMeterFactory`). There is no OpenTelemetry dependency here.
- **Collect/export (`Bsft.Observability`)** — wraps the OpenTelemetry SDK. `AddBsftObservability`
  discovers every source advertised via `ObservabilitySourceRegistration` and exports it.

Sources advertise themselves with `AddBsftInstrumentationSource(name)`. Discovery runs at
provider-build time, so the order of `AddBsftObservability` relative to the modules does not
matter.

The emit layer is a package in its own right, not the contract half of `Bsft.Observability`: the
collector implements nothing declared in it, it only reads what the emit layer advertises. That is
why `AddBsftInstrumentationSource` lives there. Six deliberately dependency-light packages —
`Bsft.Mediator`, `Bsft.Data`, `Bsft.Messaging`, `Bsft.Scheduling`, `Bsft.Security` and
`Bsft.EventAggregator.Saga` — advertise their own source from their own entry point, and moving the
call into `Bsft.Observability` would either pull the whole OpenTelemetry SDK into each of them or
silently stop their telemetry from being collected. Until 1.3.5 the package was called
`Bsft.Observability.Abstractions`, which is what made its registration look like a layering defect;
the 1.3.5 migration notes have the rename.

## Enabling

```csharp
builder.Services
    .AddBsftMediator(c => { c.RegisterServicesFromAssembly(asm); c.EnableTracing(); })
    .AddBsftSaga()                       // saga telemetry is always on (no-op without a listener)
    .AddBsftObservability(o =>
    {
        o.ServiceName = "orders-api";
        o.EnableConsoleExporter = builder.Environment.IsDevelopment();
        // OTLP is the default exporter; set o.EnableOtlpExporter = false to run without a collector.
    });
```

Options can also be bound from configuration: `AddBsftObservability(configuration)`, which reads
`BsftObservabilityOptions.SectionName` (`"Bsft:Observability"`) from the configuration root. Earlier
versions took the section itself; the overload now takes the root, as every other entry point in the
framework does.
Standard `OTEL_*` environment variables (endpoint, service name, sampler) are honored; the options
only override them when set. The `ConfigureTracerProvider` / `ConfigureMeterProvider` hooks let you
add your own exporters, samplers, or instrumentation after the framework defaults.

### Log export

Traces and metrics are exported by default; log export is opt-in, because it attaches a provider to
the application's own `ILoggingBuilder` pipeline rather than adding a side-channel collector, which is
a bigger behavioral change than adding a trace or metric provider:

```csharp
builder.Services.AddBsftObservability(o =>
{
    o.ServiceName = "orders-api";
    o.EnableLogging = true;
    // Uses the same EnableOtlpExporter / EnableConsoleExporter / OtlpEndpoint / OtlpProtocol options
    // as traces and metrics.
});
```

or from configuration:

```json
{
  "Bsft": {
    "Observability": {
      "ServiceName": "orders-api",
      "EnableLogging": true,
      "OtlpEndpoint": "http://collector:4317"
    }
  }
}
```

`ILogger` categories are collected as a whole rather than discovered like activity sources and
meters; use the standard `Logging` configuration section or `ILoggingBuilder` filters to scope what is
collected. The `ConfigureLoggerProvider` hook runs after the framework defaults, mirroring
`ConfigureTracerProvider` / `ConfigureMeterProvider`.

## Telemetry contract

These names are a stable public contract.

### Activity sources / meters

| Name | Emitted by |
|---|---|
| `Bsft.Mediator` | Command/query/notification dispatch (enabled by `EnableTracing()`). |
| `Bsft.Saga` | Saga start, steps, completion, and durable timeouts. |
| `Bsft.Data` | Full-text search execution (registered by `AddBsftDataQueryFiltering`). |
| `Bsft.Scheduling` | Job enqueue, claim and execution (registered by `AddBsftScheduling`). |

### Metrics

| Instrument | Type | Unit | Tags |
|---|---|---|---|
| `bsft.mediator.request.duration` | Histogram | s | `request_name`, `outcome` |
| `bsft.mediator.requests` | Counter | | `request_name`, `outcome` |
| `bsft.mediator.failures` | Counter | | `request_name` |
| `bsft.saga.started` | Counter | | `saga.type` |
| `bsft.saga.completed` | Counter | | `saga.type` |
| `bsft.saga.step.duration` | Histogram | s | `saga.type`, `outcome` |
| `bsft.saga.timeout.scheduled` | Counter | | `saga.type` |
| `bsft.saga.timeout.fired` | Counter | | `saga.type` |
| `bsft.saga.timeout.canceled` | Counter | | `saga.type` |
| `bsft.saga.timeout.missed` | Counter | | `saga.type` |
| `bsft.data.search.duration` | Histogram | s | `search.group` |
| `bsft.data.search.results` | Histogram | | `search.group` |
| `bsft.job.enqueued` | Counter | | `job.name` |
| `bsft.job.claimed` | Counter | | `job.name` |
| `bsft.job.skipped` | Counter | | `job.name` |
| `bsft.job.execution.duration` | Histogram | s | `job.name`, `outcome` |

`outcome` is one of `success`, `expected_error`, `error` (mediator) or `executed`, `skipped` (saga).
Expected framework exceptions (`BsftException` and its subtypes) are recorded as `expected_error`
and do **not** mark the span as an error or increment `bsft.mediator.failures`.

### Span attributes

`correlation_id` is recorded on incoming-request and mediator spans, but only when the value is
well-formed (`^[A-Za-z0-9._-]{1,128}\z`), so unvalidated client input never reaches telemetry. It is
never used as a metric dimension. Exception detail follows the OpenTelemetry exception convention;
treat the trace backend as a sensitive sink.

## Custom sources

```csharp
builder.Services.AddBsftInstrumentationSource("MyApp.Orders");

public sealed class OrderService(IInstrumentationSourceProvider sources)
{
    private readonly IInstrumentationSource _telemetry = sources.Get("MyApp.Orders");

    public void Import(string orderId)
    {
        using var activity = _telemetry.StartActivity("ImportOrder");
        activity?.SetTag("order.id", orderId);
        _telemetry.GetOrCreateCounter("orders.imported").Add(1);
    }
}
```

A runnable example is wired in `samples/Bsft.Sample.WebApi` (the `Sample.Orders` source).

## Compatibility (1.1.0)

The mediator and saga contracts moved into `Bsft.Mediator.Abstractions` (the
`Bsft.Mediator.Abstractions` namespace is unchanged). Type forwarders preserve binary compatibility, so
this is an additive change. `AddBsftObservability` pulls OpenTelemetry 1.15.x, which raises the
`Microsoft.Extensions.*` floor to 9.0.x across the framework.
