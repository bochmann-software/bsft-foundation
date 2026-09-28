# Caching

`Bsft.Caching` makes query caching a cross-cutting concern of the mediator pipeline instead of
something every handler implements itself. A query opts in with a marker interface; the pipeline
takes care of reading, writing and evicting.

## Opting a query in

Implement `ICacheable` on the query. Only `CacheKey` is required, the lifetime and the tags have
defaults:

```csharp
public sealed record GetOpenOrders(Guid CustomerId) : IQuery<IReadOnlyList<OrderDto>>, ICacheable
{
    public string CacheKey => CustomerId.ToString();

    public TimeSpan? TimeToLive => TimeSpan.FromMinutes(2);

    public IReadOnlyCollection<string> CacheTags => ["orders"];
}
```

The marker is honoured for **queries only**. A command carrying it — directly or through a shared
base type — still reaches its handler, because serving a command from the cache would drop its
write along with every check the handler performs.

A request without the marker passes through the behavior without even entering an async state
machine. A `null` result is never cached: it cannot be told apart from a miss on read. A
`TimeToLive` of zero or less caches nothing, which is the way to exempt a single request.

## Cache keys and scope

The effective key is
`{KeyPrefix}:{scope length}:{scope}:{type length}:{assembly-qualified request type}:{key length}:{CacheKey}`.
Every variable segment is length-prefixed, so a `:` inside a scope or a discriminator cannot shift
the segment boundary and make two different requests collide on one key. The type name is
assembly-qualified, so two modules declaring the same namespace-qualified query name do not share
an entry.

The scope comes from `ICacheKeyScopeProvider`, as a `CacheKeyScope` with three states:

| Scope | Meaning |
| --- | --- |
| `CacheKeyScope.Global` | Cacheable, qualified by nothing — every caller shares the entries. |
| `CacheKeyScope.For(discriminator)` | Cacheable, qualified by the discriminator, for example the resolved tenant. |
| `CacheKeyScope.NotCacheable` | The request bypasses the cache: no read, no write. |

The default implementation, `GlobalCacheKeyScopeProvider`, returns `Global`. **An application whose
queries are tenant- or user-specific must register a provider that reads its ambient context** —
otherwise every caller shares the same entries.

A provider whose ambient context did not resolve returns `NotCacheable` rather than a substitute
discriminator: requests that share such a substitute share their entries, which is the very leak the
scope exists to prevent. `CacheKeyBuilder` then produces no key at all, and `CachingBehavior` runs the
handler and stores nothing — so an uncacheable scope costs hits, never isolation, and leaves nothing
behind in the store. `default(CacheKeyScope)` is `NotCacheable` for the same reason: an uninitialized
scope disables caching instead of silently caching globally.

The provider and the key builder are registered per scope, so a provider may depend on scoped
request state such as `ICurrentUser` without becoming a captive dependency.

### The tenant scope, and the guard against forgetting it

A multi-tenant host does not have to write that provider. `Bsft.Caching.Data` ships
`TenantCacheKeyScopeProvider`, which folds the tenant `Bsft.Data`'s multi-tenancy resolved into
every key:

```csharp
services.AddBsftDataMultiTenancy(configuration, tenancy => tenancy.ResolveFromClaim());
services.AddBsftCaching(configuration);
services.AddBsftCachingMediator();

services.AddBsftCachingData();   // tenant-scoped cache keys
```

The order of the calls does not matter: `AddBsftCachingData` removes the global default rather than
merely trying past it, and leaves a provider the host registered itself alone. The scope is
`tenant:<id>` while a tenant is resolved (published as `CachingDataDefaults.TenantScopePrefix`) and
`NotCacheable` while none is, so such a request neither reads what a resolved one cached nor writes
anything a later one could read. If the tenant arrives mid-request, caching starts from the call that
sees it.

It is a separate package because neither family may depend on the other: caching would put a cache in
front of every consumer of the data layer, and the data layer would put EF Core in front of every
consumer of the cache.

Leaving the call out is the case that used to fail silently, so `AddBsftDataMultiTenancy` registers a
startup validator against it. When multi-tenancy is configured and the scope provider that wins is
still the global default, it reports a warning on its `StartupValidationResult` naming
`ICacheKeyScopeProvider`, `TenantCacheKeyScopeProvider` and `AddBsftCachingData`, and the startup
validation runner writes it at warning level. It is a warning and not a failure: caching
only tenant-independent lookups is a legitimate configuration, and the validator cannot tell it apart
from the mistake — what it can do is make the mistake loud. Registering any other provider silences
it.

That makes isolation a property of the registration rather than something every query author has
to remember. Everything else that distinguishes one result from another — filter arguments, sort
order, paging — still belongs in `CacheKey`. Keep its cardinality bounded and never build it from
unfiltered user input: every distinct value becomes its own cache entry.

## Registration

```csharp
builder.Services.AddBsftMediator(c => c.RegisterServicesFromAssembly(typeof(Program).Assembly));
builder.Services.AddBsftCaching();
builder.Services.AddBsftCachingMediator();
```

Or bound from configuration, which reads section `Bsft:Caching`:

```csharp
builder.Services.AddBsftCaching(builder.Configuration);
```

### The behavior is its own package

`CachingBehavior<TRequest, TResponse>` ships in `Bsft.Caching.Mediator`, registered by
`AddBsftCachingMediator()`. `AddBsftCaching` registers the store, the key builder, the tag index and
the invalidator, and nothing mediator shaped — so a host that caches by hand through `ICacheStore`
takes no mediator dependency, which a `DependencyClosureTests` in `Bsft.Caching.Tests` holds.

Register the behavior **after the mediator and after every authorization behavior**. The mediator
runs behaviors in registration order, innermost last, so a behavior registered after caching no
longer runs on a cache hit — registering an authorization behavior last would let a hit skip the
permission check. Calling either method more than once registers a single behavior; the options,
however, compose the way the options pattern composes, so the **last** call's delegate wins rather
than the first.

| Option | Default | Purpose |
|---|---|---|
| `Provider` | `CacheProvider.Memory` | `Memory` uses `IMemoryCache`, `Distributed` uses `IDistributedCache`. |
| `DefaultTimeToLive` | 5 minutes | Applied when a request does not specify its own `TimeToLive`. Must be positive. |
| `KeyPrefix` | `bsft` | Keeps the framework's entries apart from other writers on a shared cache, and is enforced on eviction. |

With `CacheProvider.Distributed` the application registers the `IDistributedCache` implementation
itself (`AddStackExchangeRedisCache`, `AddDistributedSqlServerCache`, …). Values are serialized
through `ICacheSerializer`, JSON by default, so a cached result must be serializable, must be a
concrete type — for a base class only the base members survive the round trip, and an interface or
abstract response type cannot be read back at all — and
is returned as a fresh instance on every hit. An entry that can no longer be read, typically after
the response type changed between deployments, is logged, dropped and reported as a miss instead of
failing the request. With `CacheProvider.Memory` the value is handed back as the same object
reference, so only immutable results should be cached.

`BsftCachingOptions` is validated by `ValidateOnStart()`: a non-positive `DefaultTimeToLive`, an
empty `KeyPrefix` and an undefined `Provider` fail the host at startup, all complaints in one
message. Until 1.3.5 the lifetime rule was an `ArgumentOutOfRangeException` thrown by
`AddBsftCaching`, which never saw a value arriving through configuration. The options are resolved
as `IOptions<BsftCachingOptions>`; the raw instance is no longer registered in the container.

Cache keys are built from the scope and the caller's discriminator, so keep personal data out of
them: a key reaches the distributed cache's own key space, and a digest of it appears in the log
when an entry turns out to be unreadable. Query results carrying personal or confidential data
leave the process in cleartext on a distributed cache that is often shared between services. Either keep those queries unmarked, or
register an encrypting decorator around `ICacheSerializer` before calling `AddBsftCaching`, and run
the backing store with transport security and authentication.

## Invalidation

Two ways to evict, both through `ICacheInvalidator`:

```csharp
// The key is what ICacheKeyBuilder.Build produced for the request; resolve the builder rather
// than assembling the string by hand. Build returns null when the ambient scope is not cacheable,
// and InvalidateKeyAsync takes that null as a no-op, so the result can be passed straight through.
await _invalidator.InvalidateKeyAsync(_keyBuilder.Build(new GetOpenOrders(customerId)), ct);
await _invalidator.InvalidateTagAsync("orders", ct);
```

Tag invalidation is the useful one for a command handler: it evicts every entry carrying the tag
without the handler knowing which keys exist, and the evictions run in parallel. Keys are dropped
from the index only after the store confirmed the eviction, and an evicted key is forgotten under
every tag it carried, so the index never points at an entry the store no longer holds. A key that
does not start with the configured `KeyPrefix` is rejected, so an invalidation driven by request
data cannot delete entries another writer owns on a shared cache. A `null` key is not rejected but
ignored: it is what `Build` returns for a scope that is not cacheable, and nothing was stored under
it, so a best-effort invalidation whose tenant did not resolve does not fail the write it follows.

Forgetting is conditional. The invalidator reads each association's stamp before evicting and passes
it back afterwards, so a query that re-caches one of those keys while the eviction is in flight keeps
its fresh association. Dropping it unconditionally would leave the re-cached entry in the store with
no tag pointing at it, out of reach of every later tag invalidation until its lifetime ran out.

A command handler that would rather not reference `ICacheInvalidator` directly can publish an event
instead. `Bsft.Caching.EventAggregator` subscribes a hosted service to the in-process event
aggregator and turns those events into evictions. The events ship in that package rather than in a
contracts-only package, so a publishing handler references `Bsft.Caching.EventAggregator`:

```csharp
builder.Services.AddBsftCacheInvalidation();

await _eventAggregator.PublishAsync(new CacheTagInvalidatedEvent("orders"), ct);
```

## When the cache is unavailable

**A broken cache degrades the application, it does not fail it.** The cache is an optimisation, so
`CachingBehavior` never lets a store outage turn a healthy handler into a failed query. A lookup
that throws — a Redis connection error, a timeout, anything the store raises — is treated as a miss
and the handler runs. A write that throws leaves the handler's result on its way to the caller
unchanged, and the tag index is left untouched, so it never points at a key the store does not hold.
Both are logged at warning level with the request type. This is the same stance the distributed
store already takes towards an entry it cannot deserialize: drop it, report a miss, run the handler.

The practical consequence is that a cache outage shows up as load and latency rather than as errors.
Watch the warning log and the handler's own metrics for it, because the requests themselves keep
succeeding.

**The tag index is inside the same boundary.** `ICacheTagIndex` is a replaceable seam, and a shared
implementation does I/O just like the store, so a throwing `Associate` is logged and the query still
returns its result. The entry then sits in the store carrying no tag: it can still be invalidated by
key and still expires on its own, but `InvalidateTagAsync` will not reach it, so it may serve a stale
result for the rest of its lifetime — the same position an untagged cacheable query is in. The stamp
protocol is unaffected, because an association that was never recorded hands out no stamp and the
conditional forget keeps comparing against associations that really exist.

**A response that cannot be serialized is not an error either.** `ICacheSerializer.Serialize`
reports a value it cannot write as `CacheSerializationException`, which reaches the behavior on the
write path and means the same thing as an unavailable store: this response is not cacheable. The
query still returns its result. Without that the handler would do all of its work and the request
would then fail on the way out, which is the worst of both.

**Cancellation is deliberately not degraded.** If the caller's `CancellationToken` is cancelled
while the store is being read or written, the `OperationCanceledException` propagates instead of
being treated as a cache failure. A cancelled caller asked for exactly that, and quietly falling
through to the handler would run work nobody is waiting for.

## Limits

**Tag invalidation is per instance and per process lifetime.** `AddBsftCaching` registers
`CacheTagIndex`, an in-process and volatile index, for both providers. With `CacheProvider.Memory`
that matches the store exactly. With `CacheProvider.Distributed` it does not: the store is shared
between instances and survives a restart, the index does not. `InvalidateTagAsync` therefore misses
every entry a peer instance wrote, and every entry this instance wrote before its last restart —
those stay served until their own `TimeToLive` expires, even though their tag was invalidated. The
event aggregator being in-process compounds it, since the invalidation event does not reach the peers
either. `InvalidateKeyAsync` is unaffected, because it does not consult the index to find the entry.

Three ways out, in increasing order of effort:

- Keep `TimeToLive` short enough that the missed entries expire within the staleness the domain
  tolerates, and treat tag invalidation as a latency optimisation rather than a guarantee.
- Propagate the invalidation event over an out-of-process transport that republishes it on every
  instance. Each instance then evicts what it cached itself, which covers the peers but still not
  what was cached before a restart.
- Replace the index. `ICacheTagIndex` is the seam, and every service in `AddBsftCaching` is
  registered with `TryAdd`, so an implementation registered first wins:

  ```csharp
  builder.Services.AddSingleton<ICacheTagIndex, RedisCacheTagIndex>();
  builder.Services.AddBsftCaching(o => o.Provider = CacheProvider.Distributed);
  ```

  An implementation has to keep `Associate`, `ListKeys`, `GetStamp` and `Forget` consistent across
  instances, expire associations along with the entries they describe, and hand out stamps that
  increase monotonically across the fleet, so a conditional `Forget` stays correct. That is a
  meaningful piece of work — the framework does not ship one, deliberately, rather than shipping one
  whose atomicity claims it cannot keep.

**Concurrent misses are not coalesced.** N simultaneous requests for the same missing key all run
the handler and all write the result. Where that thundering herd matters — a genuinely expensive
query right after an eviction — the handler needs its own guard.

**A write racing an invalidation can lose it.** An invalidation landing between the handler
returning and the cache write completing is overwritten by that write. `TimeToLive` is therefore
the upper bound on staleness even with active invalidation; choose it accordingly for data that
must not be stale for long.
