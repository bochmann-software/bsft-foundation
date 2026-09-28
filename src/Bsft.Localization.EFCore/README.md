# Bsft.Localization.EFCore

EF Core persistence for the `Bsft.Localization` family: the translations table behind
`ITranslationDbContext`, its mapping, the reader the resolution chain reads through, and the seed
applier that writes a module's compiled-in defaults into the database.

## Install

```bash
dotnet add package Bsft.Localization.EFCore
```

Requires `Bsft.Localization` (pulled transitively) and `Microsoft.EntityFrameworkCore`.

## Register

```csharp
services.AddBsftLocalizationEFCore<AppDbContext>();
```

`AppDbContext` implements `ITranslationDbContext`, and its `OnModelCreating` applies
`TranslationEntityConfiguration`. Without that configuration the entity still maps by convention but
the unique index over key, locale and tenant is silently absent — and that index is the only thing
stopping one tenant from holding two overrides of one key, which the chain would then pick between by
whatever order the provider happened to return. The configuration adds a second, non-unique index
over locale and tenant, which is what the whole-locale read behind a bundle seeks on — that read names
no key, so the unique index cannot answer it. This repository ships no migrations, so generate one
against your own context.

Seed data reaches the database through an extension on the context, typically at startup:

```csharp
await dbContext.ApplyAsync(seed, ct);
```

It inserts what is missing and never overwrites what is there, always as a global row
(`TenantId` null), so a tenant's override survives a redeploy. It reads what the store holds once
rather than asking per entry, counts a stored row as the seed's row whatever the spelling of its
locale or key, and answers the unique index refusing a row a second instance inserted first by
inserting what is still missing — once.

## Configure

None. The package has no options type and no configuration section: what it registers is decided by
the context type, which is a compile-time choice. Every tunable of the family — the locales, the
route prefix, the filter ceilings — belongs to `BsftLocalizationOptions` in `Bsft.Localization`.

| Option | Default | Effect |
| --- | --- | --- |
| None. | | |

Validation: none of its own.

## Extend

`ITranslationDbContext` is the seam onto the application's own context. `ITranslationReader` (declared
in `Bsft.Localization`) is registered here with `TryAdd`, so a host that registered its own reader
keeps it and this package contributes only the context binding.

## Documentation

Topic page: [docs/localization.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/localization.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
