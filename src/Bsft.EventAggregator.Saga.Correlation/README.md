# Bsft.EventAggregator.Saga.Correlation

The bridge between `Bsft.Correlation` and the `Bsft.EventAggregator.Saga` family: it makes a saga step
run under the correlation id of whatever caused it — the request that published the trigger event, or
the request that scheduled the timer that fired days later — instead of one minted for the step's own
service scope.

## Install

```bash
dotnet add package Bsft.EventAggregator.Saga.Correlation
```

Requires `Bsft.EventAggregator.Saga` and `Bsft.Correlation`, and carries a `FrameworkReference` to
`Microsoft.AspNetCore.App` for the middleware. The saga engine itself does not reference
`Bsft.Correlation` — that would drag the request pipeline into a background component — which is
exactly why this package exists.

## Register

```csharp
services.AddBsftCorrelation();
services.AddBsftSaga();
services.AddBsftSagaCorrelation();

app.UseBsftCorrelation();
app.UseBsftSagaCorrelation();
```

`AddBsftSagaCorrelation` registers the accessor that stamps a correlation id onto a scheduled timeout
and reads it back when the timeout fires, and the initializer that hands the id a step is running
under to that step's own correlation context. `UseBsftSagaCorrelation` closes the remaining gap — a
step triggered directly by an event a request publishes — and is placed after `UseBsftCorrelation`,
which is what resolves the id for the request. The timeout path needs no pipeline step. Without either
call nothing breaks: a step simply keeps the fresh id of its own scope.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| — | — | The package has no options type. It is two registrations and one middleware, with nothing to tune. |

Validation: none — there is nothing to configure.

## Extend

None. This package supplies implementations rather than declaring seams: `CorrelationContextSagaAccessor`
for `ISagaCorrelationAccessor` and `SagaCorrelationInitializer` for `ISagaCorrelationInitializer`, both
declared in `Bsft.EventAggregator.Saga` and both registered with `TryAdd`, so a host that wants its own
bridge registers it first. The ambient id of a firing timeout deliberately wins over the scope's own
id: without that precedence a chain of timeouts — a reminder that schedules the next reminder — would
start a new correlation on every hop.

## Documentation

Topic page: [docs/saga.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/saga.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
