# Bsft.Caching.Mediator

The behavior package of the `Bsft.Caching` family: a mediator pipeline behavior that serves
cacheable queries from the configured cache store and writes the handler's result back on a miss.

## Install

```bash
dotnet add package Bsft.Caching.Mediator
```

Requires `Bsft.Caching` and `Bsft.Mediator.Abstractions` (both pulled transitively). It exists as
its own package so that a host caching by hand — through `ICacheStore` and `ICacheInvalidator` —
takes no mediator dependency at all.

## Register

```csharp
services.AddBsftCaching();
services.AddBsftCachingMediator();
```

Then mark the queries whose results may be cached:

```csharp
public sealed record GetProductQuery(Guid Id) : IQuery<Product>, ICacheable
{
    public string CacheKey => Id.ToString();
}
```

Nothing else is required. Call this after `AddBsftMediator` and after every authorization behavior:
the mediator runs behaviors in registration order, innermost last, so a behavior registered after
this one no longer runs on a cache hit. Calling it twice registers a single behavior.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| — | — | The package has no options type. Lifetime and key prefix come from `BsftCachingOptions` (section `Bsft:Caching`) in `Bsft.Caching`, and per-request lifetime and tags come from `ICacheable`. |

Validation: none here — `Bsft.Caching` validates the options both packages read.

## Extend

None of its own. What the behavior does is decided by the seams of `Bsft.Caching`:
`ICacheStore` (where entries live), `ICacheKeyBuilder` and `ICacheKeyScopeProvider` (how a key is
composed and scoped) and `ICacheTagIndex` (which keys a tag reaches). Replace them there.

A command carrying `ICacheable` is never served from the cache, a null result and a non-positive
lifetime are not written, and a store or tag index that cannot be reached degrades the request to an
uncached one rather than failing it.

## Documentation

Topic page: [docs/caching.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/caching.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
