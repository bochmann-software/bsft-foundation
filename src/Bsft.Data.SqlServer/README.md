# Bsft.Data.SqlServer

The SQL Server half of the `Bsft.Data` search family: it turns a declared search group into
`CONTAINS` / `FREETEXT` predicates composed onto the caller's queryable, and gives an application's
migrations the DDL that builds the full-text catalogue and index behind them.

## Install

```bash
dotnet add package Bsft.Data.SqlServer
```

Requires `Bsft.Data` (pulled transitively; it declares the search groups and validates them) and
`Microsoft.EntityFrameworkCore.SqlServer`.

## Register

```csharp
services.AddBsftDataSqlServer(options => options.HasFullTextIndex("candidates"));

// or hand the same declaration to the container and to a migration
var search = new BsftDataSqlServerOptions().HasFullTextIndex("candidates");
services.AddBsftDataSqlServer(search);
```

The application's migration creates the catalogue and the index with the
`FullTextMigrationBuilderExtensions` helpers. The container receives a copy of the declaration, so a
mutation after the call cannot change what the provider answers for.

## Configure

`BsftDataSqlServerOptions` has no `SectionName` and binds from no configuration section — see the
paragraph below the table.

| Option | Default | Effect |
| --- | --- | --- |
| `BsftDataSqlServerOptions.HasFullTextIndex(group)` | no group is indexed | Declares that a full-text index exists for a search group. |

Validation: a group name must not be blank. Beyond that, `SearchStartupValidator` in `Bsft.Data`
fails startup for a searchable group this package has no index declared for — the deliberate
alternative to silently scanning the table — and for one whose language maps to no locale identifier,
because a query without a `LANGUAGE` clause is stemmed by whatever the index happens to default to and
nothing in the results says so.

What this type holds is a statement about the **schema**: that a full-text index exists. A deployment
that could state it in a settings file could state it wrongly, and every search on that group would
become the table scan the declaration exists to prevent. It therefore lives beside the migration that
creates the index, and there is deliberately no `IConfiguration` overload.

## Extend

`ISearchProvider` is the seam, and `SqlServerSearchProvider` is this package's implementation of it;
it is contributed with `TryAddEnumerable`, so a host may register providers for more than one
database and `Bsft.Data` will validate each. The relevance score is a weighted field-match score
rather than SQL Server's own `CONTAINSTABLE` rank, for the reason `docs/search.md` gives; that is not
a replaceable seam.

## Documentation

Topic page: [docs/search.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/search.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
