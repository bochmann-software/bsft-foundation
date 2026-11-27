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

A service supplies four things:

```csharp
new PollLoop(pass, reporter, interval, timeProvider).RunAsync(stoppingToken);
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

`interval` is the wait after a pass, and `timeProvider` is the clock that wait runs
on. A service passes the `TimeProvider` it was constructed with, which its AddXxx
registration resolves from the container (`TryAddSingleton(TimeProvider.System)`), so a
test that registers a fake clock moves every wait of the service by advancing it.

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

`IAmbientTransaction` (from 2.2.1-preview.1) is an `ITransactionalUnitOfWork` that
spans every store enlisted in the scope, so a unit started while one is open
joins it instead of opening its own:

```csharp
public interface IAmbientTransaction : ITransactionalUnitOfWork
{
    bool IsActive { get; }
    bool IsTransactionConflict(Exception exception);
}
```

`IsActive` is what the mediator's retry behavior asks before retrying a request:
only the outermost unit may be retried, as a whole. It is true only in the async
flow that opened the unit, so a parallel flow of the same scope does not mistake
itself for a nested caller. `IsTransactionConflict` passes on the store
provider's verdict that a failure was a conflict with a concurrent transaction,
and keeps answering after the scope was disposed, which is when a retry asks.
The shipped implementation is the shared transaction of `Bsft.Data`,
[one transaction across several contexts](data.md#a-transaction-across-several-contexts);
the mediator's transaction and retry behaviors read the seam from here, so neither
references a persistence package, and it lives here for the same reason as the
two contracts above.

`AmbientTransactionMisuseException`, beside it and derived from
`InvalidOperationException`, is what an implementation throws when the
transaction is used in a way that fails the same way on every attempt: a failure
inside the unit was caught and the work completed anyway, a second outermost unit
was started beside a running one, or a store retries on its own. The retry
behavior reads the type and does not retry it, unless the swallowed failure it
carries as inner exception is one the store classifies as a conflict.

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

## Background work per tenant

`Bsft.Core.Tenancy` (since 2.2.1-preview.1) is the seam that lets the framework's background services work
for each tenant of a host whose tenants live in databases of their own:

| Type | Role |
| --- | --- |
| `IBackgroundTenantSource` | The host lists the tenants a pass runs for. |
| `BackgroundTenant` | The ambient tenant of the pass (`Current`; `new BackgroundTenant(tenantId)` enters a tenant until it is disposed); `Bsft.Data`'s `TenantContext` takes it over, once when the context is created, while its scope has resolved no tenant of its own; a scope created outside a pass therefore stays without a tenant inside one, and never changes its tenant halfway. |
| `BackgroundTenantPasses` | `PerTenant` and `CountPerTenantAsync` run a pass once per tenant, in turn, with `BackgroundTenant` set; one tenant's failure does not keep the others from running and is rethrown afterwards. |
| `ITenantScopeFactory` / `ITenantScope` | A service scope with a tenant entered; `Bsft.Data`'s `AddBsftDataMultiTenancy` registers one. `CreateScopeAsync(tenantId, cancellationToken)` (since 2.2.1-preview.6) is for a host that can only bind a tenant's scope with I/O; its default calls `CreateScope`, so an implementation without it keeps working. |

With an `IBackgroundTenantSource` registered, the outbox dispatcher and its audit counterpart, the inbox,
audit-outbox and idempotency sweepers, the job dispatcher and the leased work queues run every pass once per
tenant; `JobExecutor` and `WorkDispatcher` open a job's scope through `ITenantScopeFactory.CreateScopeAsync` when a
factory is registered, with the run's or the item's token.
Without a source nothing changes.

Every one of these services that opens a scope inside a tenant's pass opens it through
`ITenantScopeFactory.CreateScopeAsync` when the host registers a factory (since 2.2.1-preview.7; before, the audit
outbox dispatcher, the audit-outbox, inbox and idempotency sweepers and the feature flag rule check at start opened a
plain scope with `IServiceScopeFactory.CreateScope`), and falls back to a plain scope inside the `BackgroundTenant`
when none is registered. A host whose `CreateScope` refuses because binding needs I/O is therefore never called
synchronously by a background pass:

| Service | Scopes of a tenant's pass |
| --- | --- |
| `AuditOutboxDispatcher` (`Bsft.Auditing.EFCore`) | The batch read, each row's dispatch, a failed attempt's record |
| `AuditOutboxSweeper` (`Bsft.Auditing.EFCore`) | The sweep |
| `InboxSweeper` (`Bsft.Messaging.EFCore`) | The sweep |
| `IdempotencyRecordSweeper` (`Bsft.Idempotency.EFCore`) | The sweep |
| `FeatureFlagStartupValidator` (`Bsft.FeatureFlags`) | A tenant's rule check, where the store keeps flags per tenant |
| `WorkDispatcher` (`Bsft.Jobs`), `JobExecutor` (`Bsft.Scheduling`) | An item's or a job's scope (since 2.2.1-preview.6) |
| `FeatureFlagSnapshotProvider` (`Bsft.FeatureFlags`) | A tenant's snapshot load (since 2.2.1-preview.6) |
| `EFCoreOutboxStore` (`Bsft.Outbox.EFCore`), the integration-event outbox's store | Each lease, mark, abandon and dead-letter of the outbox dispatcher's drain |
| `EFCoreJobStore` (`Bsft.Scheduling.EFCore`) | Each operation of the job dispatcher and executor (list, claim, settle, retry, enqueue, cancel, find) |
| `EFCoreWorkQueue` (`Bsft.Jobs.EFCore`) | Each operation of the leased worker (claim, renew, settle, enqueue, cancel, find, counts) |

The public services among them take the factory through an additional constructor
(`..., IBackgroundTenantSource tenants, ITenantScopeFactory tenantScopes)`), which the container picks when both are
registered; `EFCoreOutboxStore` takes it through `(scopeFactory, rows, tenantScopes)`, which `AddBsftMessagingEFCore`
uses when a factory is registered, and the internal job store and work queue take it as an optional parameter. The
stores ask the factory only while `BackgroundTenant.Current` is set, so a request or a caller outside a pass keeps its
plain scope. A failed binding counts as that tenant's failure: the pass goes on with the next tenant and rethrows it
afterwards, as for any other failure. Every scope of these passes, the tenant's and the plain one, is disposed
asynchronously, so a scoped service that is only `IAsyncDisposable` (a leased data source) is released without the
container's synchronous-dispose fault.

What the seam does not cover yet, so that a slice building on it knows: `RecurringJobSeeder`,
`StoredScheduleSeeder` and `IntegrationEventConsumerHost` run without a tenant, so in silo mode recurring jobs are
not seeded into the tenants' databases and consumed integration events are handled outside any tenant. A host with
a source also no longer runs a pass without a tenant, so a platform database beside the tenants' is not drained
by these services. And each in-process outbox wake-up drains every tenant the source lists, one database after
another, because the signal names no tenant: with many silos every burst of commits costs a pass over all of
them.

Inside a tenant's pass a few scopes are still opened plainly, without the factory:

- The loopback bus (`InMemoryMessageBus`, `Bsft.Messaging`) and the inbox-guarded delivery (`InboxGuardedDelivery`)
  open their handler scope with `CreateAsyncScope`. When the outbox drain delivers in-process, the store's lease is
  bound but the handlers and the `IInboxStore` are not, so a host whose scopes bind only through the factory fails
  each event until the row is dead-lettered.
- `JobScheduleResolver` (`Bsft.Scheduling`) opens a synchronous `CreateScope()` in a tenant's dispatch pass to read
  `IRecurringJob.Schedule`: the job is constructed unbound, a scoped service that is only `IAsyncDisposable` faults
  the dispose, and the schedule is cached across tenants.
- `AuditOutboxDispatcher` records a failed attempt in a second tenant scope while the row's scope is still open, so a
  tenant holds two bindings at once for that moment. A host that allows only one binding per tenant at a time (a
  semaphore of one) would wait there for ever.

## Time-ordered ids

`ITimeOrderedIdGenerator` (`Bsft.Core.Persistence`) hands out 64-bit ids a client makes before it writes the row,
for a table without an identity column. `TimeOrderedIdGenerator` packs 41 bits of milliseconds since 2025-01-01
(enough into 2094), 12 bits of node id and 10 bits of sequence, with the sign bit clear. Its one counter moves by
compare-and-swap to the larger of "one more than last time" and "now with sequence zero", so values rise strictly
under parallel calls, a clock that steps back does not move them back, and more than 1024 values in one millisecond
carry into the next one.

`TimeOrderedIdGenerator.ForNode(n, timeProvider)` returns one instance per node id, clock and process, so two
containers in one process that share the registered `TimeProvider` share a counter; `ForRandomNode(timeProvider)` is
the instance for a host that configured no node id, with one node id per clock counted up from a start drawn at random
per process, so up to 4096 clocks per process differ and after that a node id repeats. The clock is the one the host
registered, so a test that registers a fake clock drives the time field. Ids of processes with different node ids never
collide.
`Decompose(id)` splits an id back into instant, node id and sequence. The tenant-keyed framework tables take their ids
from it ([data.md](data.md#tenant-keys)).

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

## Addresses

`EmailAddress`, `InternetDomain` and `EmailAddressCollisions` in the `Bsft.Core` root are the one place the framework decides what an e-mail address and a domain name are. Every path
that stores, compares, looks up or delivers one goes through it: self-registration, the directory's login lookup, e-mail
verification, password reset, invitations, the organization domain claims (`IVerifiedDomainLookup`, home-realm discovery) and
the domains upstream connections report. No package canonicalizes on its own.

**Where it lives.** `Bsft.Core` is the lowest package every user may reference: `Bsft.Auth.Credentials.Abstractions` (an
`.Abstractions` package may take `Bsft.Core` only, section 1 of the package conventions), `Bsft.Auth.Oidc.Organization`,
`Bsft.Organization.Domains` and `Bsft.Auth.Oidc.Connections` all reach it, and it adds no package reference: the UTS 46 tables
are embedded text (about 130 KB) read on the first non-ASCII name, and Punycode is implemented in the package. The UTS 39
confusables data (about 760 KB) is only needed where a domain grants rights, so it lives in `Bsft.Organization.Domains`
(`DomainSkeleton`), which the connections package already references.

| Type | Does |
| --- | --- |
| `InternetDomain.TryCanonicalizeDomain` | UTS 46 non-transitional processing to the lower-case A-label form; `ToUnicode` for display and skeletons. |
| `EmailAddress.TryCanonicalize` / `TryFold` | The canonical address (ASCII local part exactly as entered, canonical domain); the case-insensitive key two spellings share. |
| `EmailAddressCollisions.FindCollisions` | The report a host reads before it canonicalizes stored addresses: collisions, addresses that are not canonical, rewrites. |

**Domain rule.** UTS 46 with `Transitional_Processing=false`, `CheckHyphens`, `CheckBidi`, `CheckJoiners`, `UseSTD3ASCIIRules` and
`VerifyDnsLength`: the mapping table (case folding, full-width and compatibility characters, `U+3002`/`U+FF0E`/`U+FF61` as dots,
ignored code points such as the soft hyphen), NFC, label validity (no leading or trailing hyphen, no `--` in positions 3 and 4,
no leading mark, only valid or deviation code points, ASCII limited to letters, digits and hyphen), the joiner rules (ZWNJ/ZWJ
only after a virama, ZWNJ also between joining letters), the RFC 5893 bidi rule for names with right-to-left text, A-labels of at
most 63 octets and a name of at most 253. Beyond UTS 46: at least two labels, no trailing dot, a last label that is not all
digits. A `xn--` label has to decode, be valid and encode back to itself, so one name has one spelling. Non-transitional means
`faß.de` stays `xn--fa-hia.de` (not `fass.de`) and the final sigma and ZWJ/ZWNJ are kept where valid.

**Deterministic on every platform.** `System.Globalization.IdnMapping` asks ICU on Linux and the operating system on Windows,
which know different Unicode versions and differ at the edges, so one name could canonicalize differently on two hosts that share
a database. The mapping, bidi, joining, virama and mark tables are generated from Unicode 18.0.0 (`tools/unicode-data/generate.py`
pins the version and fails on any other) and embedded. Unicode normalization forms C and D come from
the package too (`UnicodeNormalization`, UAX 15 over generated combining-class, decomposition and composition-exclusion tables), so
nothing is asked of ICU, NLS or the globalization mode: the invariant mode, an ICU older than Unicode 18 and Windows give the same A-labels
and skeletons, and `InternetDomainTests` runs under `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` as well. The test vectors in `InternetDomainTests` are literal values, not `IdnMapping` answers, and run on every platform CI runs.

**Local part.** Strict ASCII dot-atom, at most 64 characters, as before; an internationalized local part (RFC 6531) is refused because
delivering to it needs SMTPUTF8 on every hop.

**What is stored.** The address as entered with the domain as its canonical A-label (`Ada@Müller.DE` is stored as
`Ada@xn--mller-kva.de`); the local part keeps its case. Uniqueness and lookup are case-insensitive over the **fold**
(`ADA@XN--MLLER-KVA.DE`, upper-cased invariantly), which `DirectoryEmail.Fold` returns for `DirectoryUser.NormalizedEmail`. Delivery uses the
stored value.

**Existing data.** Rows stored under an older rule can collide once canonicalized (an upper-cased domain next to a lower-cased one, an
internationalized domain written once as Unicode and once as Punycode). `EmailAddressCollisions.FindCollisions(storedAddresses)` returns the groups
that become one address (`Collisions`, nothing is merged), the addresses that are not canonical at all (`Rejected`) and the ones that need
rewriting (`Rewrites`, `Stored` to `Canonical`); a host runs it in a migration's dry run or an import check and decides per group.

## See also

- [mediator.md](mediator.md) — where `IValidator<T>` and `Unit` are consumed.
- [resilience.md](resilience.md) — how the exception types drive the retry decision.
- [hosting.md](hosting.md) — where `IStartupValidator` implementations are run.
- `Bsft.Organization` — the closure table the calculator maintains.
- [messaging.md](messaging.md) — the outbox dispatcher and inbox pruner that run
  on `PollLoop`, and the retry schedule that honours `IRetryAfter`.
