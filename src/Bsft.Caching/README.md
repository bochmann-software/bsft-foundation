# Bsft.Caching

A memory or distributed cache store with the key builder, the tag index and the invalidator that
make an entry findable again when the data behind it changes. The mediator pipeline behavior that
serves cacheable queries from it ships in `Bsft.Caching.Mediator`.

## Install

```bash
dotnet add package Bsft.Caching
```

Requires `Microsoft.Extensions.Caching.Memory` and the usual `Microsoft.Extensions.*` abstractions. No further
Bsft packages. With `CacheProvider.Distributed` the application registers its own `IDistributedCache`;
this package does not pick one for you.

## Register

```csharp
services.AddBsftCaching(options =>
{
    options.DefaultTimeToLive = TimeSpan.FromMinutes(10);
    options.KeyPrefix = "okej";
});

// or bound from configuration, section "Bsft:Caching"
services.AddBsftCaching(configuration);
```

Nothing else is required for the memory provider, which is the default. `CacheProvider.Distributed`
takes one more line, because the store reads through an `IDistributedCache` this package does not
choose:

```csharp
services.AddStackExchangeRedisCache(redis => redis.Configuration = "…");
services.AddBsftCaching(options => options.Provider = CacheProvider.Distributed);
```

To cache mediator queries, add `Bsft.Caching.Mediator` and call
`AddBsftCachingMediator()` after `AddBsftMediator` and after every authorization behavior: the
mediator runs behaviors in registration order, innermost last, so a behavior registered **after**
the cache no longer runs on a hit. A query opts in by implementing `ICacheable`; a command carrying
the marker is still dispatched to its handler.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| `BsftCachingOptions.Provider` | `CacheProvider.Memory` | Which cache is written to. `Distributed` requires the host to register an `IDistributedCache`. Read once at registration, because it decides which store type is registered. |
| `BsftCachingOptions.DefaultTimeToLive` | `00:05:00` | The lifetime applied to an entry whose request does not state one of its own through `ICacheable.TimeToLive`. |
| `BsftCachingOptions.KeyPrefix` | `bsft` | The prefix every key starts with, which is what keeps this framework's entries apart from other writers on a shared distributed cache. |

Validation: `ValidateOnStart()` rejects a non-positive `DefaultTimeToLive`, an empty `KeyPrefix` and a
`Provider` that is not a defined value — all of them reported in one message. Before 1.3.5 the first of
these was an `ArgumentOutOfRangeException` thrown by `AddBsftCaching`, which never saw a value that
arrived through configuration.

## Extend

Every seam is registered with `TryAdd`, so registering your own before `AddBsftCaching` wins.

`ICacheStore` — reads and writes entries. `MemoryCacheStore` and `DistributedCacheStore` ship;
which one is registered follows `Provider`.
`ICacheTagIndex` — records which keys carry which tag. The shipped `CacheTagIndex` is in-process and
volatile **for either provider**, so on a distributed cache `InvalidateTagAsync` evicts only what this
instance cached since it started. An application that needs tag invalidation to reach the whole fleet
replaces it with one over shared storage; that is the single most likely replacement here.
`ICacheKeyBuilder` — composes the key from prefix, scope, request type and discriminator, or returns
`null` when the scope is `CacheKeyScope.NotCacheable`.
`ICacheKeyScopeProvider` — supplies the ambient scope every key is qualified with, as a
`CacheKeyScope`: `Global`, `For(discriminator)`, or `NotCacheable` when the ambient context did not
resolve and the request must therefore bypass the cache entirely — no read and no write. The shipped
`GlobalCacheKeyScopeProvider` returns `Global`; a multi-tenant host replaces it so that one tenant
cannot be served another's result, most simply by referencing `Bsft.Caching.Data` and calling
`AddBsftCachingData()`.
`ICacheSerializer` — turns a value into bytes for the distributed store; `JsonCacheSerializer` ships.
`ICacheInvalidator` — evicts by key or by tag, and forgets the association afterwards.

## Documentation

Topic page: [docs/caching.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/caching.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
