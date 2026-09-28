# Bsft.Data.Sqlite

The SQLite half of the `Bsft.Data` search family: it turns a declared search group into an FTS5
`match`, ranked by `bm25`, joined onto the caller's queryable, and gives an application's migrations
the DDL that creates the FTS5 table and the triggers that keep it in step.

## Install

```bash
dotnet add package Bsft.Data.Sqlite
```

Requires `Bsft.Data` (pulled transitively; it declares the search groups and validates them) and
`Microsoft.EntityFrameworkCore.Sqlite`.

## Register

```csharp
services.AddBsftDataSqlite(options => options
    .HasFullTextIndex("candidates", "Candidates_fts")
    .UseTokenizer(SqliteTokenizer.Unicode61)
    .AcceptsUnstemmedLanguage("de"));

// or hand the same declaration to the container and to a migration
var search = new BsftDataSqliteOptions().HasFullTextIndex("candidates", "Candidates_fts");
services.AddBsftDataSqlite(search);
```

Two more things are needed. The application's migration creates the index with
`migrationBuilder.CreateFullTextIndex(command, search).CreateFullTextSyncTriggers(command)`, and its
`OnModelCreating` maps the index table with `modelBuilder.HasBsftFullTextIndex(table, columns…)` —
an FTS5 index *is* a table, and EF Core can only compose a query against a table it knows. The
container receives a copy of the declaration, so a mutation after the call cannot make the provider
and the migration disagree about the same index.

## Configure

`BsftDataSqliteOptions` has no `SectionName` and binds from no configuration section — see the
paragraph below the table.

| Option | Default | Effect |
| --- | --- | --- |
| `BsftDataSqliteOptions.HasFullTextIndex(group, indexTable)` | no group is indexed | Declares that an FTS5 index exists for a search group, and which table holds it. |
| `BsftDataSqliteOptions.UseTokenizer(tokenizer)` | `SqliteTokenizer.Unicode61` | The FTS5 tokenizer every index is built with. `Porter` adds English-only stemming. |
| `BsftDataSqliteOptions.UseRemoveDiacritics(level)` | `2` | How far accents are folded before terms are stored: `0` folds nothing, `1` the Latin-1 range, `2` everything Unicode defines a folding for. The framework's `2` is what makes `muller` find `müller`. |
| `BsftDataSqliteOptions.AcceptsUnstemmedLanguage(language)` | nothing accepted | Accepts out loud that a declared language is searched without stemming. FTS5 ships one stemmer and it is English. |

Validation: a group name and an FTS5 table name must not be blank, the folding level must be 0, 1 or
2, and the tokenizer must be a declared value. Beyond that, `SearchStartupValidator` in `Bsft.Data`
fails startup for a searchable group this package has no index declared for, and for one whose
language the chosen tokenizer cannot stem unless `AcceptsUnstemmedLanguage` accepted it.

Every member of this type describes the **schema**: the tokenizer and the folding level are compiled
into the FTS5 table when the migration creates it, so changing one means rebuilding the index rather
than restarting the process. A deployment that could state them in a settings file could state them
differently from the index it actually has, and the provider would then certify stemming for an index
that has none. The declaration therefore lives beside the migration that makes it true, and there is
deliberately no `IConfiguration` overload.

## Extend

`ISearchProvider` is the seam, and `SqliteSearchProvider` is this package's implementation of it; it
is contributed with `TryAddEnumerable`, so a host may register providers for more than one database
and `Bsft.Data` will validate each. Nothing else here is replaceable — the tokenizer mapping and the
DDL are how this package talks to FTS5, not a decision a caller makes.

## Documentation

Topic page: [docs/search.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/search.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
