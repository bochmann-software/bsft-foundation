# Bsft.Core

The primitives every other module depends on: a unit type, a domain exception
hierarchy, the validation seams, the poll loop the framework's background
services run and the closure calculator its tree tables are maintained with.
`Bsft.Core` has no infrastructure dependency and registers no services of its
own.

## The exception hierarchy

Every framework exception derives from `BsftException`, which itself derives
from `Exception`. The point of the hierarchy is that the meaning of a failure
travels with its type, so a pipeline behavior or an exception middleware can map
it to a transport without a string comparison.

| Type | Error code | Meaning |
| --- | --- | --- |
| `BsftException` | — | Base type of all framework exceptions. |
| `ValidationException` | `VALIDATION_FAILED` | Input did not satisfy a validator. |
| `BusinessRuleException` | `BUSINESS_RULE_VIOLATED` | A domain invariant was violated. |
| `NotFoundException` | `NOT_FOUND` | The addressed entity does not exist. |
| `ForbiddenException` | `FORBIDDEN` | The caller is known but not permitted. |
| `ConflictException` | `CONFLICT` | The write lost against a concurrent one. |

All of them live in `Bsft.Core.Exceptions`. `BsftException` carries
`string? ErrorCode`, and each subtype supplies the code above by default, so a
transport can map an error without matching on the message text.

The constructors are not uniform, and the shape carries meaning:

```csharp
public BsftException(string message, string? errorCode = null)
public BusinessRuleException(string message, string? errorCode = null)
public ConflictException(string message)
public ForbiddenException(string message)
public NotFoundException(string entityType, object entityId)
public ValidationException(IReadOnlyDictionary<string, string[]> errors)
public ValidationException(string message, IReadOnlyDictionary<string, string[]> errors)
```

`NotFoundException` takes the **entity type and id**, not a message — it exposes
them again as `EntityType` and `EntityId`. `ValidationException` carries the
per-field `Errors` dictionary.

These five are treated as *deterministic* elsewhere in the framework: the retry
predicate in `Bsft.Resilience`
(`BsftResilienceMediatorOptions.IsTransientFailure`) never retries them, because
a second attempt would fail the same way. Only unclassified exceptions are
treated as transient. Throwing `BsftException` subtypes from a handler is
therefore not merely cosmetic — it is what keeps the retry pipeline from
hammering a request that can never succeed.

## Unit

`Bsft.Core.Unit` is a `readonly struct` implementing `IEquatable<Unit>` and
`IComparable<Unit>`. It stands in for "no result" so that a command without a
return value still flows through the same generic machinery as one with a
result: `ICommand` in `Bsft.Mediator.Abstractions` is declared as
`ICommand : ICommand<Unit>`. Application code rarely names it directly; when it
has to, `Unit.Value` is the instance and `Unit.Task` a cached completed
`Task<Unit>`. The screaming-case `Unit.VALUE` and `Unit.TASK` the type first
shipped with still work and are marked obsolete.

## Validation

Two seams, in `Bsft.Core.Validation`:

```csharp
public interface IValidator<in T>
{
    Task<ValidationResult> ValidateAsync(T instance, CancellationToken ct = default);
}

public interface IStartupValidator
{
    Task<StartupValidationResult> ValidateAsync(CancellationToken ct = default);
}
```

`IValidator<T>` is what the mediator's validation behavior resolves per request
type. `ValidationResult` carries the outcome; `StartupValidationResult` carries
the outcome of a startup check.

`IStartupValidator` is the "fail at boot, not at 3 a.m." seam. Modules register
implementations to assert that their configuration is coherent before the
application serves traffic — `Bsft.Mediator` registers
`MediatorStartupValidator`, which verifies that every command and query found by
the assembly scan actually has a handler. `Bsft.Hosting` runs the registered
validators via `AddBsftStartupValidation()`.

A startup check has two ways to speak. `StartupValidationResult.Failure(...)`
stops the application; `StartupValidationResult.Success(message, warnings)` keeps
it running and hands the findings to the runner, which logs each of them at
warning level. That second channel is for a gap the host may legitimately have
chosen — the mediator's report of a declared permission no registered behavior
enforces is one — and it exists so no validator needs a logger of its own and
every finding still passes through the same place.

## The poll loop

`Bsft.Core.Polling.PollLoop` is the loop every polling background service in the
framework runs — the outbox dispatcher, the audit outbox dispatcher, the inbox
pruner and the job dispatcher all sit on it. It is plain BCL code with no dependency on hosting
or logging, which is why it lives here rather than in a hosting package.

The loop runs a pass, observes how that pass ended instead of catching it, and
waits before the next one. What it guarantees is that **only the stopping token
ends the loop**: a pass that faulted is reported and the loop carries on, and so
is a pass that ended cancelled while nobody asked the service to stop — a store
call or a database command that timed out. Reading that second case as a
shutdown is the mistake this type exists to prevent: the host keeps running
while the service silently stops doing its work.

A service supplies three things:

```csharp
new PollLoop(pass, reporter, interval).RunAsync(stoppingToken);
```

`pass` is `Func<CancellationToken, Task<PollPassOutcome>>` — the work of one
pass. Its return value is the one degree of freedom the loop leaves it:
`PollPassOutcome.WaitForInterval` waits, `PollPassOutcome.PollAgain` starts the
next pass at once. A dispatcher that drained a full batch knows more is pending
and returns `PollAgain`; a sweep on a fixed cadence always waits.

`reporter` is an `IPollReporter`, usually the service itself, and gets told that
a pass failed, that a pass was cancelled without a stop request, or that the loop
is unwinding because the service is stopping. The loop holds no logger: each
service phrases these three events in its own words and at its own level.

A failed pass is reported as the exception it failed with, so an alert rule keyed
on the exception type sees the type the pass actually threw. Only a pass whose
task faulted with more than one exception is reported as a flattened
`AggregateException`, because that is what carries them all — note that awaiting
a `Task.WhenAll` inside the pass does not produce this shape, since the await
rethrows only the first failure; the task itself has to carry several.

A pass does not have to be an async method. One that throws out of the call
itself, before there is any task, is reported as a failed pass like any other,
and so is a delegate that returns no task at all. Nothing a pass does ends the
loop.

## Asking to be tried later

`Bsft.Core.Retry.IRetryAfter` is how code that has been told to wait says so
instead of sleeping where it stands:

```csharp
public interface IRetryAfter
{
    TimeSpan? RetryAfter { get; }
}
```

An exception implements it, and whatever owns the retry reads it off the failure.
The component that learns of a wait — an HTTP client, a notification channel — is
usually the innermost one, and what it would block by sleeping is a pipeline
shared with every other piece of work; throwing instead makes the wait a property
of the failed item. `null` is a real answer: a `429` without a `Retry-After`
header says "not now" and nothing about when.

`RetryAfterException` is the ready-made implementation. Code that already has a
suitable exception type implements the interface on that type rather than
replacing it — which is what `Bsft.Notifications.WebPush`'s
`WebPushThrottledException` does. It derives from `Exception` and not from
`BsftException`, and so lives under `Retry/` rather than with the hierarchy
above: it is raised inside a background pipeline and has no client, so an error
code would advertise a response it never produces.

Reading the request and turning it into a wait is the retrying component's job.
`Bsft.Messaging` does it for integration events — see
[messaging.md](messaging.md#a-handler-can-ask-to-be-tried-later) — where the ask
is a floor on an exponential schedule and is clamped to a configured ceiling.

## The unit of work

`Bsft.Core.Persistence` holds the two contracts the framework states its
persistence boundaries in. They are declarations only — `Bsft.Core` still has no
package reference of any kind, and neither contract names a `DbContext`:

```csharp
public interface IUnitOfWork
{
    Task CommitAsync(CancellationToken ct = default);
}

public interface ITransactionalUnitOfWork
{
    Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work, CancellationToken ct = default);
}
```

`IUnitOfWork` is for the packages that **stage** rather than write. Publishing a
notification adds the dispatch rows and the outbox events to the caller's change
tracker, and the shipped audit sink adds its row to the caller's context; that is
what ties an announcement to the change it announces, and it leaves one
obligation behind. A request commits at the end of the request. A background
sweep, which announces nothing it wrote itself, has to commit for itself or its
rows are discarded with the scope.

`ITransactionalUnitOfWork` is for the writes that have to become durable
*together*: applying a payment provider's webhook claims the delivery and then
moves the subscription, and a crash between the two loses the change for good
while the redelivery is refused as a duplicate. It is deliberately a scope the
caller hands over rather than a transaction handle it opens, because a handle
cannot be retried — a host that configured `EnableRetryOnFailure` re-runs the
whole unit from the start, and only an implementation that owns the delegate can
do that. It is also what lets an in-memory implementation stay honest: a
process-local dictionary has nothing to roll back to, so it runs the work
directly, which `Commit` and `Rollback` on a handle could only pretend to do.

Neither derives from the other. They are two capabilities, the packages that need
them are not the same packages, and an implementation that can do both implements
both — which is what the shipped
[`DbContextUnitOfWork<TContext>`](data.md#the-unit-of-work) does.

They live here rather than in `Bsft.Data.Contracts` because of who has to
reference them. The private seams they replace sit in `.Abstractions` packages,
and section 1 of [package-conventions.md](package-conventions.md) lets such a
package take `Bsft.Core` precisely because `Bsft.Core` widens its closure by
nothing. `Bsft.Data.Contracts` is the query surface a browser or console client
speaks; a persistence seam is not part of that vocabulary, and putting it there
would send every consumer of the DTOs a contract about committing.

## The closure calculator

`Bsft.Core.Trees` holds the one function the framework's closure tables are
maintained with. A tree kept as `(Id, ParentId)` rows answers "who is below this
node" only by walking; a closure table answers it with one indexed read, one row
per ancestor and descendant pair. `IClosureCalculator` computes those rows from
the parent relationships, which stay the single source of truth:

```csharp
public interface IClosureCalculator
{
    IReadOnlyCollection<TRow> Compute<TRow>(
        IEnumerable<(Guid Id, Guid? ParentId)> nodes,
        Func<Guid, Guid, int, TRow> createRow);   // (ancestorId, descendantId, depth)
}
```

Every node yields a self-row at depth 0 and one row per ancestor its parent chain
reaches, at the number of edges between the two. A parent that is not part of the
supplied set ends the walk there, so a partial set computes the part it can. A
cyclic parent reference throws `InvalidOperationException` naming the node whose
walk ran into it, rather than looping.

The calculator is generic in the row it emits because it does not know the
entity: `Bsft.Organization.EFCore` builds `OrganizationUnitClosure` through the factory,
and a second package keeping a closure table builds its own. `ClosureCalculator`
is a pure function over the BCL, which is the only reason it may live here — it
widens the closure of the package every package depends on by nothing (see
ADR-0012,
decision 3). `Bsft.Core` registers no services, so each package that uses it
registers it with `TryAddSingleton<IClosureCalculator, ClosureCalculator>()`, and
the first registration wins.

## Observers

`Bsft.Core.Observers` is the one place a framework or application type keeps an observer from breaking
the operation it observes. It is plain BCL code, like the poll loop, so it carries no logging
dependency: the guard hands each contained failure to an `IObserverFailureReporter`.

```csharp
public sealed class RunObserverComposite(ObserverComposite<IRunObserver> composite) : IRunObserver
{
    public void Started(RunInfo run) => composite.Notify(observer => observer.Started(run));

    public Task FinishedAsync(RunInfo run, CancellationToken ct) =>
        composite.NotifyAsync((observer, token) => observer.FinishedAsync(run, token), ct);
}
```

`ObserverComposite<TObserver>` tells every observer in registration order, one after the other, and
takes a snapshot of the observers when it is built. An observer that throws is contained by the
`IObserverGuard` it was given, so the observers after it still hear of the event and the observed
operation carries on. `ObserverGuard` raises `Failures` before it reports, and a reporter that throws
is contained as well: the failure was counted, and the reporter's own failure is counted in
`ReporterFailures`, so a host can tell failures that were counted but never reported.

The asynchronous forms contain a fault of the observer, whether it is thrown synchronously or carried by
the returned task, with one exception: when the token passed in is cancelled, the observed operation is
being cancelled and that is not an observer failing, so the `OperationCanceledException` propagates and
nobody is told after it. An observer that cancels on its own, with a token that is not the operation's,
is an ordinary failure. `NotifyAsync` checks the token before it tells each observer, so an observer
that ignores its token cannot let the next one be told after the operation was cancelled.

A contract that wants to be called as one observer implements itself by delegation, as above. Do not
register that class as one of the observers it injects, or it would be told about its own events.

`Bsft.Hosting` registers the parts: `services.AddBsftObservers()` adds the guard, a reporter that logs
each failure at warning level (the observer type and the exception type; the exception itself only with
`ObserverFailureLoggingOptions.IncludeExceptionDetails`, because its message may hold data of the observed
operation), and the open generic composite;
`services.AddBsftObserver<IRunObserver, AuditRunObserver>()` adds one observer. Everything is a
`TryAdd`, so a host that registered its own `IObserverFailureReporter` or `IObserverGuard` first keeps it.
Observers are singletons, because the composite that holds them is one: `AddBsftObserver` throws when the
contract is already registered as scoped or transient, instead of leaving the composite to capture a
scoped service.

## Keyed gate and slot pool

`Bsft.Core.Concurrency` has two small async primitives with a documented order, for work that must not
overlap on one resource and must not run more than so many at once.

`KeyedGate<TKey>` (`IKeyedGate<TKey>`) lets callers asking for the same key run one at a time and does
not make callers of different keys wait for each other. `EnterAsync(key, ct)` returns a handle; disposing
it lets the next waiter for that key in.

```csharp
var directories = new KeyedGate<string>(
    OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

using (await directories.EnterAsync(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), ct))
{
    // one build or restore of this directory at a time
}
```

- **Keys.** The comparer decides which keys are the same (default: the default comparer of `TKey`). The
  key reaches it as it is, so a caller that compares paths normalises them first, as above.
- **No leaked entries.** An entry exists only while some caller holds or waits for the key. It is removed
  when the last user leaves, released or cancelled, so a gate that served any number of distinct keys is
  empty again afterwards; `TrackedKeys` reads zero when idle.
- **Order.** Waiters of one key are admitted strictly in the order they asked.

`SlotPool` (`ISlotPool`) is a fixed number of slots. `EnterAsync(ct)` waits for a free one;
`Capacity` and `Available` report the numbers.

- **Fairness.** Callers are admitted strictly in the order they called, and a caller arriving while
  others wait queues behind them even when a slot has just been freed. The pool keeps its own queue
  rather than using `SemaphoreSlim`, which documents no order, and the gate uses one pool of a single slot per key.
- **Cancellation.** A token that is already cancelled yields a cancelled task before anything is taken, even when a slot is
  free. A caller cancelled while it waits leaves the queue and holds nothing. A token cancelled after the
  slot was granted does not revoke the grant: the call returns the slot and the caller disposes it.
- **Release.** Disposing a handle releases once. A second dispose does nothing, so a handle disposed on two
  code paths cannot free what another caller now holds.

The two compose. Take the key's gate before a slot of the pool, so a caller queued behind another one of
the same key never holds a slot it cannot use. Nested `using` statements give the gate back when the wait
for the slot is cancelled, because the gate's `using` is already open:

```csharp
using (await directories.EnterAsync(key, ct))
using (await pool.EnterAsync(ct))
{
    // run; the slot is disposed first, then the gate, also when the wait for the slot is cancelled
}
```

Two pools that share one `KeyedGate` never run two callers of the same key at once, whichever pool
admitted them.

## Setup

There is nothing to register. Reference the package and throw the exceptions:

```csharp
using Bsft.Core;

public sealed class GetOrderHandler(IOrderDbContext db) : IQueryHandler<GetOrder, OrderDto>
{
    public async Task<OrderDto> HandleAsync(GetOrder query, CancellationToken ct = default)
    {
        var order = await db.Orders.FindAsync([query.OrderId], ct)
            ?? throw new NotFoundException("Order", query.OrderId);

        return new OrderDto(order.Id, order.Total);
    }
}
```

## See also

- [mediator.md](mediator.md) — where `IValidator<T>` and `Unit` are consumed.
- [resilience.md](resilience.md) — how the exception types drive the retry decision.
- [hosting.md](hosting.md) — where `IStartupValidator` implementations are run.
- `Bsft.Organization` — the closure table the calculator maintains.
- [messaging.md](messaging.md) — the outbox dispatcher and inbox pruner that run
  on `PollLoop`, and the retry schedule that honours `IRetryAfter`.
