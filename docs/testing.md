# Bsft.Testing and Bsft.Auth.Testing

Test doubles and harnesses for the framework's ambient concerns — the tenant,
the correlation id, the clock and, with `Bsft.Auth.Testing`, the current user —
plus in-process harnesses for mediator handlers and saga flows, and a
`WebApplicationFactory` with the doubles already installed.

Both packages are referenced by test projects only, and both are development
dependencies. They are split by tier
([package-conventions.md](package-conventions.md#tiers-foundation-and-framework)):

| Package | Tier | Carries |
| --- | --- | --- |
| `Bsft.Testing` | Foundation | tenant and correlation doubles, the clock, the mediator and saga harnesses, `BsftWebApplicationFactory<TEntryPoint>`, the fake OIDC authority and the fake push service |
| `Bsft.Auth.Testing` | framework | the user and permission doubles, the `BsftTest` authentication scheme and `BsftAuthWebApplicationFactory<TEntryPoint>` |

`Bsft.Testing` depends on no `Bsft.Auth` package, so a Foundation test project
can use it; an application on `Bsft.Auth` adds `Bsft.Auth.Testing`, which brings
`Bsft.Testing` along. Until 2.0.0 everything was in `Bsft.Testing`; see
[the 2.1.0 migration notes](migration-2.1.0.md) for the move.

The types of `Bsft.Testing` live in namespaces by role: the context doubles in
`Bsft.Testing.Contexts`, the clock in `Bsft.Testing.Time`, the mediator harness in
`Bsft.Testing.Mediation`, the saga harness in `Bsft.Testing.Sagas`, and the web
feature in `Bsft.Testing.Web` with `BsftWebApplicationFactory<TEntryPoint>` and the
test-host environment policy at its root, the fake authority and its records in
`Bsft.Testing.Web.Oidc` and the fake push service in `Bsft.Testing.Web.Push`. The
registration methods are in `Bsft.Testing.DependencyInjection`. `Bsft.Auth.Testing`
keeps its types at its root and its registration methods in
`Bsft.Auth.Testing.DependencyInjection`.

## Context doubles

Four fakes replace the ambient services that are otherwise resolved from an HTTP
request:

| Double | Package | Replaces | Default |
| --- | --- | --- | --- |
| `TestTenantContext` | `Bsft.Testing` | `ITenantContext` | tenant `"test-tenant"` |
| `TestCorrelationContext` | `Bsft.Testing` | `CorrelationContext` / `ICorrelationContext` | `"test-correlation-id"` |
| `TestUserContext` | `Bsft.Auth.Testing` | `ICurrentUser` | user `…0001`, authenticated |
| `TestPermissionSource` | `Bsft.Auth.Testing` | the granted permissions | none |

```csharp
services.AddBsftTestContexts();                          // tenant + correlation, defaulted
services.AddBsftTestTenant(new TestTenantContext("acme"));
services.AddBsftTestUser(new TestUserContext(userId));   // Bsft.Auth.Testing
services.AddBsftTestPermissions(new TestPermissionSource("orders.read"));
```

Every one of these **replaces** the existing registration rather than adding to
it, so they are safe to apply to a fully configured container.

The doubles are mutable mid-test — `SignIn`/`SignOut` on the user, `SetTenant`/
`Clear` on the tenant — which is how you assert that an endpoint rejects an
anonymous caller without building a second host.

`SetTenant` also changes what the tenant query filter returns, so one host can
assert that a row written under one tenant is a 404 for the next. It works with
`UseTenantFilter` and `UseStaticTenantFilter` alike, because the double is a
single instance the test mutates and the filter reads its value per query. The
defect that `UseTenantFilter(this)` fixes is the *instance* a filter captures, and no
mutated singleton can reproduce it — that needs a tenant context resolved per
scope, which is what a real host has. See [data.md](data.md#multi-tenancy).

Permission grants follow the production shape: `tenant:{tenantId}:{permission}`
is tenant-scoped, a bare permission is global. See `Bsft.Auth`.

## The clock

```csharp
public interface ITestClock
{
    DateTimeOffset UtcNow { get; }
    void Advance(TimeSpan by);
    void SetUtcNow(DateTimeOffset utcNow);
}
```

`TestClock` derives from `TimeProvider`, so registering it as the
`TimeProvider` makes every framework component that respects the clock — saga
timeouts, the scheduler, the outbox — virtual. It starts at
`DateTimeOffset.UnixEpoch`. `Advance` refuses a negative span.

Only `GetUtcNow()` is overridden; timers created from the base class still run on
the real clock. Advance the clock and drive the pass explicitly rather than
waiting for a timer to fire.

## Mediator harness

```csharp
using var harness = new MediatorHarnessBuilder()
    .WithHandler<AddNumbersHandler>()
    .WithService<IRateProvider>(fakeRates)
    .Build();

var result = await harness.SendAsync(new AddNumbers(2, 3));
```

The builder composes a minimal container: `WithHandler<T>()` (or the `Type`
overload), `WithBehavior(Type)`, `WithService<T>(instance)`,
`WithServices(Action<IServiceCollection>)` and `WithTestContexts()`. `Build()`
adds the mediator last and builds the provider with `validateScopes: true`, so a
captive dependency fails in the test rather than in production.

Behaviors run in the order added, first outermost — matching the production rule
in [mediator.md](mediator.md).

The harness exposes the same four operations as `IMediator` (`SendAsync` ×2,
`QueryAsync`, `PublishAsync`) plus `Mediator` and `Services` for anything else.

## Saga harness

```csharp
using var harness = new SagaHarnessBuilder()
    .WithFlow<OrderFulfillmentFlow>()
    .StartingAt(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
    .Build();

await harness.PublishAsync(new OrderPlaced(orderId, 100m));

var fired = await harness.AdvanceTimeAsync(TimeSpan.FromDays(3));

var saga = await harness.GetSagaAsync<OrderState, OrderPlaced>(orderId.ToString());
saga!.State.IsFollowUpSent.Should().BeTrue();
```

`Build()` registers the test clock before the engine, which is what makes durable
timers virtual, and calls `UseBsftSagaFlows()` so the flows are actually subscribed.

| Member | Purpose |
| --- | --- |
| `PublishAsync` | Push an event into the flow. |
| `AdvanceTimeAsync(by)` | Move the clock and drain everything that became due. |
| `DrainTimeoutsAsync()` | Drain due timers without moving the clock. |
| `FindSagaAsync<TState, TStartEvent>(key)` | Read the persisted instance. |
| `ListPendingTimeoutsAsync()` | Inspect scheduled timers. |
| `Events`, `Store`, `Timeouts`, `Clock`, `Services` | Direct access. |

The timeout hosted service is deliberately **not** started; both drain methods
pump the real dispatcher by hand, so no poll interval is involved and the test
has no wall-clock waits. Both repeat until nothing is due, and return a count
that is a diagnostic rather than an assertion target — assert on the saga's
state, not on how many timers fired. See [saga-timeouts.md](saga-timeouts.md).

## Web application factory

```csharp
public sealed class ApiFactory : BsftWebApplicationFactory<Program>
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddBsftTestDatabase<AppDbContext>("orders-tests");
    }
}
```

`BsftWebApplicationFactory<TEntryPoint>` sets the environment to `Testing` and
installs the tenant and correlation doubles. The database is **not** touched by
default — call `AddBsftTestDatabase<TContext>` to swap in an in-memory provider.
Override `CreateTenantContext`, `CreateCorrelationContext` or `EnvironmentName`
to change the starting point.

An application on `Bsft.Auth` derives from `BsftAuthWebApplicationFactory<TEntryPoint>`
(`Bsft.Auth.Testing`) instead. It adds the user double and makes the test
authentication scheme the default, told the environment the factory boots in;
override `CreateUserContext` to start as another or an anonymous user. A factory
bringing doubles of its own overrides `ConfigureTestDoubles` and calls the base
first, which is how the auth factory is built. The live doubles are reachable
through the provider, which is how a test changes identity between requests:

```csharp
factory.Services.GetRequiredService<TestUserContext>().SignIn(otherUserId);
```

## The fake OpenID Connect authority

`FakeOidcAuthority` publishes a discovery document and a key set and exchanges authorization codes for
signed identity tokens, all in-process over a real RSA signature. Point the application's OpenID
Connect back channel at `CreateBackchannelHandler()` and a federated login runs end to end with no
identity provider in the loop.

It also mints **back-channel logout tokens**, signed with the same key and the same `kid` as the
identity tokens — which is the property a real deployment relies on, and therefore the one a test has
to reproduce:

```csharp
using var backChannel = host.CreateCookielessClient();   // a back-channel POST carries no cookies

var response = await host.Authority.SendBackChannelLogoutAsync(
    backChannel,
    "/auth/oidc/backchannel-logout",
    new FakeLogoutTokenRequest(host.ClientId, SessionId: "sess-1"));
```

`FakeOidcUser` gains a trailing `SessionId`, published as `sid`, so the login under test is one a
logout token can name precisely.

For the negative cases, `FakeLogoutTokenDefect` asks the authority for a token with exactly **one**
thing wrong with it — a foreign signing key, the wrong audience, a stale `iat`, a forbidden `nonce`, a
missing or mis-keyed `events` claim, an unknown issuer, or neither `sub` nor `sid`. One defect at a
time and never two, because a receiver test has to prove *which* check refused the token; a token that
was both unsigned and stale would satisfy whichever assertion was written and say nothing about the
other rule.

`SendLogoutTokenAsync` posts an already-minted token, which is how a replay test sends the very same
string twice.

## The fake push service

`FakePushService` plays both halves of a Web Push conversation: the push service that answers, and the
browser whose key the message was encrypted to. It has to play both, because asserting on a push means
asserting on something encrypted to a subscription's key, and only the holder of that key can read it.

```csharp
using var push = new FakePushService();

// Post push.Subscription at the registration endpoint exactly as a real client posts what
// pushManager.subscribe() returned, then send a notification through the web-push channel.

var received = push.Requests.Should().ContainSingle().Subject;

received.Payload.Should().Contain("Invoice 4711");     // decrypted, not pattern-matched
received.Vapid!.IsSignatureValid.Should().BeTrue();
```

Decryption succeeding **is** the assertion that the sender followed RFC 8291: a body whose key
derivation, salt placement or record framing is wrong does not produce a mismatched string, it fails to
authenticate. `Vapid` is the token read the way a push service reads it, with the signature verified
against the key the header itself declared — which is the one thing about a VAPID header a hand-written
expectation cannot fake.

`Status` makes the paths that matter reachable: `410` for a browser that has gone, which is the case
the whole subscription lifecycle turns on, plus `413` and `429`; `RetryAfter` rides along with a `429`.
It applies to every push until it is changed, because a test needing a sequence of answers should
change it between the calls it asserts on. `CreateHandler()` returns the `HttpMessageHandler` to give
the application's push client, and the caller owns it.

## Test authentication

`AddBsftTestAuthentication()` (`Bsft.Auth.Testing`) registers the `BsftTest` scheme, which builds a
principal from the current doubles — register the user with `AddBsftTestUser`
too, or use the auth factory, which does both. It is idempotent, and it is guarded: the
handler's constructor throws unless the host is a recognised test environment
(`Testing`, plus any extra names you pass). A stray reference from a production
host fails loudly instead of authenticating everyone.

`Development` is **not** recognised. A deployed host running with
`ASPNETCORE_ENVIRONMENT=Development` is exactly the misconfiguration the guard
exists to catch, and accepting it would authenticate every request as the
default test user. A project that genuinely boots its test host in Development
opts in by name:

```csharp
services.AddBsftTestAuthentication("Development");
```

One nuance for HTTP tests: `TestCorrelationContext` derives from the concrete
`CorrelationContext` because the middleware takes that type, and the fixed id
only survives a request that sends **no** `X-Correlation-Id` header — otherwise
the middleware honours the header. See [correlation.md](correlation.md).

## Driving a background pass by hand

The saga harness is not the only place a test has to stop waiting on a poll
interval. Three further components expose the pass itself, on the same terms:
the seam is registered on the instance the host runs as a background service, so
a pass exercises the real policy, and the count it returns is a diagnostic rather
than an assertion target.

| Seam | Package | One pass is |
| --- | --- | --- |
| `ISagaTimeoutDrainer.DrainDueAsync` | `Bsft.EventAggregator.Saga` | The timers that are due now. |
| `IOutboxProcessor.ProcessOnceAsync` | `Bsft.Messaging`, registered by `AddBsftMessagingEFCore` | One leased batch of the transactional outbox. |
| `ILogoutDeliveryDispatcher.DispatchOnceAsync` | `Bsft.Auth.TokenServer.Sessions` | One claimed batch of back-channel logout deliveries. |

Each drains at most one batch, so a test working through a backlog repeats the
pass until it returns zero. Nothing here starts or stops the hosted services; a
test host that leaves them registered simply races its own drain, so switch them
off or accept that the pass may find the work already done.

The fourth thing a host test usually wants to hold still is the mail itself, and
that is a substitution rather than a pass: register your own
`Bsft.Notifications.Email.ISmtpTransport`, which is public since
1.3.6.1. Replacing the transport leaves the contact resolution, the header
sanitizer and the message builder on the real path; replacing the whole
`INotificationChannel` does not. See `Bsft.Notifications`.

## See also

- [mediator.md](mediator.md), [saga.md](saga.md) — what the harnesses exercise.
- `Bsft.Auth` — the identity and permission model the doubles stand in for.
