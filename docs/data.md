# Bsft.Data

Server-side filtering, sorting and pagination driven by a declared whitelist,
plus multi-tenancy that is enforced by the query pipeline rather than by each
handler remembering to add a `WHERE`.

Full-text search lives in the same module but has its own page:
[search.md](search.md).

> There is no repository or unit-of-work abstraction in this framework. Handlers
> work against `IQueryable<T>` from a `DbContext` directly. `Bsft.Data` supplies
> the query *surface*, not a persistence indirection.

## Declaring a query surface

A query configuration derives from `QueryConfigurationBase<TEntity, TDto>` and
declares which DTO fields are filterable and sortable:

```csharp
public class ProductQueryConfiguration : QueryConfigurationBase<Product, ProductDto>
{
    public override void Configure(QueryConfigurationBuilder<Product, ProductDto> builder)
    {
        builder.Field(d => d.Name)
            .AllowFilter(FilterOperator.Eq, FilterOperator.Contains, FilterOperator.StartsWith)
            .AllowSort();

        builder.Field(d => d.Price)
            .AllowFilter(FilterOperator.Gt, FilterOperator.Lt, FilterOperator.Gte, FilterOperator.Lte)
            .AllowSort();

        builder.MaxPageSize(50);
        builder.DefaultSort(d => d.Name, SortDirection.Asc);
    }
}
```

A field not declared here cannot be filtered or sorted at all. That is the point:
the client sends field names, and an undeclared name is rejected rather than
compiled into an expression. `MapsTo` overrides the entity property when the DTO
name differs; without it the mapping is by name, case-insensitively.

`MaxPageSize` defaults to **100**. There is no default sort field unless you set
one, and a field named in `DefaultSort` has to be declared with `AllowSort()`
like any other. A host that runs startup validation — anything on
`AddBsftDefaults` — refuses to start otherwise: the default sort orders every
request that states no sort of its own, so an undeclared field there breaks the
most ordinary request there is, and it is knowable long before that. This is new
in 1.3.1, see the 1.3.1 migration notes.

## Setup

```csharp
services.AddBsftDataQueryFiltering(qf => qf
    .FromAssembly(typeof(ProductQueryConfiguration).Assembly));
```

The configure delegate is required. The scan registers, per discovered
configuration, an `IQueryExecutor<TEntity, TDto>` and an
`ISearchExecutor<TEntity, TDto>`, plus the data telemetry source and the search
startup validator.

Both entry points live on `Bsft.Data.DependencyInjection.DataServiceCollectionExtensions`, the
package's one registration class. The query-configuration types —
`QueryConfigurationBase<,>`, `QueryConfigurationBuilder<,>`, `FieldBuilder<,>`,
`QueryFieldMap<>`, `IQueryExecutor<,>` and `QueryFilteringConfiguration` — are in
`Bsft.Data.Filtering`, beside the filter compiler they feed; they were in
`Bsft.Data.Configuration`, which was not a folder these conventions name.
`IDataTelemetry` is in `Bsft.Data` itself, because `Diagnostics/` is a structural
folder that adds no namespace segment.

## The relational model warm-up

EF Core builds a model's *relational* model (tables, columns, mappings) lazily,
on the first `GetRelationalModel` call, and that call has a check-then-act gap:
it reads the built relational model, finds none, and only then reads the factory
that builds it, while the caller that does build it stores the result and removes
the factory in between. A caller caught between the two reads finds neither and
throws "The model must be finalized and its runtime dependencies must be
initialized before 'GetRelationalModel' can be used". The gap exists only while a
model is new, so it hits concurrent first requests (or background workers)
right after a host starts. It is present in EF Core 9 and 10.

`AddBsftDataQueryFiltering`, `AddBsftDataUnitOfWork<TContext>` and
`AddBsftDataMultiTenancy` register a hosted service that closes it; a host using
EF Core without any of them calls `AddBsftDataModelWarmup()` itself.
Calling it more than once registers it once.

```csharp
services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
services.AddBsftDataModelWarmup();
```

The service subscribes to EF Core's diagnostic listener in `StartingAsync`, which
the host runs for every lifecycle service before it starts any hosted service,
and builds the runtime model's relational model when a context reports
`ContextInitialized` — before that context can run a command — under one
process-wide lock. Every later caller finds the built model on its first read.
It needs no knowledge of how a context is registered (`AddDbContext`, a factory,
a pool or `new`), resolves nothing itself, skips non-relational providers, and
costs one annotation lookup per context once the model is built. The subscription
ends when the host has stopped.

Only the runtime model is built. The design-time model that migrations and
`EnsureCreated` use is a second, separately cached model; building it for every
context type would double the model cost of every host to cover a path that runs
once, at deployment.

To keep EF Core's lazy build, turn it off:

```csharp
services.Configure<BsftDataOptions>(o => o.ShouldWarmRelationalModels = false);
```

or set `Bsft:Data:ShouldWarmRelationalModels` to `false` where the section is bound
(`AddBsftDataMultiTenancy(configuration, ...)`).

A test has no host to run the hosted service, so `Bsft.Data.Testing.TestRelationalModelWarmup.EnsureRelationalModelBuilt(this DbContext)`
builds the same runtime model from a test's own startup hook. `tests/Shared/RelationalModelWarmup.cs` calls it for
every relational EF test project in this repository and builds the design-time model itself, the one path this
guard leaves lazy on purpose.

## The unit of work

`DbContextUnitOfWork<TContext>` is the framework's implementation of both
persistence contracts in [`Bsft.Core.Persistence`](core.md#the-unit-of-work) over
the application's own context:

```csharp
services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
services.AddBsftDataUnitOfWork<AppDbContext>();
```

That registers one scoped instance behind `IUnitOfWork` and
`ITransactionalUnitOfWork`. A commit is the context's `SaveChangesAsync`; a unit
is one transaction on the context's connection, run through the provider's
execution strategy so a host that configured retries gets them instead of an
exception. A transaction the host already opened is **joined** rather than
nested, because a second transaction on the same connection would either fail or
quietly commit one half on its own. On a non-relational provider — the in-memory
one a host meters with — the work simply runs, which is the same limitation the
framework's in-memory stores state about themselves.

It is the host's context on purpose: every package here that persists through EF
Core writes into the application's context rather than one of its own, so a row
it stages commits with whatever else the request was writing. Registration is
`TryAdd` throughout, so a host with a unit of work of its own — one spanning two
stores, or something this framework never sees — registers it first and keeps it.

This is what a package typed against `IUnitOfWork` finds: the API key expiry
reminder sweep (`Bsft.Auth.ApiKeys`)
and the notification digest sweep
(`Bsft.Notifications`) both close their own unit of work through
it.

## Requesting a page

```csharp
public record PagedRequest(
    FilterNode? Filter = null,
    IReadOnlyList<SortItem>? Sort = null,
    int Page = 1,
    int PageSize = 25)
{
    public SearchRequest? Search { get; init; }
}
```

`DefaultPageSize` is 25. `Search` is deliberately not positional — set it with an
object initialiser or `with`.

The executor applies filter, sort and paging:

```csharp
public interface IQueryExecutor<TEntity, TDto>
{
    Task<PagedResult<TEntity>> ExecuteAsync(IQueryable<TEntity> query, PagedRequest request, CancellationToken ct = default);
}
```

and returns

```csharp
public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize, bool HasNextPage);
```

For a query with no declared surface, the plain extension is enough:

```csharp
var page = await db.Products.AsNoTracking().ToPagedResultAsync(page: 1, pageSize: 25, ct);
```

A list item of a resource that supports conditional writes carries the entity tag
of its own row, so a client can write straight from a list row without reading the
resource first. The contract is one nullable member:

```csharp
public interface IEntityTaggedItem { string? ETag { get; } }
```

`PagedResult<T>` is unaffected — the validator belongs to the row, not to the page
— and the member is nullable so a resource with no concurrency token publishes
none. The tag must be the same value the resource's `GET` by id serves; the rules
and the one-place derivation are in
[crud-conventions.md](crud-conventions.md#every-list-row-carries-its-own-validator).

## The filter language

Filters arrive as a small JSON AST rather than a string expression, so there is
nothing to parse and nothing to inject:

```csharp
public class FilterNode;                       // "comparison" | "present" | "and" | "or"
public class AndNode : FilterNode { public required List<FilterNode> And { get; set; } }
public class OrNode  : FilterNode { public required List<FilterNode> Or  { get; set; } }
public class ComparisonNode : FilterNode
{
    public required string Field { get; set; }
    public required FilterOperator Op { get; set; }
    public required JsonElement Value { get; set; }
}
public class PresenceNode : FilterNode         // the pr (present) operator — unary, no value
{
    public required string Field { get; set; }
}
```

`FilterOperator` covers `Eq, Neq, Gt, Gte, Lt, Lte, Contains, NotContains,
StartsWith, NotStartsWith, EndsWith, NotEndsWith, Present`, serialised as `eq`,
`neq`, `gt`, `gte`, `lt`, `lte`, `contains`, `!contains`, `startsWith`,
`!startsWith`, `endsWith`, `!endsWith`, `pr`.

The vocabulary is audited against **SCIM (RFC 7644 §3.4.2.2)** — the only IETF RFC
with a formal filter grammar — for its operator set and semantics (`eq`, `ne`,
`co`, `sw`, `ew`, `gt`, `ge`, `lt`, `le`, `pr`, plus `and`/`or`); the wire format
stays the JSON AST rather than SCIM's string expression. The full lineage
(JSON:API's `filter` param, OData deliberately rejected, the JSON-AST family) and
the per-operator mapping table live in
[crud-conventions.md](crud-conventions.md#where-the-operator-vocabulary-comes-from).

`Present` (`pr`) is unary — it asks only whether a field has a value — so it is
carried by its own `PresenceNode` rather than a `ComparisonNode` with a faked
value, which keeps `ComparisonNode.Value` a required member. It compiles to
`col IS NOT NULL` for a nullable or reference field and to a constant `true` for a
non-nullable value field, since that value is always present. The string operators
(`contains`, `startsWith`, `endsWith` and their negations) are accepted only
against string-typed fields; aimed at a non-string field they are a 400, not a 500.

A `null` value is the absence of one rather than a value to compare against, so
only equality says anything about it. `{"op":"eq","value":null}` compiles to
`col IS NULL` and `neq` to `col IS NOT NULL`, on a scalar field: a nullable value
field, a string or any other reference-typed one. Against a field that can never
be null it is a 400, and so is a `null` carried by any other operator, because
each of those would otherwise answer rather than refuse — `co` against nothing
lowers to a search for the empty string and matches every row, an ordering
operator lifts to a comparison that is false for all of them. A field mapped to a
whole owned type rather than to one of its columns is outside this: the database
reads such a comparison column by column while an in-memory evaluation reads it
as a reference, and the two can disagree.

A node is compiled against the whitelist, which is where an undeclared field or a
disallowed operator is refused:

```csharp
Expression<Func<T, bool>> predicate = node.Compile(fieldMap);
```

## Multi-tenancy

```csharp
services.AddBsftDataMultiTenancy(mt => mt.ResolveFromHeader("X-Tenant-Id"));
```

`MultiTenancyConfiguration` offers `ResolveFromHeader(string? = null)`,
`ResolveFromClaim(string? = null)`, `ResolveFromSubdomain()` and
`ResolveUsing<TResolver>()`. It selects the resolver *type*; the header name and
the claim type it reads live on `BsftDataOptions.MultiTenancy`, whose defaults
are `DataDefaults.TenantHeaderName` (`"X-Tenant-Id"`) and
`DataDefaults.TenantClaimType` (`"tenant_id"`). Passing a name states it in code
and it is applied after the configuration section, so it wins; passing none
leaves whatever the section says:

```csharp
services.AddBsftDataMultiTenancy(configuration, mt => mt.ResolveFromHeader());
```

```jsonc
// section "Bsft:Data"
{ "Bsft": { "Data": { "MultiTenancy": { "HeaderName": "X-Account" } } } }
```

The header name must not be blank and must be a valid HTTP field name — one a
request cannot carry would leave the tenant silently unresolved — and the claim
type must not be blank. Both are checked inside `AddBsftDataMultiTenancy` and
again on start.

The resolver is a singleton and reads its name from `IOptions<BsftDataOptions>`
once, at construction; the context is scoped:

```csharp
public interface ITenantContext
{
    string? TenantId { get; }
    bool IsResolved { get; }
}
```

This lives in **`Bsft.Data.MultiTenancy`**. (A second `ITenantContext` once
existed in a `Bsft.Data.Tenancy` namespace; it is gone — see
the 1.3 migration notes.)

`AddBsftDataMultiTenancy` registers the context, the resolver and
`TenantSaveChangesInterceptor`. It does **not** add the middleware, and there is
no `UseBsftMultiTenancy()` — resolve the tenant per request explicitly:

```csharp
app.UseMiddleware<TenantMiddleware>();
```

Entities opt in with `ITenantEntity` (`string? TenantId { get; set; }`), or by
deriving from `TenantEntity`. Two mechanisms then apply:

- `UseTenantFilter(this)` in `OnModelCreating` adds a global query filter, so
  reads are scoped without the handler asking.
- `TenantSaveChangesInterceptor` stamps `TenantId` on inserts, and **throws** on
  an update that would move a row across tenants, on an insert with no tenant
  resolved, and — **deny by default** — on any update of a tenant-scoped entity
  from a scope with no tenant resolved.

The interceptor is registered for you, but putting it on the context is the
host's job, and it is the one omission nothing at runtime reports: reads stay
filtered while writes quietly go unstamped. A startup validator therefore fails
the host when a context implementing `ITenantScopedDbContext` is built without
it, whether it was named on the registration or added in the context's own
`OnConfiguring`:

```csharp
services.AddDbContext<AppDbContext>((sp, options) => options
    .UseSqlServer(connectionString)
    .AddInterceptors(sp.GetRequiredService<TenantSaveChangesInterceptor>()));
```

Registering the interceptor in the container instead, as an `IInterceptor` or an
`ISaveChangesInterceptor`, does not work and the check does not accept it: EF
Core reads interceptors off the context's options and does not resolve them from
the application's container, so one registered only there never runs. The check
finds a context through its `DbContextOptions<TContext>` registration, so one
the host constructs itself is never examined. Options registered for the
lifetime of the application, which is what `AddDbContextFactory` and an explicit
`optionsLifetime` of `Singleton` do, are failed outright: a guard built into
them is resolved from the root provider and keeps whatever tenant was current
when it was built, so every later request would be stamped with the first one. A
context that cannot be constructed at startup is passed with a warning rather
than failed.

The one way past that last refusal is to say so at the call site. A background
job that reads rows across tenants and writes each one back where it found it
wraps its save in `context.AllowTenantUnchangedWrites()` (`TenantGuardScope`); inside
that declaration an update whose own `TenantId` is unchanged from the value it
was loaded with is allowed, and one that moves the row to another tenant is still
refused:

```csharp
using (context.AllowTenantUnchangedWrites())
{
    await context.SaveChangesAsync(ct);
}
```

**The declaration is required because the entity's values cannot carry the
intent.** Comparing `TenantId` against its `OriginalValue` looks like a check but
is not one on its own: for an entity that was never loaded through this context —
`DbSet.Update`, or `Attach` plus `EntityState.Modified` — EF Core seeds the
original values from the current ones, so the two are equal whatever tenant the
caller put on the entity. Only make the declaration where the rows being written
came from a tracked query on the same context.

```csharp
public sealed class AppDbContext : DbContext, ITenantScopedDbContext
{
    private readonly ITenantContext _tenantContext;

    public string? CurrentTenantId => _tenantContext.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseTenantFilter(this);
    }
}

services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlServer(connectionString)
        .AddInterceptors(sp.GetRequiredService<TenantSaveChangesInterceptor>()));
```

**Pass the context, not the tenant context, and the reason is not style.** EF Core
caches the model — and every query filter expression in it — per context type for
the life of the process, while `ITenantContext` is scoped to a request. A filter
built from the tenant context therefore holds the instance of
whichever scope built the model first and keeps filtering on that tenant forever;
a second tenant then reads nothing, or reads the first tenant's rows. A filter
that reads a *member of the context* is rewritten by EF Core to the context
executing the query, so the tenant is resolved per query. That is what
`ITenantScopedDbContext` exists for, and the overload is constrained to
`where TContext : DbContext, ITenantScopedDbContext`, since only the context
itself gets rewritten — an implementation that is not the context would be
captured exactly like the tenant value.

**Register a tenant-scoped context with `AddDbContext`, not
`AddDbContextPool`.** A pooled context is constructed once and reused, so the
`ITenantContext` its constructor received is never resolved again and
`CurrentTenantId` freezes on the first tenant that rented it — the same defect
as the captured filter, arriving through pooling instead of the model cache.
`AddBsftDataMultiTenancy` registers a startup validator that refuses this
combination by name, so the mistake fails the host at startup rather than in
production. The check reads the registrations rather than the built provider,
because pooling is only ever visible as a registration; it therefore holds
whichever order `AddBsftDataMultiTenancy` and `AddDbContextPool` were written in,
and it does not fire for a pooled context that is not tenant-scoped. A host that
calls `UseTenantFilter` without registering multi-tenancy through this framework
gets no such check, and the rule above is then the whole story.

`UseStaticTenantFilter(tenantContext)` is the same filter for a host whose tenant
is fixed for the whole process — one deployment per tenant. It is a separate name
rather than an overload on purpose: the two once differed only in the static type
of one argument, so passing the `ITenantContext` field instead of the context
compiled, started, served and leaked across tenants with nothing to signal it.
Anywhere the tenant varies per request, which is what `ResolveFromHeader` and
`ResolveFromClaim` are for, `UseTenantFilter(this)` is the only correct choice.
This was a defect through 1.3.5; see the 1.3.6 migration notes.

Failing closed is deliberate. A missing tenant produces an exception, not a write
that quietly lands in the wrong tenant or in none.

### A sandbox tenant

A developer trying the platform out, or a payment provider's test mode, wants a tenant that is not a
customer's. `AddBsftSandboxTenants()` registers `ISandboxTenantProvisioner`, which hands one out and
takes it back:

```csharp
var sandbox = await provisioner.ProvisionAsync(new SandboxTenantSpecification
{
    Label = "Trying the reports API",
    RequestedBy = developer.Id,
    Lifetime = TimeSpan.FromDays(2),     // or leave it unset for the configured default
});

scope.ServiceProvider.EnterTenant(sandbox.TenantId, "The sandbox request");
```

It is entered like any other tenant, with `EnterTenant`, because a sandbox entered through a
mechanism of its own would exercise a path production never takes.

**This is not a tenant lifecycle.** `TenantId` is a plain string this framework has no table for, so
there is no organization created, nothing seeded, no membership and no billing. What is on offer is an
identifier nothing else was given, and a way to give it back.

**Two things the contract states rather than leaves to prose.** A sandbox tenant is isolated in
exactly the sense every other tenant is — by the scoping your own queries apply. A query that
forgets its tenant predicate has a leaky sandbox, and nothing here can fix that. And teardown removes
what this seam knows about: the registry entry, plus whatever each registered `ISandboxTenantCleanup`
speaks for. Rows you wrote under that tenant id through your own `DbContext` are yours to remove, and
`SandboxTeardown.ClearedBy` names the handlers that actually ran so a caller can tell rather than
assume.

Two seams, and the host brings both:

| Seam | Default | Why |
| --- | --- | --- |
| `ISandboxTenantRegistry` | none, and startup fails without it | The promise is an identifier *nothing else uses*, which only something durable keeps across a restart. An in-memory default would look like the seam working and hand out a colliding identifier the first time the process recycled. |
| `ISandboxTenantCleanup` | none, and none is required | Each implementation speaks for one package's rows. A host with no handler gets a teardown that forgets the registry entry and says it cleared nothing. |

The registry is also what makes teardown safe: `TearDownAsync` runs handlers only over an identifier
the registry knows, so naming a real tenant reaches nothing and answers `WasKnown: false`. Tearing the
same sandbox down twice is therefore safe too. Handlers run **before** the registry entry goes, so a
handler that throws leaves the sandbox listed and the teardown repeatable rather than leaving rows
behind under an identifier nothing names any more.

`ExpiresAt` is a statement, not a mechanism: nothing here sweeps an expired sandbox. A host that wants
them collected reads its own registry and calls the provisioner, which is a job rather than a seam.

| Option (`Bsft:SandboxTenants`) | Default | Effect |
| --- | --- | --- |
| `IdPrefix` | `sandbox` | What every sandbox identifier begins with. A readable marker, not a boundary: the registry is what prevents a collision. |
| `DefaultLifetime` | 7 days | How long a sandbox lasts when the caller named none. |
| `MaxProvisionAttempts` | 5 | How many identifiers to try before failing rather than spinning. |

## Key comparison

String keys compare **ordinally** — case-sensitive and culture-independent — and
that is the contract, not an implementation detail. It matters because several
packages ship a store as a pair: an in-memory implementation and an EF Core twin
behind one interface. The in-memory halves all key ordinally
(`InMemoryJobStore` on the job name, `InMemorySagaStore` and
`InMemorySagaTimeoutStore` on the saga correlation key, `InMemoryIdempotencyStore`
on the `(Scope, Key)` idempotency key), while the EF twins compare with LINQ
predicates that EF Core translates to SQL, so the database **column collation**
decides the comparison.

Under SQL Server's default case-*insensitive* collation the two disagree: two
keys differing only in case are two entries in memory but one row in the database
— a silent idempotency replay, two saga instances folded into one, or a
unique-index violation when seeding a job. So that production matches the ordinal
contract, give the affected key columns a case-sensitive or binary collation
where case matters:

```csharp
// idempotency Scope/Key, saga CorrelationKey (+ timeout SagaType/Name),
// scheduling job name
builder.Property(e => e.Key).UseCollation("SQL_Latin1_General_CP1_CS_AS");
```

PostgreSQL is case-sensitive by default, so no action is needed there. The full
rationale, the rejected alternatives and the per-package list are recorded in
ADR-0003.

## The package split

The query surface ships as two packages, because the two halves have entirely
different dependency needs:

| Package | Contains | Depends on |
| --- | --- | --- |
| `Bsft.Data.Contracts` | `PagedRequest`, `PagedResult<T>`, `SortItem`, `SortDirection`, the filter AST (`FilterNode`, `AndNode`, `OrNode`, `ComparisonNode`, `PresenceNode`, `FilterOperator`), `SearchRequest`, `SearchMode`, `RankedItem<T>` | nothing at all |
| `Bsft.Data` | the query executor, the filter compiler, the search pipeline, multi-tenancy, the EF Core extensions | EF Core, EF Core Relational, ASP.NET Core HTTP abstractions, plus the contracts |

The split exists for the client side. A Blazor WebAssembly front end deserialises
a `PagedResult<T>` and builds a `PagedRequest`; referencing `Bsft.Data` for those
would pull EF Core and its relational provider into the browser bundle for types
that are records with no behaviour. A console tool or an integration-test client
is in the same position.

What did *not* move is as deliberate as what did. `SearchScopeState`,
`IOrganizationScopedEntity` and `ISearchScopeProvider` have no dependencies either, but
they are the server's visibility boundary rather than part of the wire contract:
a client neither sends nor receives them, and putting them in a package a browser
references would advertise them as something it is meant to fill in.

Nothing changed for an application that references `Bsft.Data`. The namespaces
are unchanged — `Bsft.Data.Pagination`, `Bsft.Data.Filtering` and
`Bsft.Data.Search` are what they were, and only the assembly behind them moved —
and `Bsft.Data` carries a `[assembly: TypeForwardedTo]` for every moved type, so
an assembly compiled against 1.3.2 keeps loading unchanged. No `using` needs
touching and no rebuild is required.

Take the contracts alone when a project uses the query types but never executes a
query. `Bsft.Notifications.InApp` is the framework's own case: its inbox contract
returns `PagedResult<InAppNotification>` and needs nothing else from the data
layer, so it references `Bsft.Data.Contracts` and leaves the EF Core work to
`Bsft.Notifications.InApp.EFCore`.

## Search provider packages

`Bsft.Data` declares what is searchable; an engine package supplies the
`ISearchProvider` that executes it, plus the migration helpers that create the
index. There are three — `Bsft.Data.SqlServer`, `Bsft.Data.Sqlite` and
`Bsft.Data.Postgres` — and an application registers exactly one. Swapping engines
does not touch a call site. See [search.md](search.md).

## Example

```csharp
public sealed class GetProductsHandler(IInventoryDbContext db, IQueryExecutor<Product, ProductDto> executor)
    : IQueryHandler<GetProducts, PagedResult<Product>>
{
    public Task<PagedResult<Product>> HandleAsync(GetProducts query, CancellationToken ct = default) =>
        executor.ExecuteAsync(db.Products.AsNoTracking(), query.Request, ct);
}
```

## See also

- [search.md](search.md) — full-text search over the same declarations.
- [mapping.md](mapping.md) — projecting the page to DTOs inside the query.
- `Bsft.Organization` — org-unit scoping, the second scoping axis.
