# Saga Timeouts

A saga that models a real business process usually has to continue on time, not only on events: a
quote is sent, and two days later somebody has to follow up. `Bsft.EventAggregator.Saga` therefore
supports durable, named timeouts that survive restarts and deployments.

A timeout **publishes an event** when it comes due. The saga step reacts to that event like any
other. The timer itself carries no business logic — Rule 1 in
[architecture-rules.md](architecture-rules.md) stays intact.

## Scheduling

```csharp
factory.WithState<QuoteState>()
    .StartWith<QuoteSent>(e => e.QuoteId)
    .Then((ctx, e) =>
    {
        ctx.ScheduleTimeout("followup", TimeSpan.FromDays(2), new QuoteFollowUpDue(e.QuoteId));
        ctx.ScheduleTimeout("callback", TimeSpan.FromDays(3), new QuoteCallbackDue(e.QuoteId));
        return Task.CompletedTask;
    })
    .WaitFor<QuoteAccepted>(e => e.QuoteId)
    .Then((ctx, _) => { ctx.State.Accepted = true; return Task.CompletedTask; })
    .Complete();
```

Several timeouts can be pending at once. Each is addressed by a name that is unique among the pending
timers of the saga instance, so scheduling twice under the same name replaces the pending timer rather than adding a
second one. Once a timer has fired, its name is free again.

With the EF Core store the rule is backed by a unique index over the pending rows, so two application
instances racing to schedule the same name collide instead of both inserting. The index filter uses
SQL Server and SQLite identifier quoting; another relational provider must pass its own filter to
`SagaTimeoutEntityConfiguration`. The in-memory store enforces the rule by convention only, which is
adequate for the tests and demos it is meant for. There is also an overload taking an absolute `DateTimeOffset` for a timer whose due date
the caller has already computed.

A timeout is only written once the step has finished, so a step body that throws leaves neither state
nor timers behind.

**The state advance and the timer writes commit together.** After the step body returns, the flow
host opens one persistence window and both stores stage into it; a single `SaveChangesAsync` makes
the whole set durable. A timer write that fails therefore takes the state advance down with it, and
the saga is never left standing past reminders that were never written. The event can be retried
against unchanged state. The step body itself runs before the window opens, so business code never
holds a database connection.

With the in-memory store there is nothing to stage — every write is already durable on its own — so
its window is inert and behaviour is unchanged.

A step that schedules a timeout **and** completes the saga — through `MarkComplete()` or by being the
last step of an auto-completing flow — has its timeout discarded rather than written. The completed
saga is gone from the store, so a reminder for it could only fire into the void.

## Cancelling

```csharp
ctx.CancelTimeout("followup");
ctx.CancelAllTimeouts();
```

Explicit cancellation is rarely needed: **when the awaited business event arrives first, the pending
timers of that step are cancelled automatically.** In the example above, a `QuoteAccepted` before
the two days are up removes both reminders.

The race against an already-due timer is safe by construction. If the timer has been claimed and its
event published before the cancellation lands, the saga has already advanced past that step, and the
step guard in `SagaFlowHost` drops the event. Nothing runs twice.

A completed saga has all of its timers removed with it.

## Business calendars

By default a delay is wall-clock time. To make "in two days" mean two working days, register a
calendar and reference it by key:

```csharp
services.AddBsftCalendar(o => o.AddCalendar("berlin", new WeekdayBusinessCalendar()));

ctx.ScheduleTimeout("followup", TimeSpan.FromDays(2), new QuoteFollowUpDue(id), calendarKey: "berlin");
```

The key is stored on the timeout, so the working-day rules can differ per organisational unit rather
than being a single global setting. See [calendar.md](calendar.md).

## Persistence

The in-memory store is the default and is meant for tests and single-process demos — its timers die
with the process. For anything that waits longer than a deployment, use the EF Core store:

```csharp
services.AddBsftSaga();
services.AddBsftSagaEFCoreWithTimeouts<AppDbContext>();
```

Note the method name. `AddBsftSagaEFCore<TContext>()` persists saga *state* only and leaves timers in
memory; `AddBsftSagaEFCoreWithTimeouts<TContext>()` persists both, and is also the mode in which the
two are committed in one transaction, since only there do both stores target the same context. The distinction exists so that durable timeouts stay
opt-in: the application's DbContext then implements `ISagaTimeoutDbContext` (which extends
`ISagaDbContext` with `DbSet<SagaTimeoutEntity> SagaTimeouts`) and applies
`SagaTimeoutEntityConfiguration` in `OnModelCreating`. As everywhere in Bsft, the framework ships the
entity configuration and the application owns the schema.

### Retention

Dispatched timers are stamped, not deleted. For a saga population in the hundreds of thousands the
table grows without bound, so schedule a purge of rows whose `DispatchedAt` is older than your
retention window. The framework does not do this for you — how long a fired reminder stays auditable
is a business decision, not a framework default.

`DispatchedAt` marks every way a timer left the pending set: dispatched, skipped by the catch-up
policy, or retired to the dead-letter state. A purge over it therefore sweeps retired rows as well,
so exclude `DeadLetteredAt IS NOT NULL` from it if you want the broken timers to outlive the fired
ones — they are the ones somebody still has to look at.

## Dispatching, and what "exactly once" really means

`SagaTimeoutDispatcher` is a hosted service that polls for due timers, confirms each one is still
pending, publishes its event and then claims it. A timer cancelled between the poll and the dispatch
is never published. Two application instances cannot both claim the same timer — the claim matches on
the concurrency token the poll read, so the second one affects no rows.

**Delivery is at-least-once, not exactly-once.** Both instances may publish before either marks. The
saga is safe (the step guard drops the second event), but any side effect a timeout event triggers
must be idempotent. Sending the follow-up e-mail twice is a bug in the handler, not in the
dispatcher.

Options:

| Option | Default | Purpose |
| --- | --- | --- |
| `PollInterval` | 5 s | Pause after a pass that found nothing. |
| `BatchSize` | 100 | Due timers read per pass. |
| `CatchUpPolicy` | `FireAll` | What happens to timers that came due while the app was down. |
| `MaxCatchUpAge` | 1 day | Threshold for `SkipBeyondMaxAge`. |
| `MaxDispatchAttempts` | 10 | Failures a timer may collect before it is retired. |

```csharp
services.AddBsftSaga(o =>
{
    o.Timeouts.CatchUpPolicy = SagaTimeoutCatchUpPolicy.SkipBeyondMaxAge;
    o.Timeouts.MaxCatchUpAge = TimeSpan.FromHours(6);
});
services.AddBsftSagaEFCoreWithTimeouts<AppDbContext>();
```

- `FireAll` replays the whole backlog, oldest first.
- `FireLatestPerSaga` fires only the newest overdue timer per saga instance and discards the rest, so
  a long outage does not hit one saga with a burst of stale reminders.
- `SkipBeyondMaxAge` discards timers overdue by more than `MaxCatchUpAge` and fires the rest.

Discarded timers are counted as missed rather than dropped silently.

### When a timer fails to fire

A dispatch that throws leaves the timer pending and counts the attempt, so a
handler that is merely down for a while costs nothing but a delay — at-least-once
survives it.

Some failures never recover. A release renames the event type a timer carries,
`SagaPersistedTypeResolver` no longer resolves the stored name, and the
publish throws on every single pass. Because the due query is ordered by due date
and capped at `BatchSize`, such a row keeps its place at the head of every poll.
Once `BatchSize` of them have accumulated, a poll returns nothing but poison and
**every saga timer in the application stops firing**.

`MaxDispatchAttempts` is the bound that prevents it. Once a timer has failed that
often it is **retired to the dead-letter state**: the row is kept, stamped with
`DeadLetteredAt` and the failure message in `DeadLetterReason`, and it leaves the
due set for good. Its name becomes free again, so the saga can schedule that
timer anew. Nothing is deleted and nothing is retried without somebody looking at
it — find the retired timers with `WHERE DeadLetteredAt IS NOT NULL` and alert on
`bsft.saga.timeout.dead_lettered`.

`DeadLetterReason` is an exception message and can quote parts of the timeout
payload. Treat the column as at least as sensitive as the payload itself.

A cancellation is a failure like any other unless it came from the host shutting
down. A woken saga step whose `HttpClient` times out raises a
`TaskCanceledException` that travels back through the publish; the dispatcher
counts it against that timer and carries on polling. Only the dispatcher's own
stopping token ends the loop — otherwise one step timeout would stop every saga
timer for as long as the process lives.

## Testing

Everything time-dependent goes through `TimeProvider`, and the dispatcher exposes its pass as an
internal `ProcessOnceAsync`. Tests advance a fake clock and run a single pass; the test suite
contains no wall-clock waits.

## Correlation

A timeout carries the correlation id that was current when it was scheduled and restores it for the
duration of the publish, so a chain of reminders keeps the correlation of the request that started
it. Register the adapter to connect this to `Bsft.Correlation`:

```csharp
services.AddBsftSagaCorrelation();   // Bsft.EventAggregator.Saga.Correlation
```

A saga step runs in a service scope created from the root provider, so the scoped
`CorrelationContext` in that scope is not the request's own and would otherwise mint a fresh id on
every hop. The flow host therefore hands the ambient correlation id to the step's scope through
`ISagaCorrelationInitializer`, which this package implements over `CorrelationContext`. Two things
feed that ambient id:

- a **firing timeout**, whose publisher restores the scheduling request's id around the publish, and
- a **request**, once `UseBsftSagaCorrelation()` is in the pipeline:

```csharp
app.UseBsftCorrelation();
app.UseBsftSagaCorrelation();   // Bsft.EventAggregator.Saga.Correlation
```

With both in place, a step triggered synchronously by an event a request publishes logs, traces and
audits under that request's correlation id, and a step woken by a timer keeps the id of the request
that scheduled it. A step with no ambient correlation — a background publisher, or a host that never
added the middleware — keeps the fresh id of its own scope, so nothing breaks by omitting it.

That is also why nothing fails at startup when `AddBsftSagaCorrelation()` is registered without the
middleware. The two halves are independent on purpose: the timeout path needs only the registration
and works in a worker host that has no request pipeline at all, so a startup check could not tell a
deliberately timeout-only host from a forgotten `UseBsftSagaCorrelation()`, and would either warn on
correct configurations or refuse to start correct ones. The cost of forgetting it is a step logging
under an id of its own rather than a wrong id, which the correlation of the surrounding trace still
makes visible.

## Telemetry

Counters on the existing `Bsft.Saga` meter:

| Instrument | Meaning |
| --- | --- |
| `bsft.saga.timeout.scheduled` | A timer was written. |
| `bsft.saga.timeout.fired` | A due timer published its event. |
| `bsft.saga.timeout.canceled` | A pending timer was removed before firing. |
| `bsft.saga.timeout.missed` | An overdue timer was discarded by the catch-up policy. |
| `bsft.saga.timeout.dead_lettered` | A timer was retired after failing too often. |

All tagged with `saga.type`.

Firing a timer also opens a `saga.timeout` span on the `Bsft.Saga` activity source, tagged
`saga.type` and `saga.timeout.name`. Scheduling and cancelling happen inside a saga step and are
already covered by that step's `saga.step` span; firing runs on the dispatcher's polling loop, so
without this span a trace shows the request that scheduled the reminder and the step it later wakes
with an unexplained gap in between. The span wraps the publish, and therefore also the correlation
scope the publisher restores, so the woken step's work hangs under it with the original correlation
id intact.

## Draining due timers on demand

`ISagaTimeoutDrainer.DrainDueAsync` runs a single dispatch pass instead of waiting for the polling
loop. It is registered on the same instance the host runs as a background service, so a test drains
exactly the timers the application would have fired:

```csharp
var drainer = provider.GetRequiredService<ISagaTimeoutDrainer>();
clock.Advance(TimeSpan.FromDays(30));
await drainer.DrainDueAsync();
```

The returned count is a diagnostic, not an assertion target: publishing a timeout's event usually
advances the saga past the step that scheduled the timer, and advancing cancels that step's timers,
so the row can be gone before the claim lands and the pass reports it as unclaimed even though the
reminder did its work. Assert on the effect on the saga instead. `Bsft.Testing`'s saga harness wraps
this in `DrainTimeoutsAsync` and `AdvanceTimeAsync`, which repeat the pass until nothing is due and
count the timers that stopped being due.
