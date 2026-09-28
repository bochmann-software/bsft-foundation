# Bsft.EventAggregator.Saga

A saga is a long-running business process expressed as a sequence of steps, each
woken by an event. State is persisted between steps, so the process survives a
restart, and the engine guarantees a given event advances a given saga instance
exactly once.

This page covers the engine, its state persistence and its packages. Durable
timers have their own page: [saga-timeouts.md](saga-timeouts.md).

| Package | Contents |
| --- | --- |
| `Bsft.EventAggregator.Saga` | The engine, the flow builder, the in-memory stores. |
| `Bsft.EventAggregator.Saga.EFCore` | EF Core state store, durable timeout store, the atomic persistence window. |
| `Bsft.EventAggregator.Saga.Correlation` | Carries a `Bsft.Correlation` id into steps and through timers. |

## Defining a flow

A flow implements `ISagaFlow` and describes itself through the builder:

```csharp
public sealed class OrderFulfillmentFlow : ISagaFlow
{
    public void Configure(ISagaBuilderFactory factory)
    {
        factory.WithState<OrderState>()
            .StartWith<OrderPlaced>(e => e.OrderId.ToString())
            .Then((ctx, e) =>
            {
                ctx.State.Total = e.Total;
                return ctx.PublishAsync(new ReserveStock(e.OrderId));
            })
            .WaitFor<StockReserved>(e => e.OrderId.ToString())
            .Then((ctx, _) => { ctx.State.IsReserved = true; return Task.CompletedTask; })
            .Complete();
    }
}
```

The selector passed to `StartWith` and `WaitFor` is the **correlation key** — it
extracts, from the event, the value identifying which saga instance the event
belongs to. Two events with the same key reach the same instance.

Its type is `Func<TEvent, string>?`, so the key is always a **string**: a `Guid`
or `int` id needs an explicit `.ToString()`. It is also optional — omitting it
gives a flow with a single instance, which is what `Stateless()` flows usually
want.

Builder surface:

| Method | Meaning |
| --- | --- |
| `WithState<TState>()` | Start a stateful flow. `TState` needs a parameterless constructor. |
| `Stateless()` | Start a flow with no state of its own (`EmptyState`). |
| `StartWith<TEvent>(selector)` | The event that creates an instance. |
| `WaitFor<TEvent>(selector)` | The next awaited event. |
| `Or<TEvent>(selector)` | An alternative event for the same step. |
| `Then(action)` | The step body. |
| `When(predicate, action)` / `WhenNot(predicate, action)` | A guarded step body. |
| `Complete()` | Finish the definition and auto-complete the saga after the last step. |
| `Build()` | Finish the definition without auto-completion. |

A step body is a delegate, `Func<ISagaContext<TState>, TEvent, Task>` — there is
no handler interface to implement.

## The step context

```csharp
public interface ISagaContext<TState>
{
    TState State { get; }
    string? CorrelationKey { get; }
    IServiceProvider Services { get; }
    T GetService<T>() where T : notnull;
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default);
    void MarkComplete();
    void ScheduleTimeout<TEvent>(string name, TimeSpan delay, TEvent timeoutEvent, string? calendarKey = null);
    void ScheduleTimeout<TEvent>(string name, DateTimeOffset dueAt, TEvent timeoutEvent);
    void CancelTimeout(string name);
    void CancelAllTimeouts();
}
```

`MarkComplete()` ends the saga early; the instance is removed from the store.
The timeout members are documented in [saga-timeouts.md](saga-timeouts.md).

## Setup

```csharp
services.AddBsftEventAggregator();

services.AddBsftSaga(options =>
{
    options.Timeouts.PollInterval = TimeSpan.FromSeconds(5);
});

// or bound from configuration, section "Bsft:Saga"
services.AddBsftSaga(configuration);

services.AddBsftSagaEFCoreWithTimeouts<AppDbContext>();   // from Bsft.EventAggregator.Saga.EFCore
services.AddBsftSagaFlow<OrderFulfillmentFlow>();

// after the provider is built
app.Services.UseBsftSagaFlows();
```

The three calls, and what each is for:

| Call | Purpose |
| --- | --- |
| `AddBsftSaga(Action<BsftSagaOptions>?)` | Registers the engine, stores, telemetry and the timeout dispatcher. |
| `AddBsftSagaFlow<T>()` | Registers one `ISagaFlow`. |
| `UseBsftSagaFlows()` | On `IServiceProvider`. Resolves the flows, calls `Configure`, subscribes the engine to the event aggregator. |

**`UseBsftSagaFlows()` is required.** Without it the flows are registered but never
subscribed, and no event reaches a saga. The engine also requires the event
aggregator, so `AddBsftEventAggregator()` must be present.

`AddBsftSaga` starts a hosted service (the timeout dispatcher) and pulls in
`Bsft.Calendar` and a `TimeProvider`, so an application gains a background poller
by registering it.

### Options

`BsftSagaOptions`:

| Member | Default | Meaning |
| --- | --- | --- |
| `SectionName` | `"Bsft:Saga"` | The configuration section the `IConfiguration` overload binds. |
| `ShouldCreateScopePerStep` | `false` | Retained for source compatibility; no longer has an effect. |
| `Timeouts` | a `SagaTimeoutOptions` | Timer dispatch settings; see [saga-timeouts.md](saga-timeouts.md). |

The in-memory saga and timeout stores are the default and are registered with `TryAdd`, so an
application that registers its own `ISagaStore` before `AddBsftSaga` keeps it and a persistence
package called afterwards replaces it. `UseInMemoryStore()` selected them explicitly and is gone,
since there is nothing left for it to do. The options are validated by `ValidateOnStart()`.

**Every step now runs in its own DI scope, whatever `ShouldCreateScopePerStep`
says.** It used to be the caller's job to switch that on, and leaving it off gave
the step the root provider — so a step resolving a scoped service such as a
`DbContext` reused one instance across unrelated steps, and under the scope
validation an ASP.NET Core Development host turns on it threw instead. The
framework itself resolves a scoped service in a step (the correlation accessor
that stamps a timeout), so this could not stay a caller's choice. The option is
kept so existing setup code still compiles and can be removed from it.

## Persistence

The default store is `InMemorySagaStore`: instances die with the process, which
is right for tests and demos and wrong for anything else. For real use, point the
engine at a `DbContext`:

```csharp
// state only; timers stay in memory
services.AddBsftSagaEFCore<AppDbContext>();

// state and timers, committed together
services.AddBsftSagaEFCoreWithTimeouts<AppDbContext>();
```

These are extensions on `IServiceCollection` from the EfCore package, called after
`AddBsftSaga`. Until 1.3.5 they were `UseDbProvider<TContext>()` and
`UseDbProviderWithTimeouts<TContext>()` on the options object.

The context implements `ISagaDbContext`, or `ISagaTimeoutDbContext` for the
second form:

```csharp
public interface ISagaDbContext
{
    DbSet<SagaStateEntity> SagaStates { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface ISagaTimeoutDbContext : ISagaDbContext
{
    DbSet<SagaTimeoutEntity> SagaTimeouts { get; }
}
```

The split is deliberate: durable timers are opt-in, and putting their table on
the state contract would force every existing implementation to change for a
feature it may not use.

Apply the shipped entity configurations in `OnModelCreating` and create the
migration in the application. The framework ships the mapping; the application
owns the schema.

## Atomicity

With `AddBsftSagaEFCoreWithTimeouts`, a step's state advance and its timer writes
commit in **one** `SaveChangesAsync`. The flow host opens a persistence window
after the step body returns, both stores stage into it, and a single commit makes
the set durable — so a failing timer write takes the state advance down with it
rather than leaving a saga standing past a reminder that was never written.

This is covered by `SagaAtomicPersistenceTests.AFailingTimerWrite_LeavesTheSagaStateUnchanged`
and `AStepThatAdvancesAndSchedules_CommitsBothEffects` in
`tests/Bsft.EventAggregator.Saga.EFCore.Tests`.

The step body runs *before* the window opens, so business code never holds a
database connection while it works.

## Telemetry

The engine reports on the `Bsft.Saga` instrumentation source: `bsft.saga.started`,
`bsft.saga.completed`, `bsft.saga.step.duration` and the four timeout counters,
plus a `saga.step` span per step. See [observability.md](observability.md).

## Testing

`Bsft.Testing` ships a saga harness that runs a flow against a virtual clock, so
a two-day timer is tested in microseconds:

```csharp
using var harness = new SagaHarnessBuilder()
    .WithFlow<OrderFulfillmentFlow>()
    .Build();

await harness.PublishAsync(new OrderPlaced(orderId, 100m));
await harness.AdvanceTimeAsync(TimeSpan.FromDays(3));

var saga = await harness.GetSagaAsync<OrderState, OrderPlaced>(orderId.ToString());
```

See [testing.md](testing.md).

## The satellite packages

### Bsft.EventAggregator.Saga.EFCore

The relational persistence of the family. It registers nothing on its own account: both entry points
replace registrations `AddBsftSaga` already made, which is why they are called after it.

`AddBsftSagaEFCore<TContext>()` replaces `ISagaStore` with the EF-backed store and registers the
application's `TContext` as the scoped `ISagaDbContext`. Saga *state* becomes durable; timers stay in
memory and do not survive a restart.

`AddBsftSagaEFCoreWithTimeouts<TContext>()` additionally replaces `ISagaTimeoutStore` and
`ISagaPersistenceWindow`, registers that same window instance as `ISagaDbContextAccessor`, and
registers `TContext` as `ISagaTimeoutDbContext` as well. This is the only mode in which a step's
state advance and its timer writes commit in one `SaveChangesAsync`, because only here do both stores
target the same context.

The package has no options type. The one thing a host may have to configure is the SQL predicate that
selects pending rows for the filtered indexes: `new SagaTimeoutEntityConfiguration(pendingFilter)`.
The parameterless default matches SQL Server and SQLite; PostgreSQL needs `"DispatchedAt" IS NULL`.
Passing `null` drops the filter and with it the uniqueness of a pending timeout name.

Seams: `IEFCoreSagaStore` and `IEFCoreSagaTimeoutStore` name the EF-backed implementations so a host
can resolve or replace exactly the relational store, and `IEFCoreSagaTimeoutStore.IsStagingIntoWindow`
reports whether the next write would join an open window. `ISagaDbContextAccessor` exposes the context
of that window.

### Bsft.EventAggregator.Saga.Http

The read surface of the family. A saga that stops moving leaves no log line saying so, and until this
package there was no way to ask which instances are stuck other than reading the table by hand.

`AddBsftSagaHttp()` registers the options and the state read; `MapBsftSagas()` maps two routes under
`/api/sagas` by default. `GET {prefix}/instances` answers a page of progress metadata — which saga,
which correlation key, which state type, how far it got, whether it finished and when it last moved —
filtered by any of `sagaType`, `sagaTypePrefix`, `correlationKey`, `stateType`, `isComplete`, the
created and updated windows and `minStepIndex`, ordered by the last transition by default. The stall
query is `isComplete=false&updatedBefore=<an hour ago>`.

`GET {prefix}/instances/{id}` answers one instance with its state JSON and every timeout recorded for
it. It follows the read grading of [package-conventions.md](package-conventions.md) section 5a: it is
mapped only when `ReadContentPolicy` is configured, and a hit records a `saga.instance.read-content`
audit activity naming the saga type and the correlation key. A miss records nothing, so nobody fills
the audit log by walking identifiers. `MapBsftSagas` refuses a configured `ReadContentPolicy` with no
`IAuditRecorder` behind it.

Seams: `ISagaInstanceReader` and `ISagaTimeoutReader`, both declared in `Bsft.EventAggregator.Saga`.
They are two interfaces because the first needs only `ISagaDbContext` while the second needs
`ISagaTimeoutDbContext`. `AddBsftSagaEFCore` registers the instance reader,
`AddBsftSagaEFCoreWithTimeouts` registers both; the in-memory store registers neither, and
`MapBsftSagas` says so when it refuses. A host on the in-memory timeout store reads an instance and
gets an empty timeout list rather than a refusal.

Neither saga table carries a tenant column, so the database is the scope of this surface. Where a
narrower one is needed, the policy on `ReadPolicy` is the place to say so.

### Bsft.EventAggregator.Saga.Correlation

The bridge between `Bsft.Correlation` and the saga engine. The engine declares
`ISagaCorrelationAccessor` and `ISagaCorrelationInitializer` and implements neither, because
referencing `Bsft.Correlation` would drag the ASP.NET Core request pipeline into a background
component.

`AddBsftSagaCorrelation()` registers the scoped `ISagaCorrelationAccessor` that stamps the current id
onto a scheduled timeout and reads it back when the timer fires, the scoped
`ISagaCorrelationInitializer` that adopts that id into the step scope's own `CorrelationContext`, and
— with `TryAdd`, so the engine's own registration wins — the singleton `ISagaCorrelationScope`.

`UseBsftSagaCorrelation()` adds the middleware that closes the remaining gap: a step triggered
directly by an event the request publishes. Place it after `UseBsftCorrelation()`. The timeout path
needs no pipeline step, and without either call nothing fails — a step simply keeps the fresh id of
its own scope.

The package has no options type and declares no seam of its own.

## Waiting for a person

A saga waits for an event a system will emit. It has no assignee, no inbox and no way to express
"the approver left the company, move the three approvals waiting on them" — saga state describes the
progress of the orchestration and is not a business entity, so putting an assignee in it breaks
rule 2 of [architecture-rules.md](architecture-rules.md) and rendering an inbox from it breaks
rule 3.

Human decisions live in `Bsft.Workflow` instead, and the two compose. A saga step
publishes `WorkflowStartRequestedEvent`; the workflow bridge raises a work item; when the item ends the
package publishes `ApprovalDecided`, and the saga simply `WaitFor<ApprovalDecided>(e =>
e.CorrelationKey)`. The step calls nothing and knows nothing about the workflow package, which is
what rule 1 requires of it, and the saga assembly references only `Bsft.Workflow.Abstractions`.

## See also

- `Bsft.Workflow` — how to wait for a person rather than a system.
- [saga-timeouts.md](saga-timeouts.md) — durable timers, catch-up policy, the drain seam.
- [event-aggregator.md](event-aggregator.md) — the bus the engine subscribes to.
- [architecture-rules.md](architecture-rules.md) — why a timer publishes an event and carries no logic.
