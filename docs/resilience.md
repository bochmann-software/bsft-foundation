# Bsft.Resilience

Retry, circuit breaking and timeouts for outgoing HTTP calls, opt-in retry for
mediator requests, and retry for a partially consumed stream. Built on Polly,
except the stream half, which is not a Polly shape.

The two request/response halves are deliberately asymmetric: the HTTP pipeline
gets the full strategy stack, the mediator pipeline gets **retry only**. A
circuit breaker around an in-process handler would trip on the application's own
bugs.

## Mediator retry (optional package)

The mediator half ships as `Bsft.Resilience.Mediator` — `RetryBehavior`, the
`[Retryable]` marker, the pipeline provider and its options — and references
`Bsft.Resilience` not at all: the two halves share a name and nothing else, so a
host retrying commands takes neither the HTTP stack nor the correlation handler
along, and a host with resilient HTTP clients takes no mediator dependency. A
`DependencyClosureTests` on each side holds that.

Retry is opt-in per request type, with an attribute:

```csharp
using Bsft.Mediator;

[Retryable]
public sealed record SyncProductPriceQuery(string Sku) : IQuery<decimal>;
```

`RetryableAttribute` has no properties — the bare `[Retryable]` is the whole
API. It lives in `Bsft.Mediator.Abstractions` (namespace `Bsft.Mediator`) since 2.2.1-preview.1, so a
contracts project marks its requests without referencing a resilience package. The former
`Bsft.Resilience.Mediator.RetryableAttribute` stays, hidden, and `RetryBehavior` honours both; a file that
imports both namespaces names the new one or drops `using Bsft.Resilience.Mediator;`. It is applied to the **request**, valid on classes and structs, and is
inherited.

Only mark idempotent requests. Retrying a command that charges a card produces a
second charge; the framework cannot tell the difference, so the decision is
explicitly yours. A request without the attribute passes through the behavior
untouched. A `[Transactional]` handler's own writes are rolled back per failed
attempt, but anything outside the database — an email, an outbound call — is
not, which is why the marker stays opt-in per request rather than something the
framework could infer from `[Transactional]` alone.

```csharp
services.AddBsftMediator(m => m.RegisterServicesFromAssembly(assembly));
services.AddBsftResilienceMediator();
```

Under `UseDefaults()` the framework fixes the behavior's position: retry sits
**outside** the transaction behavior and inside audit, validation and
authorization, so a retried attempt re-runs the handler **and its transaction**,
while validation, authorization and audit run once per request. Registration
order only decides the position in a hand-wired chain — one that omits
`UseDefaults()`. See [mediator.md](mediator.md), "The default chain".

### Retrying a shared transaction

A host that registered the shared transaction of `Bsft.Data`
([data.md](data.md#a-transaction-across-several-contexts)) has an
`IAmbientTransaction` in every request scope, and the behavior consults it. Four
things change for such a host:

- **Only the outermost unit is retried.** A `[Retryable]` request dispatched while
  the transaction is already open — a nested command of another module — passes
  through untried. The store has typically aborted the whole transaction with the
  failure (PostgreSQL answers every later statement with `25P02` until the
  rollback), so retrying the nested request inside it cannot succeed. The failure
  propagates to the outermost attempt, whose transaction is rolled back and whose
  enlisted change trackers are cleared, and the whole attempt runs again. A host
  that applies the mediator transaction to all commands gets the same rule without
  the shared transaction; see [Nested retryable requests](#nested-retryable-requests).
- **Every further attempt runs in a fresh scope.** The behavior is an
  `IRescopingPipelineBehavior` ([mediator.md](mediator.md#rescoping-behaviors)):
  attempts after the first resolve the later behaviors, the handler, the
  connection, the contexts and the transaction from a new dependency-injection
  scope, so no scoped service carries what the failed attempt left behind. State
  the request's scope holds (the tenant, the current user) reaches the fresh scope
  only through a registered `IRequestScopeSeed`. A host without the shared
  transaction keeps retrying in the request's scope, as before.
- **A transaction conflict is always retried.** A failure the provider's
  `ITransactionConflictClassifier` classifies as a conflict with a concurrent
  transaction — `Bsft.Data.Postgres` classifies `40001` (serialization failure)
  and `40P01` (deadlock), looking through EF Core's `DbUpdateException` — is
  retried even when `ShouldRetry` says no, within `MaxRetryAttempts`. A host can
  therefore narrow `ShouldRetry` to its own transient failures without losing the
  retry PostgreSQL asks for.
- **A misuse of the transaction is never retried.** The shared transaction throws
  an `AmbientTransactionMisuseException` (an `InvalidOperationException`) when a
  failure inside the unit was caught and the work completed anyway, when a second
  outermost unit starts beside a running one in the same scope, and when an
  enlisted context retries on its own through `EnableRetryOnFailure`. Each fails
  the same way on every attempt, and the overlapping unit would even succeed in a
  fresh scope and hide the bug, so neither the default `IsTransientFailure` nor a
  host's own `ShouldRetry` retries it. The one exception is a swallowed conflict:
  the swallowed failure is the inner exception, and when the classifier calls it a
  conflict (a caught `40P01`) the whole unit is retried as before, because the
  store ended that attempt. The classification walks the whole inner-exception
  chain, and from 2.2.1-preview.2 the misuse of a swallowed nested unit carries
  that unit's first failure as well.

The idempotency requirement is the same as for any `[Retryable]` request, and it
covers the whole outermost unit including every nested command: each attempt runs
every handler again. The database writes of a failed attempt are rolled back, but
an email, an outbound call or anything else outside the shared transaction is
not; defer such work to an outbox that commits with the transaction, or make it
idempotent.

For database transients the provider's classifier is the intended source of
truth, not `ShouldRetry`: the ambient transaction asks every registered
`ITransactionConflictClassifier`, which knows the store's own codes, while
`ShouldRetry` sees only exception types. A host on the shared transaction can
leave `ShouldRetry` at its default or narrow it to non-database transients (a
timeout of an outbound call made inside the unit); it does not need to list
`PostgresException` codes itself.

### Logging each retry

From 2.2.1-preview.2 the pipeline logs one warning per retry, when the host
registered logging (`AddLogging()`; without it the provider logs nothing). The
category is `Bsft.Resilience.Mediator.MediatorResiliencePipelineProvider`, the event
`1`/`MediatorRequestRetrying`, and the entry is structured:

| Property | Value |
| --- | --- |
| `RequestType` | Full name of the request's runtime type |
| `RetryAttempt` | The retry about to run, from 1 |
| `SqlState` | The SQLSTATE of the failure that caused the retry: of the conflict the ambient transaction classified (a swallowed `40P01`, also when the handler then failed with `25P02`), else the first `DbException.SqlState` in the failure's inner-exception chain, or `none` |
| `ExceptionType` | Full name of the exception the failed attempt ended with |

Neither the exception nor its message is logged: a store's message and detail can
carry the values of the rows involved. The final failure, which the request
propagates, is the caller's to log.

### Every command without the attribute

A host in which every write is retry-safe as a whole — typically one on the
shared transaction with an outbox for everything outside the database — can
retry every command instead of marking each one:

```csharp
services.AddBsftResilienceMediator(options => options.ShouldRetryAllCommands = true);
```

`ShouldRetryAllCommands` treats every `ICommand<T>` and `ICommand` as if it carried
`[Retryable]`; queries are still retried only where marked. Retry stays
outermost-only: a request dispatched from inside a retried one passes through, so
a nested failure reaches the outer attempt instead of being retried on its own
and multiplying the attempts. A startup validator fails the host when
`ShouldRetryAllCommands` is on and either the mediator does not apply the
transaction to all commands (`ApplyTransactionToAllCommands()`) or no
`IAmbientTransaction` is registered (`AddBsftDataSharedTransaction()` or
`AddBsftDataPostgresTransaction()`). Without one transaction around the unit, a
retry reruns the handlers whose writes already committed. Without the ambient
transaction, a retry runs in the request's scope over the contexts the failed
attempt used: an order it added before its `SaveChanges` failed is still tracked
as added, and the retry writes it a second time.

The property keeps the name `ShouldRetryAllCommands` rather than mirroring the
mediator's `ApplyTransactionToAllCommands()`: it is a bindable option
(`Bsft:Resilience:Mediator:ShouldRetryAllCommands`), and options are named as
properties, the builder call as a verb.

### Nested retryable requests

From 2.2.1-preview.1, `IRequestNesting` records two things about the outer
requests of a scope, and the behavior reads them as follows:

- **An outer request is being retried.** In every host, a `[Retryable]` request
  dispatched inside it passes through untried. This is a bug fix: in 2.2.0 such a
  nested request was retried on its own inside each attempt of the outer request,
  which multiplied the attempts. A host that relied on a nested retry marks the
  outer request `[Retryable]` instead.
- **An outer request holds a mediator transaction open.** A nested request passes
  through untried only in a host where it joins that transaction: one that
  registered an `IAmbientTransaction`, or applies the transaction to all commands
  (`ApplyTransactionToAllCommands()`, [mediator.md](mediator.md#transactions)).
  Inside a joined transaction the store has typically aborted with the failure, so
  only the outer attempt can succeed. Any other host, where the outer
  `[Transactional]` handler holds a transaction of its own, keeps retrying a
  nested `[Retryable]` request as in 2.2.0.

Apart from the first rule, a host that opts into none of the additions sees no
behavioral change.

The behavior keeps its slot in the default chain, inside authorization,
validation and audit, which therefore still run once per request.

### Options

`BsftResilienceMediatorOptions`:

| Property | Default | Meaning |
| --- | --- | --- |
| `MaxRetryAttempts` | `3` | Retries **after** the first attempt, not attempts: `3` runs a request at most four times. At least `1`, which the options validation enforces; a request that is not to be retried is not marked `[Retryable]`. |
| `BaseDelay` | 200 ms | Grows exponentially, with jitter. |
| `ShouldRetry` | `IsTransientFailure` | Predicate deciding whether to retry. A failure the ambient transaction's classifier calls a conflict is retried regardless; an `AmbientTransactionMisuseException` that is no conflict never is. |
| `ShouldRetryAllCommands` | `false` | Retry every command as if it carried `[Retryable]`. Needs `ApplyTransactionToAllCommands()` and a registered `IAmbientTransaction`, which a startup validator checks. |

The default predicate never retries `OperationCanceledException`, and never
retries the deterministic exceptions from `Bsft.Core`: `ValidationException`,
`BusinessRuleException`, `NotFoundException`, `ForbiddenException`,
`ConflictException`, and never `AmbientTransactionMisuseException`. A second
attempt would fail identically, so retrying them
only wastes the caller's time. Everything else is treated as transient. This is
the practical reason to throw the `Bsft.Core` exception types — see
[core.md](core.md).

## HTTP resilience

Two entry points. On an existing client builder:

```csharp
services.AddHttpClient("billing")
    .AddBsftResilience(o => o.MaxRetryAttempts = 5);
```

Or the one-liner that also wires correlation:

```csharp
services.AddBsftResilientHttpClient("billing", o => o.AttemptTimeout = TimeSpan.FromSeconds(5));
```

`AddBsftResilientHttpClient` registers the resilience handler first and the
correlation handler inside it, so each retried attempt still carries the
correlation header rather than only the first one. See
[correlation.md](correlation.md).

### Options

`BsftResilienceOptions` mirrors the Polly standard strategy:

| Property | Default | Meaning |
| --- | --- | --- |
| `MaxRetryAttempts` | `3` | Attempts after the first. |
| `BaseDelay` | 2 s | Exponential backoff base, with jitter. |
| `AttemptTimeout` | 10 s | Timeout for one attempt. |
| `TotalRequestTimeout` | 30 s | Timeout across all attempts. |
| `FailureRatio` | `0.1` | Failure share within the window that trips the breaker. |
| `SamplingDuration` | 30 s | The breaker's evaluation window. |
| `MinimumThroughput` | `100` | Calls needed in the window before the breaker can trip. |
| `BreakDuration` | 5 s | How long the circuit stays open. |

Two constraints are enforced: `AttemptTimeout` must be smaller than
`TotalRequestTimeout`, and `SamplingDuration` must be at least twice
`AttemptTimeout`. Both prevent configurations that would never behave as the
numbers suggest. They are checked on the registration call, where the stack
still names the client being configured, as well as by Polly when the handler is
built.

Both options types bind from configuration: `BsftResilienceOptions.SectionName`
is `"Bsft:Resilience"` and `BsftResilienceMediatorOptions.SectionName` is
`"Bsft:Resilience:Mediator"`. Every entry point has an `IConfiguration` overload
that reads its section from the configuration root.

They are two root options types rather than one with a nested group, because
they configure independent pipelines on different lifetimes: the HTTP options
describe one named client, of which an application legitimately has several with
different retry budgets, while there is exactly one mediator pipeline for the
whole process. The HTTP options are therefore never registered in the container
either — a second client would overwrite the first.

`MinimumThroughput` of 100 means a low-traffic client effectively never breaks
the circuit — lower it deliberately for a quiet dependency.

## Stream retry

Both halves above retry a **call**: the failed attempt produced nothing the
caller saw, so the retry is invisible. A stream is not like that. By the time an
`IAsyncEnumerable<T>` breaks, items are already downstream — written to a socket,
appended to a buffer, rendered — and the only way to retry is to ask the source
for the stream again, from its beginning. Streaming responses from a remote model
make this a recurring need rather than an exotic one.

`ResilientStreamExtensions.WithRetry` is that retry, and it does not hide the
hazard:

```csharp
var tokens = ResilientStreamExtensions.WithRetry(
    ct => client.StreamCompletionAsync(prompt, ct),
    options =>
    {
        options.TimeProvider = timeProvider;
        options.MaxRetryAttempts = 5;
        options.BaseDelay = TimeSpan.FromMilliseconds(500);
    },
    cancellationToken);

await foreach (var token in tokens)
    Console.Write(token);
```

The factory is invoked once per attempt. Its stream is enumerated until it ends
or throws; a throw is handed to the classifier, and if the classifier and the
budget both allow it, the factory is invoked again after a backoff.

### The partial-consumption hazard

`PartialStreamPolicy` decides what a failure **after** the first item does, and
there is no default the framework could pick that is right for everyone, so it
picks the one that cannot surprise anybody:

| Policy | Meaning |
| --- | --- |
| `RethrowMidStream` (default) | Retry only while nothing has been yielded. A mid-stream failure is rethrown to the consumer, which has already received the items produced so far. **No item is ever produced twice.** |
| `RestartFromStart` | Retry a mid-stream failure by re-invoking the factory. The stream starts over, so **every item already yielded is produced again** unless the factory resumes. |

`RestartFromStart` is the right choice only for a consumer that either tolerates
duplicates (an idempotent upsert keyed by item id) or resumes. Resuming is the
opt-in hook: the second `WithRetry` overload hands the factory a
`StreamRetryContext` carrying `AttemptNumber` and `ItemsYielded`, and a source
that can skip a prefix turns the restart into a continuation.

```csharp
var rows = ResilientStreamExtensions.WithRetry(
    (context, ct) => source.ReadFromAsync(offset: context.ItemsYielded, ct),
    options =>
    {
        options.TimeProvider = timeProvider;
        options.PartialStreamPolicy = PartialStreamPolicy.RestartFromStart;
    },
    cancellationToken);
```

Nothing forces the hook: a factory that ignores the context under
`RestartFromStart` restarts, which is exactly what the policy name says.

### Classification, budget and backoff

`ShouldRetry` is the classifier — a `Func<Exception, bool>`, the same idiom as
`BsftResilienceMediatorOptions.ShouldRetry`. Its default,
`BsftResilienceStreamOptions.IsTransientFailure`, treats everything as transient
except `OperationCanceledException`. It is deliberately broader than the
mediator's predicate, which also excludes the deterministic `Bsft.Core` domain
exceptions: this package does not reference `Bsft.Core`, and a stream of tokens
from a remote model is not where those are thrown. A host that wants that reading
assigns `BsftResilienceMediatorOptions.IsTransientFailure` here.

Being broad, the default also repeats failures a second attempt cannot fix — a
rejected credential, a malformed request, an argument the source refuses. Narrow
it for a stream behind an authorization or validation boundary, where retrying
only multiplies the load and delays the real error.

`MaxRetryAttempts` is the budget for the **whole** stream. Progress does not
replenish it, so a stream that keeps breaking and restarting cannot retry
forever. Only its lower bound is enforced: like the two options types above it
has no ceiling, because how much a downstream dependency can be asked to bear is
the host's call. The delay before retry *n* is `BaseDelay * 2^(n-1)`, capped by
`MaxDelay` and, with `UseJitter`, scaled by a random factor of at least 0.5 and
less than 1. The wait runs on the configured `TimeProvider`, so a test asserts
the backoff without spending it.

A fault raised while the enumerator is torn down goes through the classifier like
any other, under one rule: the first failure of an attempt wins. So a break whose
source also throws on disposal is still classified by the break, not by the
cleanup — and the cleanup fault is not lost either, it is attached to the thrown
exception's `Data` under `ResilienceDefaults.SuppressedFailureDataKey`. The value
is the suppressed exception itself rather than a rendered string, so the host
decides how much of it to surface: `Exception.Data` is a sink several logging
pipelines serialize on their own, and a disposal failure from a database or
network client can carry a host name in its message. Treat it as diagnostic
material to redact deliberately, not as something to render into a response.

`OperationCanceledException` is never classified and never retried: it means the
enumeration was asked to stop, so it ends the stream. Cancellation is observed
between attempts as well as during the backoff, which matters when the backoff is
zero. The source enumerator is disposed on every path — completion, failure,
cancellation, and the consumer simply abandoning the stream.

### Options

`BsftResilienceStreamOptions`:

| Property | Default | Meaning |
| --- | --- | --- |
| `MaxRetryAttempts` | `3` | Retries for the whole stream. |
| `BaseDelay` | 200 ms | Doubles per retry. |
| `MaxDelay` | 30 s | Ceiling for one delay. |
| `UseJitter` | `true` | Scales the delay by a factor in `[0.5, 1)`. |
| `ShouldRetry` | `IsTransientFailure` | The classifier. |
| `PartialStreamPolicy` | `RethrowMidStream` | What a mid-stream failure does. |
| `TimeProvider` | none, required | What the backoff waits on; `WithRetry` throws when it is not set. |

They are validated on the `WithRetry` call, before anything is enumerated: a
budget that could never work fails where the stack still names the call site,
rather than inside an enumeration that may not break for hours.

Unlike the two options types above, this one carries no `SectionName` and no
`IConfiguration` overload. Half of it is objects chosen in code — a classifier
delegate and a time provider — and what it configures is a static call rather
than a registration, so there is no host-wide instance for a section to bind. A
host that keeps the numbers in `appsettings.json` reads them itself and assigns
them in the configure action. `ResilientStreamExtensions` is likewise not
registered: the package gains no second `ServiceCollectionExtensions`, because
there is nothing to resolve.

## Example

```csharp
[Retryable]
public sealed record GetExchangeRate(string Currency) : IQuery<decimal>;

public sealed class GetExchangeRateHandler(IHttpClientFactory factory)
    : IQueryHandler<GetExchangeRate, decimal>
{
    public async Task<decimal> HandleAsync(GetExchangeRate query, CancellationToken ct = default)
    {
        var client = factory.CreateClient("rates");
        return await client.GetFromJsonAsync<decimal>($"/rate/{query.Currency}", ct);
    }
}
```

Both layers apply here: the HTTP pipeline retries the call itself, and the
mediator pipeline retries the whole handler if the call ultimately fails. Keep
that in mind when setting attempt counts — they multiply.

`samples/Bsft.Sample.WebApi/Resilience` shows every option of both option classes
with its default value.

## See also

- [mediator.md](mediator.md) — behavior ordering.
- [core.md](core.md) — the exception types the retry predicate treats as final.
