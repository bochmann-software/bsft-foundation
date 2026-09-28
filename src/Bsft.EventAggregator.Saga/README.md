# Bsft.EventAggregator.Saga

The saga engine of the `Bsft.EventAggregator` family: a fluent flow definition, a step guard that lets
exactly one publisher advance a waiting step, durable timeouts on a business calendar, and one commit
path that makes a step's state advance and its reminders durable together or not at all.

## Install

```bash
dotnet add package Bsft.EventAggregator.Saga
```

Requires `Bsft.EventAggregator` (the transport a saga subscribes to and publishes on),
`Bsft.Calendar` (which turns a business-time delay into a due date), `Bsft.Instrumentation` (which
advertises the `Bsft.Saga` instrumentation source) and `Bsft.Data.Contracts` (whose `PagedRequest`
and `PagedResult` are the paging vocabulary the reading seams speak). Nothing
here touches ASP.NET Core, so the engine runs unchanged in a worker or a console tool. Persistence and
correlation are separate packages: `Bsft.EventAggregator.Saga.EFCore` and
`Bsft.EventAggregator.Saga.Correlation`.

## Register

```csharp
services.AddBsftEventAggregator();
services.AddBsftSaga(options =>
{
    options.Timeouts.PollInterval = TimeSpan.FromSeconds(1);
    options.Timeouts.CatchUpPolicy = SagaTimeoutCatchUpPolicy.FireLatestPerSaga;
});

// or bound from configuration, section "Bsft:Saga"
services.AddBsftSaga(configuration);

services.AddBsftSagaFlow<OrderFulfillmentFlow>();
```

`AddBsftEventAggregator` is not optional: the engine subscribes to that aggregator and registers none
of its own. Every flow is registered with `AddBsftSagaFlow<T>`, and the host calls
`app.Services.UseBsftSagaFlows()` once at startup — that is what configures the flows and subscribes
them to their trigger events; without it no saga ever starts. The in-memory saga and timeout stores
are registered with `TryAdd`, so they are the default only until a persistence package or the
application registers its own.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| `BsftSagaOptions.ShouldCreateScopePerStep` | `false` | Retained for source compatibility and no longer changes anything. Both ways into a step always create a scope, because a step resolves scoped services and the root provider cannot serve them. |
| `BsftSagaOptions.Timeouts.PollInterval` | `00:00:05` | How long the dispatcher waits after a pass that found nothing due. |
| `BsftSagaOptions.Timeouts.BatchSize` | `100` | The largest number of due timeouts read per pass. |
| `BsftSagaOptions.Timeouts.CatchUpPolicy` | `FireAll` | What happens to timers that came due while the application was down: fire all of them, fire only the newest per saga instance, or skip the ones older than `MaxCatchUpAge`. |
| `BsftSagaOptions.Timeouts.MaxCatchUpAge` | `1.00:00:00` | How overdue a timer may be before `SkipBeyondMaxAge` discards it. Ignored by the other two policies. |
| `BsftSagaOptions.Timeouts.MaxDispatchAttempts` | `10` | How often a timer may fail to fire before it is retired to the dead-letter state. Without a limit one timer that can never fire keeps its place at the head of every poll and eventually stops every saga timer in the application. |

Validation: `ValidateOnStart()` rejects a negative `PollInterval` or `MaxCatchUpAge`, a `BatchSize` or
`MaxDispatchAttempts` below one, and a `CatchUpPolicy` that is not a defined value — all of them
reported in one message rather than one restart at a time. The dispatcher keeps the same guards on its
own constructor arguments, because it can be constructed directly; neither is stricter than the other.

## Extend

`ISagaStore` persists and loads a saga instance by type and correlation key; the shipped
`InMemorySagaStore` keeps them in a dictionary and loses them with the process.
`ISagaTimeoutStore` does the same for durable timers and owns the whole "confirm, publish, claim"
sequence, so that an implementation is free to choose how it makes the claim atomic; `InMemorySagaTimeoutStore`
is the in-process default. Both are what `Bsft.EventAggregator.Saga.EFCore` replaces.

`ISagaInstanceReader` and `ISagaTimeoutReader` are the listing seams: what an operator needs to answer
"which sagas are stuck" and "which timers are overdue", which the stores deliberately cannot answer
because they are keyed for lookup rather than for search. They are left unimplemented here for the same
reason the stores' durable halves are, and `Bsft.EventAggregator.Saga.EFCore` implements them.

`ISagaPersistenceWindow` decides whether a step's state advance and its timer writes commit as one
unit. The default `ImmediateSagaPersistenceWindow` commits nothing, which is correct for stores that
already make every write durable on its own; a provider that can commit both together registers its
own.

`ISagaCorrelationInitializer` and `ISagaCorrelationAccessor` are declared here and deliberately left
unimplemented: the engine has no notion of correlation of its own, and a step keeps the fresh id of
its own scope until a host registers a bridge. `Bsft.EventAggregator.Saga.Correlation` is that bridge.
`ISagaCorrelationScope` carries the id of a firing timeout through the publish and has a working
default. `ISagaTimeoutPublisher` resolves the persisted payload type and publishes the carried event;
replace it to control deserialization. `ISagaTimeoutDrainer` is not a seam to replace but the supported
way to run one dispatch pass on demand — the way a test models a process spanning weeks without
waiting on a wall clock.

## Documentation

Topic page: [docs/saga.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/saga.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
