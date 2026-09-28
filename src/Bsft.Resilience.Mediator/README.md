# Bsft.Resilience.Mediator

The behavior package of the `Bsft.Resilience` family: a mediator pipeline behavior that retries
requests marked with `[Retryable]` on transient failures, over a Polly pipeline built once per
process.

## Install

```bash
dotnet add package Bsft.Resilience.Mediator
```

Requires `Bsft.Mediator.Abstractions`, `Bsft.Core` and `Polly.Core` (all pulled transitively). It
does **not** reference `Bsft.Resilience`: the HTTP half and the mediator half share a name and
nothing else, so a host retrying commands takes neither the HTTP resilience stack nor the
correlation handler along.

## Register

```csharp
services.AddBsftResilienceMediator(options =>
{
    options.MaxRetryAttempts = 3;
});

// or bound from configuration, section "Bsft:Resilience:Mediator"
services.AddBsftResilienceMediator(configuration);
```

Then mark the requests that may safely run twice:

```csharp
[Retryable]
public sealed record SyncProductPriceQuery(Guid ProductId) : IQuery<decimal>;
```

With `UseDefaults()` the position is the framework's, not the registration order: retry runs outside
the transaction behavior, so each attempt gets its own transaction, and inside authorization,
validation and audit, which therefore run once per request. Without `UseDefaults()`, registration
order is the pipeline order, so call this after `AddBsftMediator`. Apply the marker only to
idempotent requests — retrying a command with side effects duplicates them.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| `BsftResilienceMediatorOptions.MaxRetryAttempts` | `3` | Retries performed after the initial invocation. |
| `BsftResilienceMediatorOptions.BaseDelay` | `200ms` | Base delay between retries; grows exponentially and is randomized with jitter. |
| `BsftResilienceMediatorOptions.ShouldRetry` | `IsTransientFailure` | Decides which exception is transient. The default retries everything except cancellation and the deterministic `Bsft.Core` domain exceptions (validation, business rule, not found, forbidden, conflict). |

Validation: a negative retry count, a negative delay and a null predicate are rejected at
registration and again on `ValidateOnStart`, each naming the member and the offending value.
`ShouldRetry` has no configuration binding — it is a delegate — so a section can set the first two
members only.

## Extend

`IMediatorResiliencePipelineProvider` — supplies the shared `ResiliencePipeline` the behavior
executes through. The default builds it once from the options and reuses it; replace the
registration to run requests through a pipeline this package cannot describe (a bulkhead, a
hedging strategy, a circuit breaker over the mediator).

## Documentation

Topic page: [docs/resilience.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/resilience.md#mediator-retry-optional-package).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
