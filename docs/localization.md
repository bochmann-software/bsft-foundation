# Bsft.Localization

Store-backed translations with a tenant-aware fallback chain, exposed through
the standard `IStringLocalizer`. Modules seed their own defaults in code; a
tenant overrides them at runtime without a redeploy.

The feature ships as two packages. `Bsft.Localization` owns the resolution
chain, the cache and its invalidation, the seed builder and the read endpoints;
`Bsft.Localization.EFCore` owns the table, its mapping, the reader behind the
chain and the seed applier. The seam between them is `ITranslationReader`.

## The three registrations

They do different jobs and **all three** are needed:

```csharp
// Compiled-in defaults. Contributed by each module.
services.AddBsftTranslations(seed => seed
    .ForLocale("en", en => en
        .Add("orders.status.pending", "Pending"))
    .ForLocale("de", de => de
        .Add("orders.status.pending", "Ausstehend")));

// The runtime: locales, cache, invalidation, endpoints.
services.AddBsftLocalization(options =>
{
    options.DefaultLocale = "en";
    options.FallbackLocale = "en";
});

// The store.
services.AddBsftLocalizationEFCore<AppDbContext>();
```

`AddBsftLocalization` does **not** register a translation seed — resolving
`IStringLocalizerFactory` needs the `TranslationSeed` singleton that only
`AddBsftTranslations` adds. An application that calls only
`AddBsftLocalization` fails when the factory is first resolved. It does not
register a store either: without `AddBsftLocalizationEFCore` (or another
`ITranslationReader`) the first resolution fails on the missing reader.

`AddBsftLocalization` registers the options, a memory cache,
`BsftStringLocalizerFactory`, the reader the bundle endpoints serve out of, the
`IHttpContextAccessor` the chain reads the request's tenant through, the cache
invalidator and its hosted service.

The tenant is read off the request being served. `ITenantContext` is scoped
state that a host resolves onto the request's own scope, so the chain resolves
its store and its tenant out of that scope whenever there is one, and out of a
scope of its own only outside a request — a background job, a hosted service, a
console host.

## Configuration

`BsftLocalizationOptions`, section `Bsft:Localization`:

| Option | Default | Effect |
| --- | --- | --- |
| `DefaultLocale` | `"en"` | Locale assumed when none is requested. |
| `FallbackLocale` | `"en"` | Locale consulted when a key is missing. |
| `RoutePrefix` | `"/api/localization"` | Where `MapBsftLocalization` mounts the read endpoints. Read once, at map time. |
| `MaxFilterModules` | `32` | Distinct modules one `?modules=` filter may name. |
| `MaxFilterModuleNameLength` | `64` | Length of one module name in that filter. |

Every value binds from configuration:

```csharp
services.AddBsftLocalization(configuration);
```

`AddBsftLocalization` validates before registering anything, so a blank locale,
a route prefix that is not an absolute path or a non-positive ceiling fails at
the registration call rather than on the first request; the same rules run again
through `ValidateOnStart`.

The seed builder: `ForLocale(locale, cfg)` opens a locale, and inside it
`Add(key, value)` adds one entry while
`FromEmbeddedJson(assembly, resourcePrefix)` loads a whole embedded resource —
useful once a module has more than a handful of strings.

## Bsft.Localization.EFCore

The satellite that stores translations. It has no options type and no
configuration section: what it registers is decided by the context type.

```csharp
public interface ITranslationDbContext
{
    DbSet<TranslationEntity> Translations { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

`TranslationEntity` carries `Key`, `Locale`, `Value` and a nullable `TenantId`,
where **null means global**. Two indexes, one per read: a unique one over
`(Key, Locale, TenantId)`, which both enforces that a tenant holds exactly one
override per key and locale and answers the single-key lookup, and a second one
over `(Locale, TenantId)` for the whole-locale read a bundle is built from — that
read names no key at all, so the unique index cannot answer it. Apply the shipped
`TranslationEntityConfiguration` in `OnModelCreating`; the framework owns the
mapping, the application owns the schema.

How strictly a key or a locale code is matched is the store's, and the chain never
narrows what the store returned. A store whose collation is case insensitive
resolves `EN` and `en` alike; one whose collation is case sensitive resolves
whichever spelling its rows carry, so such a deployment has to be consistent about
how it spells a locale code.

Seed data reaches the database through an extension on the context:

```csharp
await dbContext.ApplyAsync(seed, ct);
```

It inserts what is missing and never overwrites what is there, always as a
global row, so a tenant's override survives a redeploy. What the store already
holds is read once rather than asked about per entry, and a row already there
counts whatever the spelling of its locale or key. Two instances starting
together race — both read a store without a row, both insert it, and the unique
index refuses the second — so a refused write is answered by reading what the
store holds now and inserting what is still missing. A second refusal is a real
one and is thrown.

### The reader seam

`ITranslationReader`, declared in `Bsft.Localization`, is what the resolution
chain reads through, and `Bsft.Localization.EFCore` is the implementation the
framework ships. It has four synchronous reads, one per question the chain asks:

| Read | Answers |
| --- | --- |
| `ListForLocale(locale, tenantId)` | Every row of that locale the tenant can see: its own rows and the global ones. Behind `GetAllStrings`. |
| `ListForKey(key, locale, fallbackLocale, tenantId)` | Every candidate row for one key, in both locales and both tenant scopes, in one read. Behind the indexer. |
| `ListForLocaleOrFallback(locale, fallbackLocale, tenantId)` | Every row of the locale and of its fallback the tenant can see. Behind the bundle. |
| `ListLocales()` | The distinct locale codes any row belongs to, not narrowed by tenant. Behind the two locale endpoints. |

Each read names a whole answer the chain then resolves in memory. That is what
keeps a bundle to one query rather than one per key, and it is why the seam is
four named reads rather than a general query surface: a second implementation
cannot accidentally lose the property. The reads are synchronous because
`IStringLocalizer` is — its indexer returns a value, not a task.

The reader is registered with `TryAdd`, so a host that registered its own keeps
it and the package contributes only the context binding.

## Resolution order

`BsftStringLocalizer` resolves a key by walking, in order:

1. the tenant's row for the requested locale,
2. the global row (`TenantId == null`) for the requested locale,
3. the compiled-in seed for the requested locale,

then repeats all three under the **fallback** locale, and finally returns the key
itself. A missing translation therefore degrades to a visible key rather than an
empty string or an exception.

## Caching and invalidation

Resolved values are cached for 30 minutes under
`bsft:i18n:{tenantId ?? "global"}:{locale}:{key}`. Only **positive** results are
cached, so adding a translation for a key that was previously missing takes
effect without waiting out a negative-cache entry.

Publishing a `TranslationsUpdatedEvent` (namespace
`Bsft.Localization.Abstractions`) through the event aggregator evicts the
affected entries; `TranslationCacheInvalidator` subscribes, and its hosted
service owns the subscription lifetime. The event carries an optional `Locale`
and `TenantId` to narrow what is dropped.

## Endpoint

```csharp
app.MapBsftLocalization();
```

Maps three read endpoints under the `Localization` tag, below
`BsftLocalizationOptions.RoutePrefix` (`/api/localization` by default): `GET {prefix}`
(available locales), `GET {prefix}/languages` (a flat array) and
`GET {prefix}/{locale}` (the whole bundle as a flat key/value object,
optionally narrowed with `?modules=`). A single-page front end typically fetches
the last one once per session. The prefix is read once, when the routes are
built, so a later change cannot move them.

### Conditional requests

`GET /api/localization/{locale}` carries a strong `ETag` and answers a matching
`If-None-Match` with an empty `304 Not Modified`. A front end that reloads
becomes one conditional round-trip instead of a full bundle download.

```http
GET /api/localization/de
→ 200  ETag: "sha256-9f2c…"  Cache-Control: private, no-cache

GET /api/localization/de   If-None-Match: "sha256-9f2c…"
→ 304  ETag: "sha256-9f2c…"
```

The version is the SHA-256 of the **resolved** bundle — the locale, the tenant
and every key/value pair the fallback chain produced, hashed in a fixed order —
and it is computed once, when the bundle is materialised, and stored in the same
cache entry as the content it describes, under a key composed of the tenant and
the locale rather than joined into a string — no tenant id can be spelled so as
to read another tenant's entry, or the entry of a request that ran in no tenant
at all.

That is the whole invalidation design, and it is deliberately not a second one.
`TranslationCacheInvalidator` already drops that cache when a
`TranslationsUpdatedEvent` is published; the version goes with the content it
sits beside, and the next request re-materialises both from the database. A
version that had its own eviction path would have to be kept in step with the
content's, and the failure mode of getting that wrong is a client pinned to a
stale bundle by a tag that never moves. The corollary is honest and worth
stating: a write that does **not** publish the event does not move the tag
either, exactly as it does not currently invalidate the per-key cache.

The bundle is now memoised as a whole, where before every request re-resolved
it. Its lifetime is the same 30 minutes the per-key entries use.

### What counts as a locale

`{locale}` has to be a language tag the deployment's runtime knows, such as `de`
or `de-CH`; anything else is answered `400` without reading the store. The
segment decides what is resolved and what is then held in the memory cache for
half an hour, so without the check an anonymous caller fills the cache with as
many copies of the fallback bundle as it cares to invent spellings. The shape
bounds how long a tag may be, and asking the runtime whether it carries the
culture bounds how many there can be. A host running with invariant
globalization has no such list and keeps the shape bound alone, which is the
trade that mode makes everywhere else too.

### Module-scoped bundles

A front end that only mounted two modules does not need the translations of
every other one. `?modules=` narrows the bundle:

```http
GET /api/localization/de?modules=Orders,Inventory
→ 200  ETag: "sha256-41ab…"
```

The narrowed view contains two things. The **module keys**: everything under
`modules.{Name}.` for each module named, which is the convention
[modules.md](modules.md) already states for a module's own strings. And the
**shared keys**, defined as everything that is not under `modules.` at all —
`common.save`, `errors.notFound`, a bare `greeting`. A key outside the module
namespace belongs to the application rather than to any one module, and a front
end needs it whichever screen it opened, so sharing is the default and being
module-scoped is the exception. One consequence is worth stating: a key directly
under `modules.` with no further segment, `modules.Orders`, names no module by
the convention and is therefore neither shared nor owned, so it appears in no
narrowed view.

Without the parameter the endpoint is unchanged, down to the entity tag it
serves. A parameter that normalises to nothing — `?modules=`, `?modules=,,` —
is the same as not passing it: naming no module is not a narrowing. A module
nothing is keyed under is not an error either; it simply contributes no keys.
Answering it would turn the endpoint into an oracle for which modules a tenant
has installed, and would make a front end's whole request fail because one
module was disabled instead of degrading to the keys it can still show.

The parameter is bounded, because it is caller-supplied and decides how much
work one request costs: at most `MaxFilterModules` distinct modules, each at
most `MaxFilterModuleNameLength` characters, each made only of ASCII letters,
digits, `-` and `_`. A filter that breaks a bound is answered `400` with the
reason. The character set is deliberately narrower than what a translation key
may hold — a name reaches both a cache key and an entity tag, and the convention
segment it stands for is one dot-free name, so a dot that would select across
segments is not admitted.

#### The narrowed tag

The module names are normalised before anything is keyed on them: trimmed,
upper-cased invariantly, de-duplicated and ordinal-sorted. `?modules=A,B`,
`?modules=B,A` and `?modules=b,%20a,B` are therefore one selection with one
entity tag. Upper-casing is a canonical form rather than a
comparison choice — the resolved bundle compares keys ordinal-ignore-case, so
the case a caller happened to type must not reach the tag.

The narrowed view is **cut from the memoised full bundle** on the way out, not
resolved or memoised in its own right, and that single decision settles three
things.

Invalidation needs nothing new. The only thing memoised is the full bundle, and
`TranslationCacheInvalidator` already drops it on `TranslationsUpdatedEvent`, so
a narrowed view moves with it by construction. There is no second eviction path
to keep in step with the first — the failure mode this section warns about
above, and here it simply does not exist. The corollary from the unfiltered
bundle carries over unchanged: a write that does not publish the event does not
move a narrowed tag either.

Consistency needs nothing new. A narrowed response cannot disagree with the
unfiltered one about a key both contain, because one resolution of the fallback
chain stands behind both.

And the cost is small. The narrowed version is hashed over the full bundle's
version plus the module names, not over the narrowed content, so the tag is
known *before* anything is narrowed: a revalidation costs a hash of a few dozen
bytes and never builds the view it stands in for. Serving a narrowed body costs
one pass of prefix comparisons over the bundle's keys, which is dwarfed by
serialising the result.

Memoising the narrowed views themselves was considered and dropped. It would
have saved one dictionary rebuild per served request — never a database query,
which the full bundle already absorbs, and never a hash of the locale, which
this design does not do in the first place — in exchange for cache entries whose
keys the caller chooses, which is a flooding surface the ceilings above would
then have to be tight enough to close. Materialising filtered bundles separately
from the database was dropped for a stronger reason: a second query per filter,
a second hash of the whole locale, and an invalidation path that could drift out
of step with the first.

The locale and the tenant are hashed alongside the content rather than only
selecting the cache entry. The URL names the locale but never the tenant, so a
tag built out of the content alone would be shared by two tenants whose
translations happen to agree, and a client that switched tenant mid-session
could be told 304 for a bundle it is not holding.

`private, no-cache` means revalidate, never share. The bundle depends on the
tenant of the request, which is not in the URL, so a proxy or CDN caching by URL
would serve one tenant's translations to another; `no-cache` still lets the
client keep its copy, which is what makes the entity tag worth serving.
[http.md](http.md) has the full reasoning and the primitives.

## Example

A module contributes its own strings, keyed by the module convention
`modules.{Name}.displayName` and `modules.{Name}.description`:

```csharp
public void ConfigureServices(IServiceCollection services, IConfiguration config)
{
    services.AddBsftTranslations(seed => seed
        .ForLocale("en", en => en
            .Add("modules.Orders.displayName", "Orders")
            .Add("modules.Orders.description", "Order management module"))
        .ForLocale("de", de => de
            .Add("modules.Orders.displayName", "Bestellungen")
            .Add("modules.Orders.description", "Bestellverwaltung")));
}
```

Consumption is the standard ASP.NET Core abstraction:

```csharp
public sealed class OrderPresenter(IStringLocalizer localizer)
{
    public string StatusLabel(string status) => localizer[$"orders.status.{status}"];
}
```

## See also

- [modules.md](modules.md) — the `modules.{Name}.*` key convention.
- [event-aggregator.md](event-aggregator.md) — how the invalidation event travels.
