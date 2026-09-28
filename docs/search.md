# Full-Text Search

`Bsft.Data` deliberately does not build SQL strings. Its `contains` filter compiles to
`LOWER(COALESCE(col,'')) LIKE '%…%'`, which is correct but non-sargable: every search is a table
scan. That is the defect the typed filter engine was built to prevent.

The business need for a contains-style search does not go away, though. Searching master data or
candidate profiles by fragments of names and free text is a normal requirement, and without a
supported way to do it, application developers reach for exactly the leading-wildcard `LIKE` the
framework is trying to avoid. This is that supported way.

## Declaring what is searchable

Search is declared in the same place as filters and sorting, so a module keeps one description of
its query surface:

```csharp
public class CandidateQueryConfig : QueryConfigurationBase<Candidate, CandidateDto>
{
    public override void Configure(QueryConfigurationBuilder<Candidate, CandidateDto> builder)
    {
        builder.Field(d => d.Name).AllowFilter(FilterOperator.Eq).AllowSort();
        builder.Field(d => d.Id).AllowSort();
        builder.DefaultSort(d => d.Id, SortDirection.Asc);

        builder.Searchable("profile", s => s
            .Field(e => e.Name, weight: 2.0)
            .Field(e => e.Notes)
            .Language("de")
            .Mode(SearchMode.FreeText));
    }
}
```

A search field names an **entity column**, not a DTO property. A full-text index exists on a column,
and naming the column here keeps the declaration honest about what has to be indexed.

## Searching

```csharp
var request = new PagedRequest(Filter: activeOnly, Page: 1, PageSize: 25)
{
    Search = new SearchRequest("profile", "müller"),
};

PagedResult<RankedItem<Candidate>> page = await _search.SearchAsync(_db.Candidates, request, ct);
```

`Search` is an init-only property rather than a positional parameter, so an existing
`PagedRequest(...)` call and any deconstruction of it keep working unchanged.

Search, filter, sort and paging compose. The search narrows the set, the filter narrows it further,
paging applies to the combination. Results carry a relevance `Score`; set `IsOrderedByRank: false` on
the `SearchRequest` to use the request's sort criteria instead.

Rank ordering is broken by a deterministic tie-breaker — the configured `DefaultSort` field, or the
first searchable field when none is configured — so a hit does not jump between pages. Configure a
unique default sort for a strictly stable order.

With `IsOrderedByRank: false` and no sort on the request, the configured `DefaultSort` orders the
result, exactly as it does for a plain query. Paging an unordered result is decided by the query
plan, so the same row can come back on two pages and another on none.

## Scoping is not optional

Results are filtered by tenant and organisational unit **before** the search provider sees the
queryable, and this happens in the pipeline, not in the caller:

```csharp
public sealed class OrganizationSearchScopeProvider : ISearchScopeProvider
{
    public async Task<SearchScopeState> GetScopeAsync(CancellationToken ct = default)
    {
        var units = await _access.GetUnitsWithPermissionAsync(_user.Id, "candidates.read", ct);
        return new SearchScopeState(_tenant.TenantId, units);
    }
}
```

An entity participates by implementing `ITenantEntity` and/or `IOrganizationScopedEntity`.

Register the provider **scoped**. `ISearchExecutor<TEntity, TDto>` is registered scoped for the same
reason: the provider derives the boundary from request state — the ambient `ITenantContext`, the
signed-in principal — so it has to run in the caller's request scope. Resolved anywhere else it sees
a blank request whose tenant was never set, and every scoped search fails.

**Scoping fails closed.** A group whose entity carries neither marker, or a scoped group with no
registered `ISearchScopeProvider`, fails startup validation and refuses to execute at runtime. A
forgotten DI registration must not read as "search everything". Data that genuinely has no boundary
says so:

```csharp
builder.Searchable("public-catalogue", s => s.Field(e => e.Title).Unscoped());
```

`Unscoped()` is accepted **only** on an entity that carries neither marker. It declares that the data
has no boundary; it is not a switch that removes one. A group declared `Unscoped()` on an entity
implementing `ITenantEntity` or `IOrganizationScopedEntity` fails startup validation, because the two readings
of that declaration are both bad: honoured, one builder call turns off tenant isolation for the
group — and the framework's own error messages used to nudge people towards adding it; ignored, the
group returns results silently narrower than it promises. The boundary a scoped entity carries is
never negotiable from the search declaration. Data that genuinely has no boundary lives on an entity
that has none.

The runtime checks the answer too, not just the registration. A scope provider that returns a
`SearchScopeState` leaving `TenantId` or `AllowedOrganizationUnitIds` unset for an entity that carries the matching
marker fails the request. Startup validation cannot catch that one — the scope is resolved per
call — and a provider that forgets a dimension looks exactly like one that deliberately allows
everything.

`Bsft.Data` does not reference `Bsft.Organization` — the seam keeps the dependency direction intact,
the same way `IAuditCorrelationAccessor` does for the audit package.

## No index, no start

Startup validation fails when a declared group has no index behind it, when no `ISearchProvider` is
registered, when more than one is registered (the executor and the validator would otherwise disagree
about which one is in charge), when a scoped group has nothing to scope it, when an `Unscoped()`
group sits on an entity that carries a scoping marker, or when the group's
stemming language is one the provider cannot map. There is no fallback path: without a provider there
is no way to execute a search at all. Those failure modes are unreachable by accident, which is the
entire point.

A group name has to be unique across the application. A provider declares its indexes by name alone,
so the same name on two entities would let one `HasFullTextIndex("profile")` certify a table that has
no index at all — validation passing and the first real search failing in production.

## Choosing a provider

`ISearchProvider` is the pluggable seam, and the declaration above is engine-agnostic: the same
`Searchable(...)` block is served by any of the three packages. What differs is what the engine can
actually do, and that is worth reading before picking one.

| | `Bsft.Data.SqlServer` | `Bsft.Data.Sqlite` | `Bsft.Data.Postgres` |
| --- | --- | --- | --- |
| Registration | `AddBsftDataSqlServer` | `AddBsftDataSqlite` | `AddBsftDataPostgres` |
| Index | full-text catalogue + index | FTS5 external-content table | generated `tsvector` + GIN |
| Kept in step by | the engine, asynchronously | migration-created triggers | the engine, synchronously |
| Prefix mode | `CONTAINS "müller*"` | `match()`, `"müller"*` | `to_tsquery`, `müller:*` |
| Free text | `FREETEXT` | `match()`, words ORed | `websearch_to_tsquery` |
| Ranking | weighted field match | real `bm25`, field weights | real `ts_rank`, field weights |
| German stemming | yes (LCID 1031) | **no** — see below | yes (`german`) |
| Field weights | any number of fields | any number of fields | at most four (labels A–D) |
| Migration transaction | suppressed (engine refuses) | kept | kept |
| Verified against a live engine | no | **yes**, end to end | no |

Register exactly one. Two registered providers fail startup validation rather than letting the
executor and the validator disagree about which one is in charge.

Each provider package has one primary options type — `BsftDataSqlServerOptions`,
`BsftDataSqliteOptions`, `BsftDataPostgresOptions` — and none of the three carries a `SectionName` or
binds from configuration, which is deliberate and the one place these packages depart from the
options rule in [package-conventions.md](package-conventions.md). Every member of them describes the
schema: which column or table the migration created, which text search configuration was baked into a
generated column, which tokenizer an FTS5 table was built with. A deployment able to state those
separately would be able to state them differently from the index it actually has, and the result is
the quiet degradation this whole feature exists to remove — a search answering a narrower question
than it was asked, with nothing in the results saying so. The declaration therefore lives beside the
migration that makes it true. Registration still goes through the options pattern
(`AddOptions<T>().Configure(...)`), and each provider reads its declaration through `IOptions<T>`.

## SQL Server provider

```csharp
services.AddBsftDataSqlServer(o => o.HasFullTextIndex("profile"));
```

`Bsft.Data.SqlServer` translates a search into `CONTAINS` (prefix mode) or `FREETEXT` (natural
language), composed onto the caller's `IQueryable`. Composition is deliberate: EF Core global query
filters — the mechanism enforcing tenant isolation — apply to a composed query and would be bypassed
by a statement built beside the entity set.

Prefix mode anchors on the right (`"müller*"`) and never on the left.

The search term reaches SQL as a parameter, not as an inlined literal. That matters for a
search-as-you-type endpoint: a literal would give every keystroke its own statement text and its own
query plan.

Language is passed through as a locale identifier, so `Language("de")` reaches the query as `1031`
and German stemming applies. Supported codes are `de`, `en`, `es`, `fr`, `it` and `nl`. A code outside
that set fails startup validation rather than quietly issuing the query with no `LANGUAGE` clause,
which would stem German text with whatever the index defaults to and leave no trace in the results.

### Ranking: what you get today

The score is a **weighted field-match score**: every field that matches contributes its declared
weight. It is translatable, orderable and stable across pages.

It is *not* SQL Server's own `CONTAINSTABLE` rank. `EF.Functions` exposes no `CONTAINSTABLE`, and
reaching it would mean hand-written SQL joined beside the entity set — the shape that silently drops
global query filters. The weighted-match score was chosen over that risk. If true BM25-style ranking
is needed, that is a separate piece of work with its own safety story.

### Creating the index

Index creation belongs in the migration flow rather than in a manual step that gets forgotten in one
environment:

```csharp
migrationBuilder
    .CreateFullTextCatalog("BsftSearch")
    .CreateFullTextIndex(new CreateFullTextIndexRequest(
        Table: "Candidates",
        KeyIndexName: "PK_Candidates",
        CatalogName: "BsftSearch",
        Columns: ["Name", "Notes"],
        Language: "de"));
```

The framework still never runs these itself: they are emitted into the application's own migration,
which owns its schema exactly as it does for every other table. `DropFullTextIndex` is the `Down`
counterpart.

Identifiers are validated against a strict character set rather than escaped — a name that would
need escaping is a name that should be rejected.

#### These statements run outside the migration transaction

SQL Server refuses full-text DDL inside a user transaction (Msg 574), and EF Core wraps a migration
in exactly such a transaction. All four helpers therefore emit their statement with
`suppressTransaction: true`. Without it `dotnet ef database update` aborts on the first
`CREATE FULLTEXT CATALOG` and rolls the whole migration back, which makes the feature undeployable.

The consequence is worth knowing before a deployment goes wrong: a migration that fails *after* one
of these statements is only partly undone. EF rolls back the transactional operations and still
records the migration as unapplied, while the catalogue, the index or the changed stoplist stay in
the database. Creating the catalogue tolerates that, because it is guarded by an existence check.
Creating the index does not — a re-run reports that the index already exists, so drop the leftover
(`DROP FULLTEXT INDEX ON [Candidates]`) before applying the migration again. Keeping the full-text
statements in a migration of their own is the simplest way to avoid the situation entirely.

Stopwords are language-specific, so a German index usually wants its own list rather than the server
default. Attach one in the same flow:

```csharp
migrationBuilder.SetFullTextStopList("Candidates", "GermanStopwords");
```

`SYSTEM` selects the server's own list and `OFF` indexes every word; both are keywords rather than
object names and are emitted unbracketed. Creating and filling the stoplist itself
(`CREATE FULLTEXT STOPLIST` / `sp_fulltext_stopword`) stays with the application, since its contents
are a business decision.

### Index population lag

A SQL Server full-text index is populated asynchronously. A row written now is not necessarily
findable a millisecond later. Do not build a read-after-write flow on search; use the primary key.

## SQLite provider

```csharp
services.AddBsftDataSqlite(o => o
    .HasFullTextIndex("profile", "Candidates_fts")
    .AcceptsUnstemmedLanguage("de"));
```

`Bsft.Data.Sqlite` searches an FTS5 index. Two things differ from SQL Server before anything else:
the index is a **table**, so the group names it, and FTS5 is not known to EF Core, so the index has
to be mapped once in the model:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.HasBsftFullTextIndex("Candidates_fts", "Name", "Notes");
}
```

That call maps the index table and registers the two FTS5 functions the provider composes with. It
creates nothing — the migration does that — and it is excluded from migrations so scaffolding never
tries to. The columns are named in the same order as the group's fields, because `bm25` weights its
columns positionally: a group whose fields are declared in a different order is refused rather than
ranked by the wrong weights, which no result would reveal.

The search becomes a join: the index is matched first and its rows are joined back onto the caller's
queryable by `rowid`. The caller's queryable stays the outer side, so global query filters still
apply. The provider holds no `DbContext` — it reads the model off the query root of the queryable it
is handed, which is what keeps it a singleton like the other two.

Ranking is FTS5's own `bm25`, weighted by the declared field weights and negated so that a higher
score is a better match. This is a real ranking function rather than the weighted field-match score
the SQL Server provider has to make do with.

### German is not stemmed, and the provider says so

FTS5 ships exactly one stemmer, Porter, and Porter is English. There is no German dictionary to
select. So a group declaring `Language("de")` is matched by exact tokens: `schweisser` does not find
`schweisserin` in free text. That is a usable search and often the right trade, but it is not what
the declaration promises, so it has to be said out loud:

```csharp
o.AcceptsUnstemmedLanguage("de");
```

Without that call, startup validation fails. Prefix mode is what covers inflection in practice
(`schweisser` finds `schweisserin`), and diacritics are folded in both directions, so `muller` and
`müller` find each other.

The tokenizer is configurable with `UseTokenizer(SqliteTokenizer.Porter)` for English stemming, or
`Ascii` where folding is unwanted. The default, `unicode61 remove_diacritics 2`, folds diacritics
beyond the Latin-1 range that FTS5's own default stops at.

The options are the only place the tokenizer is named. It is one decision for the application rather
than one per index, because it is also what the startup check above answers from: a per-index
tokenizer would let an index be created with one tokenizer while validation kept certifying another —
including certifying that English is stemmed for an index built without a stemmer. So the migration
command names no tokenizer, and `CreateFullTextIndex` reads it from the options it is handed.

### Diacritic folding

How far accents are folded is its own setting, because it is the one part of the tokenizer
specification a deployment sometimes has to change without changing the tokenizer:

| `RemoveDiacritics` | Effect |
| --- | --- |
| `2` (default) | Folds every diacritic Unicode defines one for. `muller` finds `müller`. |
| `1` | FTS5's own default: folds the Latin-1 range only, leaving the rest unfolded. |
| `0` | Folds nothing. `muller` and `müller` are different terms. |

```csharp
services.AddBsftDataSqlite(o => o
    .HasFullTextIndex("profile", "Candidates_fts")
    .UseRemoveDiacritics(0));
```

Change it only where accents genuinely carry meaning that must not be collapsed — a lexicon, a
transliteration table — because a user searching from a plain keyboard is the case the default
serves. Any value outside `0`, `1` and `2` is refused while the application is wired up rather than
by `CREATE VIRTUAL TABLE` halfway through a deployment.

The level is part of the **index**, not of the query: terms already stored were folded under the old
one, so changing it means recreating the index and rebuilding it. The `ascii` tokenizer ignores the
setting entirely — it treats everything above U+007F as a token character, so it has nothing to fold.

### Creating the index

```csharp
var index = new CreateFullTextIndexRequest(
    IndexTable: "Candidates_fts",
    ContentTable: "Candidates",
    KeyColumn: "Id",
    Columns: ["Name", "Notes"]);

migrationBuilder
    .CreateFullTextIndex(index, SearchSetup.Options)
    .CreateFullTextSyncTriggers(index)
    .RebuildFullTextIndex("Candidates_fts");
```

The options are handed in because a migration runs outside the container. The index is built with the
tokenizer specification they describe and the provider answers for the same ones at startup, so build
them once and register that instance rather than a second one configured alike:

```csharp
public static class SearchSetup
{
    public static BsftDataSqliteOptions Options { get; } = new BsftDataSqliteOptions()
        .HasFullTextIndex("profile", "Candidates_fts")
        .UseRemoveDiacritics(0);
}

services.AddBsftDataSqlite(SearchSetup.Options);
```

`AddBsftDataSqlite` also takes the usual `Action<BsftDataSqliteOptions>` callback; the overload above
exists so the migration can reach the same declaration. The container receives a copy of it, so a
mutation of `SearchSetup.Options` after the registration cannot make the provider and the migration
disagree; the provider reads it through `IOptions<BsftDataSqliteOptions>`. Left out of the migration call entirely, the
framework's default of `2` applies, which is what a deployment that configures none already had. Only
`CreateFullTextIndex` takes the options; the triggers and the rebuild carry no tokenizer
specification.

An external-content index stores the terms but not the text, so the base table stays the single copy
of the data — and the engine does not maintain it. The three triggers are what keep it in step, and
without them the index answers with whatever was true when it was last rebuilt. `RebuildFullTextIndex`
fills it from rows that already exist; the triggers only carry changes made from then on, so a
migration against a populated table needs both. `DropFullTextIndex` is the `Down` counterpart and
drops the triggers first, since a trigger writing into a table that no longer exists fails every
insert into the content table.

The base table's primary key has to be a single `int` or `long`: an external-content index addresses
its rows by `rowid`. Anything else is refused with that reason.

#### These statements run inside the migration transaction

The opposite of the SQL Server decision, and deliberately so. SQLite's DDL is transactional, virtual
tables and triggers included, so these statements roll back with the rest of a failing migration
instead of leaving a half-created index behind. Every statement is also written to be re-runnable, so
a migration re-applied after an unrelated failure does not trip over its own leftovers.

### Index population is not lagging

Unlike SQL Server, a trigger-maintained FTS5 index is updated inside the same transaction as the
write. A row written now is findable now.

## PostgreSQL provider

```csharp
services.AddBsftDataPostgres(o => o.HasFullTextIndex("profile"));
```

`Bsft.Data.Postgres` matches a stored `tsvector` column with `@@`, which is what the GIN index is
built on. The column is mapped in the application's model, usually as a shadow property so the entity
does not have to carry a database concept as a member:

```csharp
modelBuilder.Entity<Candidate>().Property<NpgsqlTsVector>("SearchVector");
modelBuilder.Entity<Candidate>().HasIndex("SearchVector").HasMethod("GIN");
```

Another property name is fine — `HasFullTextIndex("profile", "ProfileVector")`. A group whose column
is unmapped, or mapped as text rather than `NpgsqlTsVector`, is refused with the mapping that fixes
it; matching text with `@@` is not a slower search but a different one that does not use the index.

Prefix mode anchors every word (`müller:* & schmidt:*`) and free text goes through
`websearch_to_tsquery`. The two are sanitised differently because the two functions are:
`to_tsquery` takes an operator expression and fails on anything it cannot parse, so prefix mode is
rebuilt from the term's own words; `websearch_to_tsquery` is defined to take whatever a user typed,
quoted phrases and all, and is given the term as it stands.

Language reaches the query as a text search configuration, so `Language("de")` becomes `german` and
German is stemmed by a real Snowball stemmer. The built-in codes are `de`, `en`, `es`, `fr`, `it` and
`nl`. A code outside that set fails startup validation rather than quietly running on the server's
`default_text_search_config`.

### The deployment's own text search configurations

The built-in map is a floor, not a fence. A deployment that has built its own `regconfig` — an
`unaccent`-composed one, a dictionary with domain stopwords — registers it against the code the
groups already declare:

```csharp
services.AddBsftDataPostgres(o => o
    .HasFullTextIndex("profile")
    .UseTextSearchConfiguration("de", "german_unaccent")   // overrides the built-in german
    .UseTextSearchConfiguration("pt", "portuguese"));      // adds a code the framework never knew
```

**A registration wins over the built-in mapping for the same code.** That is what makes it an
override rather than only an extension: a deployment that has built `german_unaccent` wants every
group declaring `de` to use it, not merely the codes the framework never shipped. Register it before
the migration that builds the index and before startup validation runs — both read the same options
instance — so the query names the same configuration the index was built with. Naming a different
one is not a slower search but a different one, probing the GIN index with lexemes another stemmer
produced.

The configuration name is interpolated into the generated column as a literal, so it is whitelisted
to letters, digits and underscores exactly as every other identifier this package emits. A
schema-qualified name is therefore rejected; put the configuration on the `search_path`.

A code that maps to nothing still resolves to `null`, and that fallback stays: leaving a query on the
server's `default_text_search_config` is better than guessing wrong and stemming German with English
rules. It is silent only at the point of resolution. A group declaring such a code fails startup
validation, and `AddFullTextVector` refuses to emit DDL for one, so a deployment reaches the server
default by choosing to rather than by forgetting to.

### Ranking and the four weight labels

Ranking is `ts_rank` with the group's declared field weights. PostgreSQL weights lexemes by the four
labels `A` to `D`, which the migration puts on the indexed columns in the order the group declares
its fields, and `ts_rank` reads them back as the vector `{D, C, B, A}`. The consequence is a real
limit rather than a convention: a group can weight **at most four fields**, and one declaring more is
reported at startup instead of silently ranking the fifth as the least important.

### Creating the column and the index

```csharp
var index = new CreateFullTextIndexRequest(
    Table: "Candidates",
    Columns: ["Name", "Notes"],
    Language: "de");

migrationBuilder.AddFullTextVector(index).CreateFullTextIndex(index);
```

#### Stating the stemming language once

The regconfig is baked into the index, so every migration decides it — and the one migration that
forgets gets English stemming on German text with nothing in the results to say so. A German-first
deployment states it once instead:

```csharp
public static class SearchSetup
{
    public static BsftDataPostgresOptions Options { get; } = new BsftDataPostgresOptions()
        .HasFullTextIndex("profile")
        .UseDefaultLanguage("de")
        .UseTextSearchConfiguration("de", "german_unaccent");
}

services.AddBsftDataPostgres(SearchSetup.Options);
```

```csharp
var index = new CreateFullTextIndexRequest(Table: "Candidates", Columns: ["Name", "Notes"]);

migrationBuilder.AddFullTextVector(index, SearchSetup.Options).CreateFullTextIndex(index);
```

`Language` on the command is the per-index override and keeps working: named, it wins over the
default; left out, the options decide. `DefaultLanguage` is `en` until something sets it, so a
deployment that configures none emits exactly what it did before.

The options are handed in because a migration runs outside the container. Build them once and
register that instance — `AddBsftDataPostgres` takes either the usual
`Action<BsftDataPostgresOptions>` callback or a prepared instance — so the index is built with the
configuration the provider will name at query time, registrations included. Two instances configured
alike work until one of them changes. The container receives a copy of the declaration and the
provider reads it through `IOptions<BsftDataPostgresOptions>`, so a mutation after the registration
cannot move the two apart either. Only `AddFullTextVector` takes the options;
`CreateFullTextIndex` puts no regconfig in its DDL.

An unmappable language — the command's or the default's — throws while the DDL is being built, before
anything reaches the server.

which emits

```sql
ALTER TABLE "Candidates" ADD COLUMN IF NOT EXISTS "SearchVector" tsvector GENERATED ALWAYS AS (
  setweight(to_tsvector('german', coalesce("Name", '')), 'A') ||
  setweight(to_tsvector('german', coalesce("Notes", '')), 'B')) STORED;
CREATE INDEX IF NOT EXISTS "IX_Candidates_SearchVector" ON "Candidates" USING GIN ("SearchVector");
```

Generated and stored rather than trigger-maintained: PostgreSQL keeps it in step itself, so the index
cannot drift and nothing has to be rebuilt after a bulk import. The text search configuration is a
literal because it has to be — `to_tsvector(regconfig, text)` is only immutable in that form, and a
generated column accepts nothing else. Every column is wrapped in `coalesce`, since concatenating a
null `tsvector` yields null and one empty column would erase the whole document.

GIN rather than GiST: slower to build, faster to search, which is the trade a search endpoint wants.

#### These statements run inside the migration transaction

PostgreSQL's DDL is transactional, `ALTER TABLE` and a plain `CREATE INDEX` included, so these roll
back with the rest of a failing migration. `CREATE INDEX CONCURRENTLY` is deliberately not offered:
it is the one form that would need the transaction suppressed, and it would buy nothing here, because
adding a stored generated column rewrites the table under an `ACCESS EXCLUSIVE` lock in the same
migration. On a large table, add the column in its own migration and plan for the rewrite.

## Observability

| Instrument | Meaning |
| --- | --- |
| `bsft.data.search.duration` | Search duration in seconds. |
| `bsft.data.search.results` | Total number of matches. |

Both tagged with `search.group`, so a degrading index shows up before users complain.

## Verification limits

This is where each provider's tests stop, stated per engine rather than as one blanket claim.

**SQL Server and PostgreSQL** are verified through the SQL they generate and the EF model they
require, not against a running instance: the repository has no such server and no container
infrastructure in CI. Predicate shape, the language identifier or text search configuration, the
ranking expression, scoping composition, the model checks and the migration DDL are all covered.
What is **not** verified and should be checked once against a real instance before relying on it:
that stemming behaves as expected in a given language, how ranking orders real data, whether the
planner picks the index, and — for PostgreSQL — that the generated-column DDL applies cleanly on the
server version in use. For SQL Server, index population timing is in that list too.

**SQLite is verified end to end against the real engine**, because it runs in-process and
Microsoft.Data.Sqlite ships FTS5. Those tests create the schema by executing exactly the statements
the shipped migration helpers emit, write through EF so the triggers fire, and then assert what the
engine returns: prefix and free-text matching, real `bm25` ordering under the declared weights,
tenant and organisational scoping composed with the index match, updates and deletes keeping the
index in step, diacritic folding, rank-stable paging, a pre-applied sort surviving the join, and the
rebuild path for an index created over a table that already holds rows. The unstemmed-German
boundary is asserted rather than only described.

## Sample

The sample application runs on SQLite and does not declare a searchable group. It could now — with
`Bsft.Data.Sqlite` there is a provider for that engine — but the sample's own tables carry no text
worth an index, and a declaration added only to demonstrate the wiring would be a fixture rather than
an example.
