# Bsft.Caching.EventAggregator

The adapter between the `Bsft.Caching` and `Bsft.EventAggregator` families: a hosted subscriber that
evicts cached query results when an invalidation event is published in-process, so a write handler can
invalidate without taking a reference on the cache.

## Install

```bash
dotnet add package Bsft.Caching.EventAggregator
```

Requires `Bsft.Caching` and `Bsft.EventAggregator`, both of which must already be registered — this
package supplies no cache and no aggregator of its own.

## Register

```csharp
services.AddBsftCaching();
services.AddBsftEventAggregator();
services.AddBsftCacheInvalidation();
```

Nothing else is required. Publish `CacheKeyInvalidatedEvent` or `CacheTagInvalidatedEvent` through
`IEventAggregator` and the subscriber calls `ICacheInvalidator` for you. The subscriber is an
`IHostedService`, so it only runs where a host runs it.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| — | — | The package has no options type: what it registers is a single subscriber with nothing to tune. What gets evicted is decided by the event, and how far the eviction reaches is decided by `BsftCachingOptions` and by which `ICacheTagIndex` is registered. |

Validation: none — there is nothing to configure.

## Extend

None. The two events are the surface: publish one and the eviction happens. Replace `ICacheInvalidator`
in `Bsft.Caching` to change what an eviction does, and `ICacheTagIndex` to change how far a tag
invalidation reaches — an in-process index bounds it to this instance, which is the trap worth knowing
about before wiring this up on a distributed cache.

## Documentation

Topic page: [docs/caching.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/caching.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
