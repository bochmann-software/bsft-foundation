# Bsft.Data.Postgres

The PostgreSQL half of the `Bsft.Data` search family: it turns a declared search group into a
`tsvector` match against a GIN-indexed generated column, ranked by PostgreSQL's own `ts_rank`, and
gives an application's migrations the DDL that builds that column.

## Install

```bash
dotnet add package Bsft.Data.Postgres
```

Requires `Bsft.Data` (pulled transitively; it declares the search groups and validates them) and
`Npgsql.EntityFrameworkCore.PostgreSQL`.

## Register

```csharp
services.AddBsftDataPostgres(options => options
    .HasFullTextIndex("candidates")
    .UseDefaultLanguage("de")
    .UseTextSearchConfiguration("de", "german_unaccent"));

// or hand the same declaration to the container and to a migration
var search = new BsftDataPostgresOptions().HasFullTextIndex("candidates").UseDefaultLanguage("de");
services.AddBsftDataPostgres(search);
```

The application's migration creates the column and the index with
`migrationBuilder.AddFullTextVector(command, search).CreateFullTextIndex(command)`. That is the point
of the second overload: the generated column bakes its text search configuration in as a literal, so
the index and the queries against it have to name the same one, and one instance stating it is what
makes that structural rather than a convention two files apart. The container receives a copy, so a
mutation after the call cannot make the two disagree.

## Configure

`BsftDataPostgresOptions` has no `SectionName` and binds from no configuration section — see the
paragraph below the table.

| Option | Default | Effect |
| --- | --- | --- |
| `BsftDataPostgresOptions.HasFullTextIndex(group, vectorProperty)` | `vectorProperty` = `"SearchVector"` | Declares that a group is backed by a `tsvector` column, and which entity property maps it. |
| `BsftDataPostgresOptions.UseDefaultLanguage(language)` | `"en"` | The stemming language an index is built with when its command names none. |
| `BsftDataPostgresOptions.UseTextSearchConfiguration(code, name)` | the six built-in codes `de, en, es, fr, it, nl` | Maps a language code to a `regconfig` of the deployment's own, such as an `unaccent`-composed `german_unaccent`. A registration wins over the built-in mapping for the same code. |

Validation: a group name, a `tsvector` property name and a language code must not be blank, and a
registered configuration name must be a plain SQL identifier — it is interpolated into the generated
column's DDL as a literal, so a name needing escaping is rejected rather than escaped. Beyond that,
`SearchStartupValidator` in `Bsft.Data` fails startup for a searchable group this package has no
index declared for, and for one whose language maps to no text search configuration.

Every member of this type describes the **schema** — which column a migration generated and which
configuration was baked into it. A deployment that could state them in a settings file could state
them differently from the index it actually has, and the result would be queries stemmed by one
configuration against an index built with another: a search quietly answering a narrower question
than it was asked. The declaration therefore lives beside the migration that makes it true, and there
is deliberately no `IConfiguration` overload.

## Extend

`ISearchProvider` is the seam, and `PostgresSearchProvider` is this package's implementation of it;
it is contributed with `TryAddEnumerable`, so a host may register providers for more than one
database and `Bsft.Data` will validate each. Nothing else here is replaceable — the language mapping
and the DDL are how this package talks to PostgreSQL, not a decision a caller makes.

## Documentation

Topic page: [docs/search.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/search.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
