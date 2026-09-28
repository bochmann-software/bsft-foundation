# Bsft.Caching.Data

Folds the current tenant into every cache key, so a cacheable query answered for one tenant is never
served to another, and bypasses the cache entirely while no tenant is resolved. It is the caching
family's bridge to `Bsft.Data`'s multi-tenancy.

## Install

```bash
dotnet add package Bsft.Caching.Data
```

Requires `Bsft.Caching` for the key builder that reads the scope and `Bsft.Data` for the tenant
context (both pulled transitively). It is a separate package so that neither family depends on the
other: a single-tenant host caches without the data layer, and a multi-tenant host that caches
nothing takes no cache.

## Register

```csharp
services.AddBsftDataMultiTenancy(configuration, tenancy => tenancy.ResolveFromClaim());
services.AddBsftCaching(configuration);
services.AddBsftCachingMediator();

services.AddBsftCachingData();   // tenant-scoped cache keys
```

Nothing else is required, and the order of the calls does not matter: the tenant-blind default
`GlobalCacheKeyScopeProvider` is removed rather than merely tried past. Leaving the call out of a
multi-tenant host is reported as a startup warning by `Bsft.Data`.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| `CachingDataDefaults.TenantScopePrefix` | `tenant:` | The prefix a resolved tenant's scope carries, followed by the tenant identifier. |

Validation: none — the package has nothing a deployment states.

## While no tenant is resolved

The provider then reports `CacheKeyScope.NotCacheable`, and `CachingBehavior` runs the request
through its handler without reading or writing the cache. It does not fall back to a substitute
scope: every request that failed to resolve would share it, and one of them may still produce a
tenant-specific result — a host authenticating through another mechanism, a header dropped in front
of the app, a caller omitting it on purpose — which the next unresolved request would then be served.
The cost is the missed hit for such requests; nothing is written, so there is no shared bucket and no
garbage in the store. Within one request the tenant may still arrive late: caching starts from the
call that sees it.

## Extend

`ICacheKeyScopeProvider` (what every cache key is qualified with; this package supplies the tenant).
A host that also caches per user registers its own provider folding both in: this package adds with
`TryAdd`, so a provider registered before it wins. A provider of your own returns
`CacheKeyScope.NotCacheable` for the same reason whenever its context did not resolve.

## Documentation

Topic page: [docs/caching.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/caching.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
