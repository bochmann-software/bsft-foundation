# Bsft.Localization

Store-backed translations with a tenant-aware fallback chain, exposed through the standard
`IStringLocalizer`, plus the read endpoints a front end fetches its bundle from. This is the
resolution half of the family; `Bsft.Localization.EFCore` is the store half.

## Install

```bash
dotnet add package Bsft.Localization
```

Requires `Bsft.Data` (the tenant context the chain reads), `Bsft.EventAggregator` (the invalidation
event) and `Bsft.Http` (the entity tags the bundle endpoint serves). It stores nothing itself:
reference `Bsft.Localization.EFCore` as well, or implement `ITranslationReader`.

## Register

```csharp
// Compiled-in defaults, contributed by each module.
services.AddBsftTranslations(seed => seed
    .ForLocale("en", en => en.Add("orders.status.pending", "Pending"))
    .ForLocale("de", de => de.Add("orders.status.pending", "Ausstehend")));

// The runtime: locales, cache, invalidation, endpoints.
services.AddBsftLocalization(options => options.FallbackLocale = "en");

// or bound from configuration, section "Bsft:Localization"
services.AddBsftLocalization(configuration);

// The store half.
services.AddBsftLocalizationEFCore<AppDbContext>();
```

Both registrations are needed. `AddBsftLocalization` does **not** register a translation seed, and
resolving `IStringLocalizerFactory` needs the `TranslationSeed` singleton that only
`AddBsftTranslations` adds — an application that calls only `AddBsftLocalization` fails when the
factory is first resolved. Map the endpoints with `app.MapBsftLocalization()`.

`GET {prefix}/{locale}` serves a language tag the runtime knows and answers anything else with
`400`: the segment picks a cache entry that lives for half an hour, so what a caller may put there
bounds what an anonymous request can make the process hold.

## Configure

`BsftLocalizationOptions.SectionName` is `"Bsft:Localization"`.

| Option | Default | Effect |
| --- | --- | --- |
| `BsftLocalizationOptions.DefaultLocale` | `"en"` | The locale assumed when a request names none. |
| `BsftLocalizationOptions.FallbackLocale` | `"en"` | The locale consulted when the requested one has no match for a key. |
| `BsftLocalizationOptions.RoutePrefix` | `"/api/localization"` | Where `MapBsftLocalization` mounts the three read endpoints. Read once, at map time. |
| `BsftLocalizationOptions.MaxFilterModules` | `32` | Distinct modules one `?modules=` filter may name. |
| `BsftLocalizationOptions.MaxFilterModuleNameLength` | `64` | Length of one module name in that filter. |

Validation: both locales must be non-blank, the route prefix must be an absolute path starting with
`/`, and both filter ceilings must be positive. A failure names the member and the offending value,
and it is thrown from `AddBsftLocalization` as well as on start.

## Extend

`ITranslationReader` (where stored translations come from) is the one seam. Its four reads are the
four questions the resolution chain asks — every key of a locale, every candidate row for one key,
every row of a locale and its fallback, and the distinct locale codes — and each returns a whole
answer the chain then resolves in memory, so a second implementation cannot turn a bundle into a
query per key. `Bsft.Localization.EFCore` registers the shipped implementation with `TryAdd`, so a
host that registered its own keeps it.

## Documentation

Topic page: [docs/localization.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/localization.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
