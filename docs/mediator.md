# Bsft.Mediator

A CQRS dispatcher: commands, queries and notifications are objects, handlers are
resolved from the container, and cross-cutting concerns are pipeline behaviors
wrapped around the handler rather than code repeated in it.

Two packages:

| Package | Contents |
| --- | --- |
| `Bsft.Mediator.Abstractions` | The contracts only — `IMediator`, `ICommand`, `IQuery`, `INotification`, their handlers, `IPipelineBehavior<,>`, `IRequestMetadataProvider`, `[Transactional]`. |
| `Bsft.Mediator` | The dispatcher, the built-in behaviors and the DI entry point. |

A module that only *defines* commands and handlers depends on the abstractions
package; only the host needs the implementation. Since 1.3.5 the contracts live
in namespace **`Bsft.Mediator.Abstractions`**, matching the assembly they ship
in; they used to share `Bsft.Mediator.Abstractions` with the implementation package, and
two assemblies never share a namespace. The dispatcher and the four shipped
behaviors are in `Bsft.Mediator.Services`, and `MediatorConfiguration` and
`AddBsftMediator` are in `Bsft.Mediator`.

## Contracts

```csharp
public interface ICommand<TResult>;
public interface ICommand : ICommand<Unit>;
public interface IQuery<TResult>;
public interface INotification;

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken ct = default);
}

public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    Task HandleAsync(TCommand command, CancellationToken ct = default);
}

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken ct = default);
}

public interface INotificationHandler<in TNotification> where TNotification : INotification
{
    Task HandleAsync(TNotification notification, CancellationToken ct = default);
}
```

The dispatcher itself:

```csharp
public interface IMediator
{
    Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct = default);
    Task SendAsync(ICommand command, CancellationToken ct = default);
    Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken ct = default);
    Task PublishAsync<TNotification>(TNotification notification, CancellationToken ct = default)
        where TNotification : INotification;
}
```

Commands go through `SendAsync`, queries through `QueryAsync`. The split is not
decoration: only the command path is wrapped by the transaction behavior, and
the two are reported separately in telemetry.

## Setup

```csharp
services.AddBsftMediator(mediator => mediator
    .RegisterServicesFromAssembly(typeof(CreateOrderHandler).Assembly)
    .EnableTracing());
```

`AddBsftMediator` takes a **required** configure delegate — passing `null`
throws. It registers `IMediator` as scoped, the configuration as a singleton,
the telemetry emitter, and `MediatorStartupValidator` as an `IStartupValidator`.

`MediatorConfiguration` has exactly six methods:

| Method | Effect |
| --- | --- |
| `RegisterServicesFromAssembly(Assembly)` | Scans the assembly and registers every `ICommandHandler<,>`, `ICommandHandler<>`, `IQueryHandler<,>` and `INotificationHandler<>` implementation as transient. Callable more than once. |
| `AddBehavior(Type)` | Adds an open-generic pipeline behavior (a generic type definition implementing `IPipelineBehavior<,>`). Anything else is rejected at registration with an `ArgumentException`. |
| `ForwardNotifications()` | Also hands every notification to the registered `INotificationForwarder` after the local handlers, so aggregator subscribers and sagas see it. |
| `EnableTracing()` | Registers `TracingBehavior<,>` as the outermost behavior and emits spans and metrics. |
| `UseDefaults()` | Registers the canonical cross-cutting chain from whichever behavior packages the application references, in the order below. Implies `EnableTracing()`. |
| `SuppressDefaultBehavior(string)` | Drops one behavior from that chain. A name matching none throws. |

## Pipeline behaviors

```csharp
public interface IPipelineBehavior<TRequest, TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken ct = default);
}
```

**Registration order is outermost-first.** The dispatcher resolves all behaviors
and folds them around the handler in reverse, so the first one registered is the
first one entered and the last one left. When `EnableTracing()` is used, the
tracing behavior is registered ahead of every user behavior deliberately, so its
span encloses the whole chain regardless of the order the application asked for.

A behavior applies to *every* request dispatched under the static contract type
and is therefore always an open generic. Register it either through the
configuration or directly against the service type — both end at the same
registration:

```csharp
services.AddBsftMediator(m => m.AddBehavior(typeof(MyBehavior<,>)));
// equivalent:
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(MyBehavior<,>));
```

The direct form is what every framework module does — caching, retry, audit and
org enforcement all register their behavior this way from their own `AddBsft*`
call.

There is deliberately no per-concrete-request registration: the dispatcher
resolves `IPipelineBehavior<TRequest, TResponse>` for the **static** type it
dispatches under (`ICommand<TResult>` or `IQuery<TResult>`), never the concrete
request class, so a behavior decides inside `HandleAsync` which requests it acts
on.

> Passing a type that is not an open-generic pipeline behavior throws at
> registration with a message naming the type, rather than being accepted and
> silently never invoked. Behaviors registered as themselves alone were never
> resolved by the dispatcher; that dead path is gone.

### Which behaviors apply to a request

Behaviors are resolved by the **static** generic arguments at the dispatch site,
not by the runtime type of the request object. `SendAsync<TResult>(ICommand<TResult> command)`
resolves `IPipelineBehavior<ICommand<TResult>, TResult>`, so a behavior closed
over a *concrete* command type is not consulted on that path.

The practical rule: a behavior meant to apply broadly must be an **open generic**
registered against `IPipelineBehavior<,>`. Use a closed behavior only where the
call site is itself typed to that request. This is why every cross-cutting
concern in the framework ships as an open generic.

The module ships four:

| Behavior | Purpose |
| --- | --- |
| `LoggingBehavior<,>` | Logs start, duration and outcome. |
| `TracingBehavior<,>` | Span plus metrics; outermost when tracing is enabled. |
| `ValidationBehavior<,>` | Runs the registered `IValidator<TRequest>` and throws `ValidationException`. |
| `TransactionBehavior<,>` | Wraps handlers marked `[Transactional]` in a database transaction. |

### Attribute-driven behaviors: the request metadata seam

Because the pipeline is closed over the declared contract, a behavior that acts on
an attribute has to read it off `request.GetType()`. That works, and it is what
every shipped attribute-driven behavior does — but the reflection is per dispatch,
so each of them grew a `ConcurrentDictionary<Type, …>` of its own to keep it off
the hot path. Consumers hit the same wall and wrote the same cache again, once per
behavior. A behavior that skipped the cache and reflected on every call paid for
it; one that read `typeof(TRequest)` instead silently did nothing at all, because
the type parameter is `ICommand<TResult>` and never the concrete request.

The framework supplies that cache. `AddBsftMediator` registers
`IRequestMetadataProvider` as a singleton, and any behavior can take it:

```csharp
public interface IRequestMetadataProvider
{
    IRequestMetadata GetMetadata(object request);   // by instance
    IRequestMetadata GetMetadata(Type requestType); // by type
}

public interface IRequestMetadata
{
    Type RequestType { get; }                       // the concrete request class
    TAttribute? FindAttribute<TAttribute>()          where TAttribute : Attribute;
    IReadOnlyList<TAttribute> ListAttributes<TAttribute>() where TAttribute : Attribute;
    bool HasAttribute<TAttribute>()                 where TAttribute : Attribute;
}
```

Both live in `Bsft.Mediator.Abstractions`, so a behavior satellite takes the seam
without taking the dispatcher into its closure. The descriptor is built once per
request type — one reflection pass capturing every attribute, `inherit: true`, the
same way the shipped behaviors read theirs — and the same instance is handed to
every later caller. It is immutable, and the provider is safe to call from several
threads at once.

**Before** — the consumer's own cache, written once per behavior:

```csharp
public sealed class UsageRecordingBehavior<TRequest, TResponse>(IUsageRecorder recorder)
    : IPipelineBehavior<TRequest, TResponse>
{
    // One of these per attribute-driven behavior in the application.
    private static readonly ConcurrentDictionary<Type, UsageAttribute?> Cache = new();

    public async Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken ct = default)
    {
        var usage = Cache.GetOrAdd(
            request!.GetType(),
            static type => type.GetCustomAttribute<UsageAttribute>(inherit: true));

        var response = await next().ConfigureAwait(false);

        if (usage is not null)
            await recorder.RecordAsync(usage.Feature, ct).ConfigureAwait(false);

        return response;
    }
}
```

**After** — the cache is deleted, and with it the `ConcurrentDictionary`, the
`static` field and the `System.Reflection` using:

```csharp
public sealed class UsageRecordingBehavior<TRequest, TResponse>(
    IRequestMetadataProvider metadata,
    IUsageRecorder recorder)
    : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken ct = default)
    {
        var usage = metadata.GetMetadata(request!).GetAttribute<UsageAttribute>();

        var response = await next().ConfigureAwait(false);

        if (usage is not null)
            await recorder.RecordAsync(usage.Feature, ct).ConfigureAwait(false);

        return response;
    }
}
```

The behavior stays registered against the marker interface exactly as before —
`AddBehavior(typeof(UsageRecordingBehavior<,>))` — and `metadata.RequestType` is
the concrete command, not the contract it was dispatched under. Testing a behavior
that takes the seam needs no container: `IRequestMetadata` is an interface, so a
fake describing one request type is enough.

Two things the seam deliberately does not do. It is a **cache, not a policy**: the
registration is `TryAddSingleton`, so a host that supplies its own provider keeps
it — and `RequestMetadataProvider` is public and constructible, so that provider
can wrap the shipped one instead of reimplementing the reflection behind it. And
it does not change what a behavior is handed — the `IPipelineBehavior<,>`
signature is unchanged, and surfacing the concrete type through a context
parameter instead would have broken every behavior in every consumer to save a
call that `request.GetType()` already answers. What was missing was never access
to the type; it was somewhere to put the reflection result.

**Constrained open-generic marker behaviors still never bind.** A behavior
declared `where TRequest : IAuditable` is not resolved for
`IPipelineBehavior<ICommand<TResult>, TResult>`, because the dispatch type does
not satisfy the constraint, so it is registered and never invoked. That is a
property of the resolution, not something the metadata provider changes: express
the opt-in as an attribute or a marker interface read *inside* `HandleAsync`, the
way the shipped behaviors do.

## The default chain

```csharp
services.AddBsftMediator(mediator => mediator
    .RegisterServicesFromAssembly(appAssembly)
    .UseDefaults());
```

`UseDefaults()` registers the cross-cutting behaviors of every behavior package
the application **references**, in the order the framework fixes. A package that
is not referenced contributes nothing and the host writes no conditional for it.
Registering the package's own services stays the host's job — `AddBsftCachingMediator`
still has to be called — and a behavior whose package is referenced but never
registered is reported by the mediator startup validator, naming the call to
make, instead of failing on the first dispatch.

Ordering is no longer something each host has to get right by reading a warning.
It is load-bearing, and each adjacency is there for a reason:

| Position | Behavior | Package | Why here |
| --- | --- | --- | --- |
| 1 | Tracing | `Bsft.Mediator` | Outermost, so its span encloses every other behavior and the handler. |
| 2 | *(correlation)* | — | Reserved. No correlation behavior ships yet; correlation reaches the pipeline through `TracingBehavior`'s `ICorrelationIdAccessor`. |
| 3 | Authorization (tenant, then org) | `Bsft.Auth.Mediator`, `Bsft.Organization.Mediator` | Before validation, so an unauthorized caller learns nothing about the shape of the payload. |
| 4 | Validation | `Bsft.Mediator` | After authorization, before anything is recorded or written. |
| 5 | *(idempotency)* | — | Reserved. No idempotency behavior ships yet. |
| 6 | Audit | `Bsft.Auditing.Mediator` | Inside authorization, so a rejected call is not recorded as an attempted business action; outside the transaction, so the audit write is not rolled back with it. |
| 7 | Retry | `Bsft.Resilience.Mediator` | Outside the transaction, so each attempt begins its own; inside audit and validation, so a retried request is validated and recorded once. |
| 8 | Transaction | `Bsft.Mediator` | Inside retry, so an attempt that failed never inherits a transaction the previous failure aborted. |
| 9 | Caching | `Bsft.Caching.Mediator` | Innermost, so it can never serve past authorization. |
| — | handler | | |

The order is applied when the pipeline is folded, not when the behaviors are
registered: a behavior package registers its own behavior from its own `AddBsft*`
call, which a host may make before or after `AddBsftMediator`, so registration
order is not something the defaults could control. Behaviors the host adds — via
`AddBehavior` or directly against `IPipelineBehavior<,>` — keep the position they
were registered in, around the default ones.

**A retry gets a fresh transaction.** The transaction is opened inside the retry
loop, so each attempt runs its own BEGIN/COMMIT/ROLLBACK — matching EF Core's own
rule that a manually demarcated transaction belongs inside the retried delegate,
not around it. That costs one extra round trip per retry, and it requires
something of the handler: a request marked both `[Transactional]` and
`[Retryable]` must be safe to run twice. `[Retryable]` is still only for
idempotent requests — retrying a command with side effects duplicates them — and
work committed by an earlier attempt cannot leak into the next one, because a
failed attempt is rolled back whole before the next begins. What it does not
protect is the scoped `DbContext` shared across attempts: EF Core's change
tracker is not cleared between attempts, so a handler that mutates tracked
entities and calls `SaveChangesAsync` should re-read what it writes rather than
rely on state a previous attempt left tracked. `Bsft.Auditing.EFCore`'s
`AuditOutboxInterceptor` is the framework's own example of discarding
still-tracked entities before a retried save, scoped to the outbox entity only.

What does not re-run: authorization, validation and audit sit outside retry, so
a retried request is authorized, validated and audited exactly once; caching
sits inside, so a cache write happens on the successful attempt only.

Reordering these two is all or nothing. `SuppressDefaultBehavior` plus
`AddBehavior` does not do it: the canonical rank is resolved over every slot
regardless of suppression, on purpose — a behavior the host registers itself
still belongs in its canonical position — so a re-registered `TransactionBehavior`
or `RetryBehavior` is sorted straight back. That is what keeps a host from
breaking the order by accident, and it means a host that wants **one transaction
spanning every attempt** — the pre-1.3.8.6 arrangement — does not call
`UseDefaults()` at all and registers the whole chain itself.

Both authorization behaviors decide on the dispatched **instance**, not on the
type parameter: the pipeline is closed over the declared contract
(`ICommand<TResult>`), so a marker or attribute on the concrete command is
invisible to the type parameter. `AuthorizationBehavior<,>` reads
`[TenantAuthorize]` off `request.GetType()` and `OrganizationAuthorizationBehavior<,>`
tests the request for `IRequireOrganizationPermission`; an unmarked request passes
through, and both hold their slot ahead of validation.

`AuthorizationBehavior<,>` reads a second attribute with it:
`[RequirePermission("credentials.manage")]`, from `Bsft.Mediator.Abstractions`.
It is enforced identically — the same requirement, the same user, the same
refusal when there is no `HttpContext` — and exists only so that a package which
maps no endpoints can name a permission without acquiring `Bsft.Auth` and, with
it, the ASP.NET Core shared framework. Use `[TenantAuthorize]` where the package
already has a web dependency; use `[RequirePermission]` where taking one would be
a deployment change for the package's consumers.

### A declared permission nothing enforces

Both declarations are inert on their own. `[RequirePermission]`,
`[TenantAuthorize]` and `IRequireOrganizationPermission` say what a caller must hold; the
behavior in the pipeline is what checks it, and registering the behavior is the
host's call. A host that dispatches such requests without registering the
behavior therefore has a mediator that works and permissions that do nothing.

The mediator reports that at startup, as a **warning** on the validator's result,
which `AddBsftStartupValidation()` logs at warning level:

```
AuthorizationCoverageValidator: 2 dispatched request type(s) declare a permission
that no registered pipeline behavior enforces: Contoso.Billing.RotateCredentialCommand,
Contoso.Billing.RevokeCredentialCommand. The declaration is decorative until
'Bsft.Auth.Mediator.AuthorizationBehavior`2, Bsft.Auth.Mediator' runs in the
mediator pipeline: those requests reach their handlers unchecked. Call
AddBsftAuthMediator() (next to UseDefaults(), or AddBehavior for a hand-wired
chain), or remove the declaration from those requests if another gate already
enforces it. The check reads registrations only: a behavior wired through a
factory delegate cannot be seen and is reported here even though it runs, and
pipeline order is not verified at all — in a hand-wired chain a short-circuiting
behavior ahead of authorization keeps this check quiet while enforcement never
runs. UseDefaults() is what guarantees the order.
```

A notification carrying such a declaration is reported separately, because no
registration closes that gap: publishing goes straight to the handlers and never
enters the pipeline, so the permission has to be enforced where the notification
is raised, or in the handler, and the declaration removed.

The check runs whether or not `UseDefaults()` was called: a hand-wired pipeline
leaves the same hole, and a host that calls `UseDefaults()` without referencing
`Bsft.Auth.Mediator` gets a slot that resolves to nothing, so the
referenced-but-unregistered check above has nothing to report either. It is a
warning rather than a failure because registration stays opt-in — a permission
enforced by an endpoint policy alone is a legitimate deployment — so the
framework names what is unenforced and leaves the decision with the host.

Request types are named in full, namespace included. Two requests of the same
simple name in different namespaces are two gaps, and a warning naming only
`RotateCredentialCommand` would leave the reader guessing which one to fix.

**What the check does and does not verify.** It verifies **registration**, and
only where a registration names a type: the open-generic
`AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>))`,
a closed-generic registration over a single request type, and a pre-built
instance are all recognised. A behavior handed to the container as a **factory
delegate** names no implementation type and cannot be seen, so such a host gets a
warning that describes no real gap — the warning says so itself rather than
leaving the reader to chase it.

It says nothing about **order**. A hand-wired chain in which a short-circuiting
behavior — caching, an early idempotency return — runs ahead of authorization
passes this check while enforcement never runs on the short-circuited path.
Verifying order is not attempted; ordering stays the host's responsibility, and
`UseDefaults()` is what guarantees it, which is the reason the canonical order is
stated by the framework rather than by each host.

The two enforcing behaviors are matched by name, exactly as the default chain
resolves them, which is what keeps `Bsft.Mediator` free of a reference to
`Bsft.Auth` and `Bsft.Organization`. A host enforcing these declarations with a
behavior of its own is therefore warned about as well; register the framework
behavior, or carry an own marker instead of the framework's attribute.

### Escape hatches

Two, and both are deliberate:

```csharp
// One behavior out, the rest in canonical order.
services.AddBsftMediator(m => m
    .UseDefaults()
    .SuppressDefaultBehavior(MediatorDefaults.Caching));

// The whole chain the host's own: omit UseDefaults() and registration order is
// the pipeline order, exactly as before 1.3.8.
services.AddBsftMediator(m => m
    .AddBehavior(typeof(MyAuthorizationBehavior<,>))
    .AddBehavior(typeof(MyCachingBehavior<,>)));
```

Names come from `MediatorDefaults` (`Tracing`, `Authorization`,
`OrganizationAuthorization`, `Validation`, `Audit`, `Retry`, `Transaction`, `Caching`). A
name matching no default behavior throws at registration rather than being
ignored — the mistake a silent no-op hides is a host that believes it disabled a
security behavior, or that one is still in place.

Registering the same behavior twice — `EnableTracing()` next to a hand-written
tracing registration, say — is reported by the startup validator too, instead of
running the behavior twice per request. See [caching.md](caching.md) for what the
caching position protects.

## Notifications bypass the pipeline

`PublishAsync` invokes the handlers directly; no behavior wraps them. Each
handler is isolated, and if any threw, the failures are rethrown together as a
single `AggregateException`. One failing subscriber therefore cannot stop the
others from running, and none of them can be silently swallowed.

## Forwarding notifications to the event aggregator

A mediator notification reaches only its `INotificationHandler<T>`
implementations. To let a saga — or anything else subscribed at runtime — react
to the same notification, forward it:

```csharp
services.AddBsftEventAggregator();

services.AddBsftMediator(mediator => mediator
    .RegisterServicesFromAssembly(assembly)
    .ForwardNotifications());

services.AddBsftMediatorEventAggregator();   // package Bsft.Mediator.EventAggregator
```

With this configured, `PublishAsync` runs the notification handlers first
**and** then hands the same instance to the registered `INotificationForwarder`,
whose event-aggregator bridge publishes it, so aggregator subscribers and saga
flows see it. A forwarding failure is folded into the same `AggregateException`
as handler failures. Without the configuration, the mediator and the aggregator
are independent and a saga never observes a mediator notification.

Forwarding needs a target: `AddBsftMediatorEventAggregator()` supplies the
bridge over the aggregator that `AddBsftEventAggregator()` registers. Enabling
`ForwardNotifications()` without a registered forwarder fails startup validation
and, failing that, throws from `PublishAsync` instead of dropping notifications
silently. See [event-aggregator.md](event-aggregator.md).

## Transactions

`[Transactional]` is applied to the **handler class**, not to the request:

```csharp
[Transactional]
public sealed class CreateOrderHandler(IOrderDbContext db) : ICommandHandler<CreateOrder, Guid>
{
    public async Task<Guid> HandleAsync(CreateOrder command, CancellationToken ct = default)
    {
        ...
    }
}
```

`TransactionalAttribute` lives in `Bsft.Mediator.Abstractions`, so a module can
mark its handlers without referencing the implementation.

The behavior looks for the marked handler by the **runtime** type of the dispatched
request, cached per type, and recognises all three handler shapes
(`ICommandHandler<,>`, `ICommandHandler<>`, `IQueryHandler<,>`). Reading the type
parameter instead found nothing on the dispatch path, because the pipeline is closed
over `ICommand<TResult>` — the same shape of defect the two authorization behaviors
had. A transaction is only opened when a `DbContext` resolves from the container;
otherwise the behavior logs a warning and runs the handler unwrapped.

Resolving that `DbContext` needs no extra wiring: a plain
`services.AddDbContext<AppDbContext>()` is enough. `AddDbContext<TContext>` registers
only the concrete `TContext`, never the base `DbContext` service type, so the behavior
first tries the base type — for a host that registered it explicitly — and otherwise
falls back to the concrete context named by the registered `DbContextOptions`, whose
`ContextType` is `TContext`. The resolved target **type** is cached on the resolver,
which is an application singleton, keyed by the `DbContextOptions` service type, so the
`ContextType` reflection runs at most once per application in the common case for both
`AddDbContext<T>()` and `AddDbContextPool<T>()` — not once per request. A concurrent
cold-start may compute it more than once, but the result settles to a single cached entry.
Only the type is cached; the concrete
`DbContext` instance is still resolved from the request scope on every dispatch.

When more than one `DbContext` is registered, that fallback is ambiguous: EF Core's
`TryAdd` binds the non-generic `DbContextOptions` to whichever context was registered
first, so the behavior could open and commit a transaction on the wrong context while a
handler writing to another one stays unprotected. Rather than infer silently, a startup
validator fails fast the moment `TransactionBehavior` is in the pipeline and more than
one `DbContext` is registered with no base `DbContext` to disambiguate — the same way
`UseDefaults()` fixes behavior order and the coverage check reports an unenforced
permission. The single-context case resolves silently; a host with several contexts
disambiguates by registering the base `DbContext` explicitly
(`services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>())`), which
both names the context the transaction wraps and clears the startup failure.

With `UseDefaults()` the transaction is opened inside the retry behavior, so a
`[Transactional]` handler on a `[Retryable]` request gets one transaction per
attempt, not one shared across every attempt. See "The default chain" above.

## Telemetry

With `EnableTracing()`, the module reports on the instrumentation source
`Bsft.Mediator`: `bsft.mediator.request.duration`, `bsft.mediator.requests` and
`bsft.mediator.failures`. The outcome tag separates `success`, `expected_error`
(a deterministic `BsftException`) and `error`, so an alert on genuine faults is
not drowned by ordinary validation failures. See
[observability.md](observability.md).

## Example

```csharp
public sealed record GetOrder(Guid OrderId) : IQuery<OrderDto>;

public sealed class GetOrderHandler(IOrderDbContext db) : IQueryHandler<GetOrder, OrderDto>
{
    public async Task<OrderDto> HandleAsync(GetOrder query, CancellationToken ct = default)
    {
        var order = await db.Orders.FindAsync([query.OrderId], ct)
            ?? throw new NotFoundException("Order", query.OrderId);

        return new OrderDto(order.Id, order.Total);
    }
}

// in an endpoint
var dto = await mediator.QueryAsync(new GetOrder(id), ct);
```

`samples/Bsft.Sample.WebApi` wires the mediator across three assemblies and
forwards notifications into the event aggregator.

## See also

- [event-aggregator.md](event-aggregator.md) — where forwarded notifications land.
- [caching.md](caching.md), [resilience.md](resilience.md), `Bsft.Auditing` — behaviors contributed by other modules.
- the getting-started guide — the minimal wiring.
