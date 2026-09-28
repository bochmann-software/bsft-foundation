# Bsft.Resilience

Retry, circuit breaker and timeout for outgoing HTTP clients, and classifier-driven retry for a
partially consumed `IAsyncEnumerable<T>`. Built on Polly. The mediator half of the family —
`RetryBehavior` and `[Retryable]` — ships separately in `Bsft.Resilience.Mediator`.

## Install

```bash
dotnet add package Bsft.Resilience
```

Requires `Bsft.Correlation.Http`, plus `Microsoft.Extensions.Http.Resilience` and
`Polly.Core`. The outbound half of correlation is all the resilient client needs, so ASP.NET Core stays
out of this package's closure; a host still calls `AddBsftCorrelation()` from `Bsft.Correlation` to
supply the `ICorrelationContext` the handler reads.

## Register

```csharp
// a named HTTP client with correlation propagation and the resilience pipeline
services.AddBsftResilientHttpClient("billing", options => options.AttemptTimeout = TimeSpan.FromSeconds(5));

// or bound from configuration, section "Bsft:Resilience"
services.AddBsftResilientHttpClient("billing", configuration);
```

Nothing else is required. An existing client builder takes the pipeline directly with
`services.AddHttpClient("billing").AddBsftResilience(...)`; `AddBsftResilientHttpClient` is that plus
the correlation handler, registered inside the resilience envelope so each retried attempt still
carries the correlation header.

## Configure

`BsftResilienceOptions` describes **one named client**, of which an application legitimately has
several with different retry budgets, so it is bound and validated per registration call rather than
once for the container. The mediator pipeline is configured by `BsftResilienceMediatorOptions` in
`Bsft.Resilience.Mediator`, which is a container-wide `IOptions<T>` because there is exactly one of
it per process.

`BsftResilienceOptions.SectionName` is `"Bsft:Resilience"` — the shared default each client's
registration call then adjusts. It is never registered in the container, for the same reason.

| Option | Default | Effect |
| --- | --- | --- |
| `BsftResilienceOptions.MaxRetryAttempts` | `3` | Attempts after the first. |
| `BsftResilienceOptions.BaseDelay` | `2 s` | Grows exponentially, with jitter. |
| `BsftResilienceOptions.AttemptTimeout` | `10 s` | Timeout of one attempt. |
| `BsftResilienceOptions.TotalRequestTimeout` | `30 s` | Timeout spanning all attempts. |
| `BsftResilienceOptions.FailureRatio` | `0.1` | Failure ratio in the window that trips the breaker. |
| `BsftResilienceOptions.SamplingDuration` | `30 s` | Window the breaker evaluates. |
| `BsftResilienceOptions.MinimumThroughput` | `100` | Actions needed in the window before the breaker can trip. |
| `BsftResilienceOptions.BreakDuration` | `5 s` | How long the circuit stays open before half-open. |

Validation: retry counts and delays must not be negative; the attempt timeout must be positive and
strictly smaller than the total, the failure ratio must be in `(0, 1]`, the sampling window must be at
least twice the attempt timeout, the minimum throughput must be at least 2, and the break duration
must be positive. These are Polly's own rules, stated here so the failure lands on the registration
call — where the stack still names the client — rather than on the first request through it. A
failure names the member and the offending value.

## Stream retry

`ResilientStreamExtensions.WithRetry` retries a stream, which the pipelines above cannot: they retry a
call whose failed attempt produced nothing the caller saw. It takes no registration — the factory, the
classifier and the budget are per call, so there is nothing container-wide to resolve.

```csharp
var tokens = ResilientStreamExtensions.WithRetry(
    ct => client.StreamCompletionAsync(prompt, ct),
    options => options.MaxRetryAttempts = 5,
    cancellationToken);

await foreach (var token in tokens)
    Console.Write(token);
```

The hazard the API refuses to hide: once an item has been yielded it is gone downstream, and retrying
re-invokes the factory from the beginning. `PartialStreamPolicy` is therefore the caller's choice, and
it defaults to `RethrowMidStream` — retry only while nothing has been yielded, so no item is ever
produced twice. `RestartFromStart` opts into restarting, and the overload taking a
`StreamRetryContext` lets the factory resume from `ItemsYielded` instead of duplicating.

| Option | Default | Effect |
| --- | --- | --- |
| `BsftResilienceStreamOptions.MaxRetryAttempts` | `3` | Retries for the whole stream; progress does not replenish them. |
| `BsftResilienceStreamOptions.BaseDelay` | `200 ms` | Doubles per retry. |
| `BsftResilienceStreamOptions.MaxDelay` | `30 s` | Ceiling for one delay. |
| `BsftResilienceStreamOptions.UseJitter` | `true` | Scales the delay by a factor in `[0.5, 1)`. |
| `BsftResilienceStreamOptions.ShouldRetry` | `IsTransientFailure` | The classifier. |
| `BsftResilienceStreamOptions.PartialStreamPolicy` | `RethrowMidStream` | What a mid-stream failure does. |
| `BsftResilienceStreamOptions.TimeProvider` | `TimeProvider.System` | What the backoff waits on. |

The options are validated on the `WithRetry` call, before anything is enumerated. See
[docs/resilience.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/resilience.md)
for the full semantics.

## Extend

None of its own beyond the options above: the pipeline is Polly's, and a client that needs a shape
this package does not describe builds it on its own `IHttpClientBuilder`. What counts as a transient
failure for a *mediator* request is decided by `BsftResilienceMediatorOptions.ShouldRetry` in
`Bsft.Resilience.Mediator`.

## Documentation

Topic page: [docs/resilience.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/resilience.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
