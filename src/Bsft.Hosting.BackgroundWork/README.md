# Bsft.Hosting.BackgroundWork

Background work for a generic host, without the web stack: a startup operation that keeps trying until it
succeeds, and bounded, never-blocking pipelines that hand items from a hot path to a batching writer. It is
what a console tool or a CLI takes when it wants the pipelines and nothing else. `Bsft.Hosting` references it,
so a web host gets the same types from either package.

## Install

```bash
dotnet add package Bsft.Hosting.BackgroundWork
```

Depends on `Bsft.Instrumentation` and on `Microsoft.Extensions.Hosting` (the generic host, for `HostOptions`),
`Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging.Abstractions`,
`Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Configuration.Binder`. It has
no `FrameworkReference` on `Microsoft.AspNetCore.App` and no Swashbuckle, Asp.Versioning or OpenAPI package;
a test pins that closure.

## Register

```csharp
using Bsft.Hosting.BackgroundWork.DependencyInjection;

services.AddBsftBackgroundWorkRunOnce<RegisterInstanceService>(configuration);

services.AddBsftBatchingWriter<UsageRecord, UsageBatchCommitter>(configuration)
        .AddBsftPipelineEnrichment<UsageRecord, UsageEnricher>(); // optional, after the writer
// producers inject IBoundedBuffer<UsageRecord> (or IEnrichmentStage<UsageRecord>) and call TryEnqueue
```

The types keep the namespaces they had in `Bsft.Hosting` (`Bsft.Hosting.BackgroundWork`,
`Bsft.Hosting.BackgroundWork.Pipelines`). Only the two registration classes moved to
`Bsft.Hosting.BackgroundWork.DependencyInjection`, which is where the package conventions put a package's
entry points; a host that used `using Bsft.Hosting.DependencyInjection;` for these calls adds that one using.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| `BsftRunOnceOptions` (`Bsft:Hosting:RunOnce:<service type full name>`) | see hosting.md | The backoff of a run-once startup operation. |
| `BsftBoundedPipelineOptions` (`Bsft:Hosting:Pipelines:<item type full name>`) | see hosting.md | Capacity, batch size, flush interval and the stop budgets of a pipeline. |

Both validate on start; the pipeline's stop budgets must fit in `HostOptions.ShutdownTimeout`. Counters are read
through `IPipelineCounters<TItem>` and emitted as `bsft.pipeline.items` on the instrumentation source
`BackgroundWorkDefaults.PipelineInstrumentationSourceName` (`Bsft.Hosting`, the name they always had).

## Extend

`IBatchCommitter<TItem>` is what a host implements to store a batch; `IPipelineEnricher<TItem>` is the optional
per-item enrichment. `RunOnceWithRetryBackgroundServiceBase` is the base class of a run-once operation, and
`IServiceScopeFactory.RunScopedAsync` runs one operation against a scoped service and disposes the scope on every
path.

## Documentation

Topic page: [docs/hosting.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/hosting.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
