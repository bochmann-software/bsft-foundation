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

## A transaction across several contexts

A modular host often has one database and one context per module, each with its
own schema and its own migrations history. A command of one module that
dispatches a command to another then writes through two contexts, and the
single-context unit of work above cannot make that atomic. The shared transaction
can, from 2.2.1-preview.1 on. It is opt-in: a host that does not register it keeps
everything above unchanged.

```csharp
services.AddBsftDataSharedTransaction(_ => new NpgsqlConnection(connectionString));

services.AddDbContext<CatalogDbContext>((sp, options) => options
    .UseBsftSharedTransaction(sp, (o, connection) => o.UseNpgsql(connection,
        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "catalog"))));
services.AddDbContext<InventoryDbContext>((sp, options) => options
    .UseBsftSharedTransaction(sp, (o, connection) => o.UseNpgsql(connection,
        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory"))));
```

On PostgreSQL, `Bsft.Data.Postgres` shortens both calls and adds the failure
classification described below:

```csharp
services.AddBsftDataPostgresTransaction(connectionString);
services.AddDbContext<CatalogDbContext>((sp, options) => options
    .UseBsftSharedNpgsql(sp, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")));
```

The PostgreSQL registration is `AddBsftDataPostgresTransaction`, not
`AddBsftDataPostgresSharedTransaction`, on purpose: the longer name breaks the
30-character limit on method names, and the package registers nothing else called
a transaction, so the shorter one is unambiguous. The pair stays recognizable by
its options call, `UseBsftSharedNpgsql`.

**One connection per scope.** `AddBsftDataSharedTransaction` registers one
scoped instance behind `IAmbientTransaction`, `ITransactionalUnitOfWork` and
`IUnitOfWork` (all from [`Bsft.Core.Persistence`](core.md#the-unit-of-work)). It
creates the scope's connection with the host's factory the first time a context or
a unit needs it and disposes it with the scope. `UseBsftSharedTransaction` hands
that connection to the context's provider call, so every enlisted context of a
scope talks over the same connection while everything else about the provider
(schema, migrations history table, command timeout) stays per context. The API is
provider-agnostic: the connection factory and the provider call are the host's.

**The outermost unit opens, every nested one joins.** The first
`ExecuteAsync` of a scope opens the connection if it is closed, begins one
transaction and runs the work. Any `ExecuteAsync` started while it runs, a nested
`[Transactional]` command or a package typed against `ITransactionalUnitOfWork`,
only runs its work: it never begins a second transaction and never commits.
After the outermost work returned, every enlisted context that still tracks a
change is saved, and the transaction commits once. A handler that calls
`SaveChangesAsync` writes into the open transaction; nothing is visible to
other connections before the outermost commit. An advisory lock taken with
`pg_advisory_xact_lock` in any enlisted context is therefore held until that
commit.

**A context resolved late enlists itself, as it initializes.** A context resolved
inside an open unit (the context of a module that only a nested command reaches)
takes the open transaction the moment EF Core initializes it, which is the first
time anything touches its `Database`, a query or a save. A store that asks
`Database.CurrentTransaction` before its first command, to join its caller's
transaction or begin one of its own, therefore finds the shared one and joins it.
The hook is EF Core's `ContextInitialized` diagnostic event, matched to the scope
through the interceptor `UseBsftSharedTransaction` adds to the context's options;
the interceptor also enlists right before the first command and before the first
save, for a context that initialized before the unit began. The save hook matters
because `SaveChanges` would otherwise open a transaction of its own, which the
providers refuse on a connection that already has one. When the unit ends, every
context gives the transaction back together with its hold on the connection, so a
query the context runs later opens and closes the connection itself, which
returns it to the pool. A context disposed inside the unit (one a handler created
from the options and disposed) is forgotten: its disposal already gave the
transaction back.

**A cleanup after the commit does not fail the unit.** Taking the transaction
back from the contexts and disposing it run whatever the other did. When either
fails after the commit, the failure is logged as a warning and the unit reports
success, because it persisted; reporting it as failed would make a caller, or a
retry, write it again.

**Any failure rolls back everything.** An exception out of the outermost work,
out of the final saves or out of the commit disposes the transaction uncommitted,
which rolls back every write of every context, including what a nested unit wrote
before it. The change trackers of all enlisted contexts are cleared, because they
still describe rows that no longer exist. A nested unit that failed marks the
transaction for rollback: if an outer handler catches that exception and returns
normally, the outermost unit throws `AmbientTransactionMisuseException` instead
of committing, because the store typically aborted the transaction with the
nested failure anyway. The first failure of a nested unit is its inner exception
(from 2.2.1-preview.2), and the provider's conflict classification looks at it, so
a `40001` or `40P01` that a nested unit raised without a failing EF Core command —
a raw command on the shared connection, an error while rows were read — and that
an outer handler caught still makes `[Retryable]` re-run the whole unit. The nested
failure counts only through that chain and ends with its unit: a handler that
catches it and then throws an exception of its own decides the outcome itself, and
only that exception is classified.

The same holds for a single failed or cancelled database command whose exception a
handler swallows. PostgreSQL aborts the transaction with the failure and answers
the later `COMMIT` with `ROLLBACK`, which Npgsql does not report as an error, so
the unit would otherwise return success for writes that were never persisted. The
enlisting interceptor records the first command failure or cancellation of the
open transaction (EF Core's `CommandFailed` and `CommandCanceled`), and the
outermost unit then throws `AmbientTransactionMisuseException` with that failure
as its inner exception. The provider's conflict classification looks at that recorded
failure too, so a swallowed `40001` or `40P01` still makes `[Retryable]` re-run the
unit, even when what the handler throws afterwards is PostgreSQL's "current
transaction is aborted".

One failure is not recorded: a `SaveChanges` that EF Core protected with its own
savepoint. Inside a transaction it did not open, EF Core creates a savepoint
before each save (`Database.AutoSavepointsEnabled`, on by default) and rolls back
to it when the save fails, so a handler that catches a unique violation from
`SaveChangesAsync` and carries on leaves the transaction committable, on SQLite
and on PostgreSQL alike. The unit commits in that case; what the failed save
wrote is gone, and the change tracker still holds the entities it could not save,
so the handler detaches them or the final save tries them again.

**The store is asked before the commit.** The interceptor sees only commands that
run through an enlisted context, and not even all of those: an error PostgreSQL
raises while EF Core reads the rows of a query (`ToListAsync` over a result that
fails at row 1000) reaches no interceptor at all, and neither does a command a
handler creates on the raw connection. So before it commits, the outermost unit
asks every registered `ITransactionAbortProbe` whether the store has already
aborted the transaction, and throws `AmbientTransactionMisuseException` with the
store's answer as inner exception instead of committing. `Bsft.Data.Postgres`
registers a probe that sends `SELECT 1` inside the transaction and reports the
`25P02` it gets on an aborted one; it costs one round trip per outermost unit,
0.3 ms against a local PostgreSQL 16, next to about 7 ms for a one-row insert and
its commit. A failure only the probe sees is not classified as a conflict: the
unit fails with `25P02` and is not retried, because the original error is gone.

`AmbientTransactionMisuseException` (`Bsft.Core.Persistence`) derives from
`InvalidOperationException`, what these failures were thrown as before, so an
existing `catch` keeps working. It has a type of its own so that the mediator's
retry behavior can leave it alone: a swallowed failure fails the same way on
every attempt, and a unit refused beside a running one would succeed in a fresh
scope and hide the bug. The one exception is the swallowed conflict above, which
is still retried through its inner exception.
Without a probe (the provider-agnostic registration), such a failure stays
invisible to the unit, exactly as it is to EF Core.

**Observing the unit's own connection steps** (from 2.2.1-preview.4). Opening
the connection, beginning the transaction, the probe, the commit, disposing the
transaction and closing the connection run on the `DbConnection` and
`DbTransaction` directly, so no EF Core interceptor sees them. A host that admits
one operation on the scope's connection at a time registers a scoped
`ISharedTransactionStepObserver` (`Bsft.Data.Transactions`), which is told
`OnStepStarting` and `OnStepCompleted` for each `SharedTransactionStepInfo`: its
`Kind` (`Open`, `Begin`, `AbortProbe`, `Commit`, `Rollback`, `Release`, `Close`)
and the shared `Connection`. `Open` and `Close` are reported only when the unit
opens the connection itself, `AbortProbe` only when a probe is registered,
`Rollback` is the dispose of a transaction that did not commit and `Release` the
dispose after a commit. Savepoints and the saves of the contexts go through EF
Core and are not reported. Steps never overlap, and only the outermost unit runs
them.

```csharp
services.AddScoped<ISharedTransactionStepObserver, ConnectionGuardObserver>();
```

Both calls are synchronous. Several observers are told in registration order and
completed in reverse; `OnStepCompleted` runs in a `finally` for every observer
whose `OnStepStarting` returned, also when the step failed or was cancelled. An
exception from `OnStepStarting` refuses `Open`, `Begin`, `AbortProbe` or `Commit`:
the step does not run and the exception propagates unchanged (not wrapped in
`AmbientTransactionMisuseException`); a refused commit rolls back like a failed
one. `Rollback`, `Release` and `Close` run anyway, so the transaction is always
released and the connection always closed, and the exception is logged as a
warning, never thrown: after a commit, throwing would report a persisted unit as
failed and let a retry write it twice, and a unit that did not commit already
fails with an exception of its own. `AbortProbe` is one step, however many probes
are registered. An exception from `OnStepCompleted` is logged and ignored.
Without an observer nothing changes.

An observer decides at once: it admits a step by returning and refuses it by
throwing. It never waits for another operation to end (no blocking `Wait`,
`Result` or lock held by foreign work), because both calls run synchronously on
the unit's own flow between the steps of an open transaction. Neither call may
use the scope's connection or transaction itself.

`Bsft.FeatureFlags` registers one observer of its own (since 2.2.1-preview.6): it
admits every step and uses `Begin`, `Rollback` and `Release` only to announce a flag
change made inside the unit after the commit, and never after a rollback (see
feature-flags.md).
A host that registers `AddBsftFeatureFlags` therefore always has an observer.

Once the outermost work has returned, the unit no longer observes the caller's
cancellation token (from 2.2.1-preview.2; before, only the commit itself did not):
saving what the contexts still track, asking the probe and committing all run with
`CancellationToken.None`. A caller that cancels after the work completed has
nothing left to stop, so the completed work commits; abandoning the commit halfway
would leave the caller unable to tell whether the unit persisted. A cancellation
that a command inside the work observes still fails the unit, and nothing commits.
Those final statements are then bounded only by the provider's command timeout
(`CommandTimeout`, 30 seconds by default on Npgsql), not by the caller's token: a
context configured with `CommandTimeout = 0` (no limit) can wait on a lock in the
final save indefinitely. Keep a finite command timeout, or save explicitly inside
the work, where the caller's token still applies.

**Retries are whole units, each in a fresh scope.** With `[Retryable]` from
[`Bsft.Resilience.Mediator`](resilience.md#mediator-retry-optional-package), a failed
outermost attempt is retried in a new dependency-injection scope: a new
connection, new contexts and a new transaction, with nothing the failed attempt
left behind in a scoped service. A host carries the state of the request's scope
(the tenant, the current user) into the fresh one with an `IRequestScopeSeed`
([mediator.md](mediator.md#rescoping-behaviors)). A nested request is never
retried on its own, because a retry inside an aborted transaction cannot
succeed. The provider classifies which failures are a conflict
with a concurrent transaction through `ITransactionConflictClassifier`;
`Bsft.Data.Postgres` registers one for `40001` (serialization failure) and
`40P01` (deadlock), and the retry behavior retries such a failure whatever its
own predicate says. A retried unit runs its whole work again, so everything it
does outside the database (a mail, an HTTP call) must be idempotent or deferred
to an outbox; the database writes of the failed attempt are rolled back.

**The isolation level** is `BsftDataOptions.SharedTransaction.IsolationLevel`
(`Bsft:Data:SharedTransaction:IsolationLevel` where the section is bound),
`Unspecified` by default, which is read committed on PostgreSQL. `Serializable` or
`RepeatableRead` make PostgreSQL refuse a conflicting commit with `40001`, which is
what the retry above is for.

```csharp
services.Configure<BsftDataOptions>(o => o.SharedTransaction.IsolationLevel = IsolationLevel.Serializable);
```

What it requires and what it does not do:

- **One unit at a time per scope.** The scope's transaction belongs to the
  async flow that opened it. A unit started from that flow while it runs joins
  it; a unit started from another flow of the same scope at the same time (two
  requests started with `Task.WhenAll` on one scope) throws
  `AmbientTransactionMisuseException` instead of silently joining a transaction
  it does not own. Run parallel work in scopes of its own.
- **Registered before it is enlisted.** `UseBsftSharedTransaction` throws an
  `InvalidOperationException` naming the context when neither
  `AddBsftDataSharedTransaction` nor `AddBsftDataPostgresTransaction` is
  registered.
- **Scoped, unpooled contexts.** The options of an enlisted context are built per
  scope, which `AddDbContext` does by default. `AddDbContextPool` and
  `AddDbContextFactory` build them once, from the root provider, for every scope
  and would hand one connection and one transaction to all of them;
  `UseBsftSharedTransaction` therefore throws when it is called with the root
  provider. The context is recorded under the type its options name, so register
  it under its own type.
- **No `EnableRetryOnFailure` on an enlisted context.** A retrying execution
  strategy refuses a transaction it did not open, and retrying one statement of a
  shared transaction is wrong anyway. Enlisting such a context throws an
  `AmbientTransactionMisuseException` naming it; retry with `[Retryable]`
  instead.
- **Not together with `AddBsftDataUnitOfWork<TContext>`.** The single-context
  unit would begin a second transaction on the shared connection. A startup
  validator fails when both are registered; the shared transaction already
  provides `IUnitOfWork` and `ITransactionalUnitOfWork`.
- **EF Core interceptors keep working.** Save interceptors such as the tenant
  `TenantSaveChangesInterceptor` and the audit outbox interceptor of
  `Bsft.Auditing.EFCore` run on every save as before, and their rows commit or roll
  back with the unit. EF Core raises no transaction events for a transaction
  handed to a context from outside, so a component that waits for a context's own
  commit event, such as the deferred cache invalidation of
  `Bsft.Organization.EFCore`, does not see this one and keeps refusing to run
  inside it.
- **No savepoints of its own.** A nested unit is all or nothing with its outer
  unit. EF Core still creates its own savepoint around each `SaveChanges` inside
  the transaction, as it does for any transaction it did not open, which is what
  lets a caught save failure leave the unit committable (above).
- **Modules that begin a transaction unconditionally stay outside.** The stores
  that join a caller's transaction when `Database.CurrentTransaction` is set work
  on an enlisted context: `Bsft.Hierarchy.EFCore`, `Bsft.Organization.EFCore`,
  `Bsft.Billing.EFCore`, `Bsft.Chat.EFCore`, `Bsft.DataTransfer.EFCore`, the
  directory invitations of `Bsft.Auth.Oidc.Organization.EFCore` and the stores of
  `Bsft.AI.Agents.EFCore`, `Bsft.AI.Memory.EFCore` and `Bsft.AI.Skills.EFCore`.
  The following begin a transaction of their own at a fixed isolation level
  whatever the context already has, and fail on an enlisted context, because the
  providers refuse a second transaction on the connection: the credential ledger
  of `Bsft.Auth.Credentials.EFCore`, the recovery codes of
  `Bsft.Auth.Factors.EFCore`, the token redemption of `Bsft.Security.EFCore`, the
  subscription cap of `Bsft.Webhooks.EFCore` and the work claim of
  `Bsft.Jobs.EFCore`. Register their contexts without `UseBsftSharedTransaction`;
  they then commit on a connection of their own, independently of the unit.

With the mediator, `TransactionBehavior` runs every `[Transactional]` request as a
unit of this transaction and no longer needs a base `DbContext` registration to
pick a context; see [mediator.md](mediator.md#transactions).

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

## Keyset (cursor) paging

Offset paging reads and throws away every row before the page, so a deep page on a large table is
slow, and a row inserted meanwhile shifts every later page by one. A listing of a large or growing
table pages by keyset instead: the next page starts strictly after the last row of the previous one,
in a fixed order on a sort key with the row's id as the tie-breaker. It pages forward only.

```csharp
public record CursorPagedRequest(string? Cursor = null, int PageSize = 25)
{
    public bool IncludeTotalCount { get; init; }   // off by default: counting costs what keyset saves
}

public record CursorPagedResult<T>(IReadOnlyList<T> Items, int PageSize, string? NextCursor)
{
    public long? TotalCount { get; init; }          // only when the request asked for it
}
```

Both live in `Bsft.Data.Contracts` beside `PagedRequest` and `PagedResult<T>`, which are unchanged.
On the wire a page is `{ "items": [...], "pageSize": 25, "nextCursor": "...", "totalCount": 9 }`;
`nextCursor` and `totalCount` are left out when null, so the absence of `nextCursor` is what marks the
last page. A client passes `nextCursor` back as `cursor`, unchanged, and starts with none.

The order of a listing is declared once, and the reader applies it:

```csharp
private static readonly KeysetOrder<Reading, DateTime, long> ByRecordedAt =
    new("readings.by-recorded", r => r.RecordedAt, r => r.Id);   // SortDirection.Desc for newest first

services.AddBsftDataKeysetPaging();   // or (configuration), binding Bsft:Data:KeysetPaging (MaxPageSize)

CursorPagedResult<Reading> page = await reader.ListAsync(   // IKeysetPageReader
    db.Readings.AsNoTracking().Where(r => r.MeterId == meterId),
    ByRecordedAt,
    new CursorPagedRequest(cursor, pageSize),
    cancellationToken);
```

The query is narrowed and ordered with plain LINQ, so every EF Core provider translates it and no SQL
is written by hand. On SQLite it reads

```sql
WHERE "r"."RecordedAt" > @Key OR ("r"."RecordedAt" = @Key AND "r"."Id" > @Id)
ORDER BY "r"."RecordedAt", "r"."Id"
```

with `<` and `DESC` for a descending order; a string key is compared through
`string.Compare(a, b) > 0`, which EF Core turns into `a > b`. The position is a parameter, never a
literal. One row more than the page size is read to learn whether a next page exists, so no count is
needed for that; the total is a separate `COUNT` of the whole (filtered) query and only runs when
asked for. An index on `(key, id)` is what makes the page cheap.

The page size is the client's: a size above `KeysetPagingOptions.MaxPageSize` (default 100, the
offset surface's maximum, validated at start-up) is clamped to it and the page reports the size it
applied, and a size below 1 is refused with a `ValidationException` keyed on `pageSize` and carrying
`KeysetPagingErrorCodes.InvalidPageSize` (`KEYSET_INVALID_PAGE_SIZE`), a 400.

**The cursor is opaque.** `IKeysetCursorSerializer` writes base64url without padding over a versioned
binary payload: the format version, a type code and the invariant text of the key and the id, and a
16-byte SHA-256 checksum over the payload and the listing name. Reading checks all of it before any
query runs, and a cursor that is malformed, altered, of an unknown version, of other key or id types,
of another listing, or longer than 1024 characters is refused with a `ValidationException` keyed on
`cursor` and carrying `KeysetPagingErrorCodes.InvalidCursor` (`KEYSET_INVALID_CURSOR`), which
`BsftExceptionMiddleware` answers with a 400. The checksum is not a signature: a client that recomputes
it can name any position of a listing, which grants nothing, because a position only says where a page
starts in rows the query already lets the caller read. It does mean the 400 is guaranteed for a
cursor that is malformed or altered, not for one forged with a recomputed checksum: such a cursor can
carry any value of the key type, `DateTime.MinValue` for one, and a column type narrower than the CLR
type (SQL Server's `datetime`, a `decimal(18,2)`) can then fail in the provider. A cursor also carries
the key and id of a page's last row readably once decoded, so do not key a listing on a column the
response hides. An empty cursor is refused rather than read as the first page.

Supported key and id types are `string`, `Guid`, `int`, `long`, `decimal`, `DateTime` (its kind is
kept), `DateTimeOffset` and `DateOnly`; another type is refused with `NotSupportedException` before
the query runs. Neither may be nullable, and the id must be unique within the listing. The key must be
a column the provider can order: SQLite, for one, refuses to order by `decimal` or `DateTimeOffset`.
The key and the id together have to fit 745 UTF-8 bytes, what a 1024-character cursor leaves
beside its header and checksum; writing a longer one fails with `InvalidOperationException`, so a string key or id needs a
bounded column length.
Changing the listing name or the order invalidates the cursors clients hold.

A reader that composes its own keyset query uses `IKeysetCursorSerializer` directly to read the
position out of a cursor and to write the next one.

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
StartsWith, NotStartsWith, EndsWith, NotEndsWith, Present, In`, serialised as `eq`,
`neq`, `gt`, `gte`, `lt`, `lte`, `contains`, `!contains`, `startsWith`,
`!startsWith`, `endsWith`, `!endsWith`, `pr`, `in` (`In` from 2.2.1-preview.3, see
[Set membership](#set-membership-in)).

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

### Missing values

What "not set" means is fixed once for every evaluator, in memory and in SQL
alike, and the property tests of `Bsft.Data.Postgres.Tests` hold the two to it:

| Filter | A field that is not set | A field with a value |
| --- | --- | --- |
| `{field, eq, null}` (canonical form of "not set") | matches | does not match |
| `{field, neq, null}`, same as `pr` | does not match | matches |
| `{field, neq, v}` | matches | matches unless the value is `v` |
| `{field, eq, v}`, `gt`, `startsWith`, ... | does not match | compares |
| `{field, in, [v, ...]}` | does not match | matches when the value is one of them |

On a declared nullable column "not set" is `NULL`, and EF Core's C# null semantics
give `neq v` the `OR col IS NULL` it needs. On a [dynamic field](#dynamic-fields)
it is whatever the resolver's presence test says is absent; the compiler builds
the rest of the table from that test, so no resolver can get it wrong.

### Set membership (`in`)

`{"$type": "comparison", "field": "status", "op": "in", "value": ["open", "held"]}`
matches a row whose field equals one of the values. It means exactly what an
`or` over one `eq` per element means, in memory and in SQL, and the values are
always parameters, never literals:

- A field compared without case folding (a non-string, or a string under
  `FilterStringMatch.Collation`) is sent as one parameterized collection.
  PostgreSQL (Npgsql) reads it as `= ANY(@p)` on EF Core 9 and 10; SQL Server
  and SQLite read it as one parameter per value on EF Core 10 (padded to a
  bucket size), as a JSON array parameter on EF Core 9.
- A string under the default `FilterStringMatch.LowerInvariant` is compiled as
  the `or` over `eq` itself, each element folded exactly as `eq` folds its value,
  so a set matches what `eq` matches under every culture (`"I"` under tr-TR
  included). As with `eq ""`, `in [""]` matches a `NULL` column in this mode.

Two rules keep it unambiguous:

- **The set is never empty and holds no `null`.** An empty array is a 400 with
  `errorCode` `FILTER_IN_EMPTY`, a `null` element a 400 with
  `FILTER_IN_NULL_ELEMENT` (both keyed on `filter`). "Not set" has one spelling,
  `{field, eq, null}`, combined with `or` where a set should include it. Every
  evaluator reads the set through `FilterInValues.Read`, so the refusal is the
  same in memory, in SQL and in the workflow inbox. Either rule can be relaxed
  later without breaking a client; tightening it could not.
- **A field accepts `in` only when it names it.** A field declared without
  `AllowFilter` accepts every other operator, as it always has, but not `in`:
  `.AllowFilter(FilterOperator.Eq, FilterOperator.In)` opts a field in.
- **A set is bounded.** One set holds at most 100 values and all sets of one
  filter together at most 500; beyond either it is a 400 with `errorCode`
  `FILTER_IN_TOO_MANY`, the limit in the message. The total keeps many small
  sets from sending what one large set may not. A configuration sets both with
  `builder.MaxInValues(n)` and `builder.MaxInValuesPerFilter(n)`; the total is
  bounded in turn by the provider (SQL Server accepts 2100 parameters per
  command). The limits are checked on the whole filter before anything is built,
  whichever transport delivered it.

An element of the wrong type for the field (`"abc"` for an `int`) is a 400 too.

## Dynamic fields

Fields that no DTO property declares, such as a tenant's configured extension
fields, join the same whitelist through a prefix and a resolver:

```csharp
public override void Configure(QueryConfigurationBuilder<Party, PartyDto> builder)
{
    builder.Field(d => d.Name).AllowFilter(FilterOperator.Eq).AllowSort();
    builder.DynamicFields("custom.", _customFieldResolver);   // IDynamicFieldResolver<Party>
}
```

Every filter field and sort field whose name starts with the prefix (matched
case-insensitively) and is not a declared field is resolved on each request by
`IDynamicFieldResolver<TEntity>.Resolve(key)`, where `key` is the name without
the prefix. The resolver answers with a `DynamicField<TEntity>`, or `null` for a
field it does not know, which is the same 400 an undeclared field is and gives no
hint that the field exists for someone else. Because the configuration lives for
the application, a resolver is thread-safe and reads request state (tenant,
caller) from an ambient accessor. The key is client input as the request spelled it: a
resolver matches it against its field definitions and never puts it into SQL
text or a JSON path; where it has to reach the query, it goes in as a value,
which EF Core sends as a parameter.

A `DynamicField<TEntity>` holds a presence test, one predicate builder per
positive operator (`eq`, `gt`, `gte`, `lt`, `lte`, `contains`, `startsWith`,
`endsWith`, `in`) taking the filter value as a `JsonElement`, and an
optional sort key. The compiler composes the rest, so the
[missing-value table](#missing-values) holds for every resolver:

- `pr` and `{field, neq, null}` are the presence test, `{field, eq, null}` its negation;
- a positive operator is `present AND predicate`, so a predicate is only asked
  about rows that hold a value, and a SQL predicate that yields `NULL` on a
  missing key cannot leak into the result;
- a negated operator (`neq`, `!contains`, `!startsWith`, `!endsWith`) is
  `NOT present OR NOT predicate` of its positive counterpart, and is allowed
  exactly when that counterpart is;
- `in` is offered only when the field names it, as a declared field offers it
  only when `AllowFilter` does. A field whose set membership is plain equality
  names it with `eqBuilder.ToInPredicate()`, an `or` over its `eq` builder; a
  field that can test a set in one step (`= ANY`, one containment) supplies its
  own builder. A resolver with only `eq` answers `in` with a 400.

A declared field whose name starts with a dynamic prefix is refused when the
configuration is built, and so are two prefixes of which one starts with the
other. A dynamic field sorts by its sort key; without one, sorting on it is the
same 400 as on a declared field without `AllowSort()`. The search executor
filters and sorts through the same field map, so dynamic fields work there too.

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

On .NET 10 (EF Core 10) the tenant filter can also be a **named** query filter.
EF Core 10 refuses to mix an anonymous filter with named ones on one entity type
(`AnonymousAndNamedFiltersCombined`), so a model that adds a soft-delete or any
other filter by name applies the tenant filter by name as well:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Order>().HasQueryFilter("soft-delete", o => !o.IsDeleted);
    modelBuilder.UseNamedTenantFilter(this);              // named "bsft.tenant"
    // modelBuilder.UseNamedTenantFilter(this, "tenant"); // or a name of your own
}

// step around the tenant filter alone; the soft-delete filter still applies
db.Orders.IgnoreQueryFilters([BsftDbContextOptionsExtensions.TenantFilterName]);
```

`UseNamedStaticTenantFilter(tenantContext)` is the named form of
`UseStaticTenantFilter`. `UseTenantFilter(this)` keeps applying the anonymous
filter it always did.

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

## Tenant keys

A host that keeps every table of a tenant database tenant-scoped (a non-null
`TenantId` on every table, a key of a Guid or `(TenantId, Id)`, no identity column) can switch the framework's own
tables to that shape. The switch is one call per context, in `OnModelCreating`, **before** the framework
configurations are applied:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.UseTenantKeys();                                     // Bsft.Data.MultiTenancy

    modelBuilder.ApplyConfiguration(new AuditOutboxEntityConfiguration("\"DeadLetteredAt\" IS NOT NULL"));
    modelBuilder.ApplyConfiguration(new AuditLogEntityConfiguration());
    modelBuilder.ApplyConfiguration(new IdempotencyRecordEntityConfiguration());
    modelBuilder.ApplyConfiguration(new OutboxMessageEntityConfiguration());
    modelBuilder.ApplyConfiguration(new InboxMessageEntityConfiguration());
    // Bsft.Workflow.EFCore and Bsft.FeatureFlags.EFCore: their configurations follow the same switch
}
```

and, once per host and required with `UseTenantKeys`, the registration that carries the node id of its ids:

```csharp
services.AddBsftDataTenantKeys(o => o.NodeId = replicaOrdinal);       // 0..4095, Bsft:Data:TenantKeys:NodeId
```

`UseTenantKeys` written after a framework configuration throws, naming the entity types already mapped without
tenant keys, so a late call cannot leave a table behind unnoticed. `UseTenantKeys` without `AddBsftDataTenantKeys` is
refused at start: a multi-tenancy host (`AddBsftDataMultiTenancy`) with `AddBsftStartupValidation` fails with a
message naming the context and the missing call. A context that does not call it keeps every table
exactly as before, and its rows keep their identity `long Id`.

What the switch changes:

| Table | Without `UseTenantKeys` | With `UseTenantKeys` |
| --- | --- | --- |
| `AuditLog` | identity `Id`, nullable `TenantId` | key `(TenantId, Id)`, `TenantId` required |
| `AuditOutbox` | identity `Id`, no `TenantId` | key `(TenantId, Id)`; poll index led by `TenantId` |
| `IdempotencyRecords` | identity `Id`, unique `(Scope, Key)` | key `(TenantId, Id)`; unique `(TenantId, Scope, Key)`; `(TenantId, ExpiresAt)` |
| `OutboxMessages` | identity `Id` | key `(TenantId, Id)`; claim index led by `TenantId` |
| `InboxMessages` | identity `Id`, unique `(Consumer, EventId)` | key `(TenantId, Id)`; unique `(TenantId, Consumer, EventId)` |
| `WorkItems`, `WorkflowDelegations` | Guid key, nullable `TenantId` | Guid key, `TenantId` required |
| `WorkflowStepInstances`, `WorkflowAssignments`, `WorkflowDecisions`, `WorkItemComments` | Guid key, no `TenantId`; foreign key to the parent's `Id` | Guid key, `TenantId` required (the work item's); foreign key `(TenantId, <parent>Id)` to the parent's alternate key `(TenantId, Id)` |
| `FeatureFlags` | Guid key, unique `Key` | Guid key, `TenantId` required; unique `(TenantId, Key)` |
| `FeatureFlagRules`, `FeatureFlagOverrides` | Guid key, no `TenantId`; foreign key `FlagId` | Guid key, `TenantId` required (the flag's); foreign key `(TenantId, FlagId)` to the flag's alternate key `(TenantId, Id)` |
| `FeatureFlagChanges` | Guid key, `(FlagKey, OccurredAt)` | Guid key, `TenantId` required; `(TenantId, FlagKey, OccurredAt)` |

`TenantId` is `varchar(128)` wherever it is required. The parents a child table references (`WorkItems`,
`WorkflowStepInstances`, `WorkflowAssignments`, `FeatureFlags`) get the alternate key `(TenantId, Id)` that the
composite foreign key points at, so the database refuses a child whose tenant differs from its parent's.

**How a row gets its keys.** The `Id` is filled on `Add` by the registered `ITimeOrderedIdGenerator`
(`Bsft.Core.Persistence`): 41 bits of milliseconds since 2025-01-01, 12 bits of node id and 10 bits of sequence. One
generator's values rise strictly, also under parallel calls and when the clock steps back, and more than 1024 values
in one millisecond carry into the next one, so the ids are ordered by the time they were made in that process. The
`TenantId` is the writer's where it knows one (an audit event's tenant, a work item's), and otherwise the scope's:
the resolved `ITenantContext`, or inside a background pass `BackgroundTenant.Current`. A row written in a scope with
no tenant fails at `Add` instead of being stored without an owner. A child row never takes a tenant of its own: the
workflow store gives every row of an aggregate the tenant of its work item, the feature-flag store gives rules and
overrides the tenant of their flag.

**How the stores read it.** Every store reads the decision back from the model (`IsTenantKeyed()` on the entity type
or the set), so code and schema cannot disagree. Under tenant keys the idempotency store, the inbox store and the
feature-flag stores put the scope's tenant in every statement and refuse a scope without one, with one exception:
the flag catalog (`ListAllAsync`) read without a tenant is empty, so the untenanted snapshot and the start check of
a host without tenant source see no flags rather than failing. A host with tenant-keyed flag tables sets
`BsftFeatureFlagsOptions.IsSnapshotPerTenant = true`; the outbox dispatchers, sweepers and the audit-log
retention pass restrict a pass that runs for one tenant to that tenant's rows. A pass without a tenant addresses an
audit outbox row by `(TenantId, Id)`, holds a messaging outbox claim to the claimed row's tenant, and repeats a
sweep's expiry condition in its delete, so a row of another tenant that happens to share an id is never touched.

**Several processes.** Ids of one process never collide; ids of processes with different node ids never collide. A
host that runs several processes against one database gives each its own `NodeId`. With `NodeId` unset each
process draws a node id at random, which is enough for one process but not a guarantee
for several. Two processes that share a node id can make the same id in the same millisecond; because the id is
part of the primary key, the second insert then fails with a key violation, it never overwrites a row. The host
logs a warning at start when `AddBsftDataTenantKeys` is called without a `NodeId`.

**Restarts.** The sequence counter lives in memory and is not persisted. A process that restarts with the same
`NodeId` starts again from the clock, so it repeats ids only if its clock now stands at or before the last
millisecond the previous run used: after the clock was set back, or when the previous run had carried more than 1024
ids per millisecond into the future (a burst) and the restart came before that lead was used up. Such a repeat fails
the insert on the primary key, it does not overwrite. Keep the clock synchronised (NTP slews rather than steps) and do
not move a node id to another process while the old one may still be writing.

**Start-up checks.** A tenant-keyed context must be created in the request's or the pass's scope, because that is
where its `TenantId` comes from. A context registered with `AddDbContextPool`, `AddDbContextFactory`,
`AddPooledDbContextFactory` or with options for the lifetime of the application is created from the root provider
instead and would write rows under whatever tenant the root resolves, which can be another tenant's, without any
error. `TenantKeyStartupValidator` (registered by `AddBsftDataTenantKeys` only, run by `AddBsftStartupValidation`; it
reads the lifetime from the registrations and builds only the contexts whose options live for the application) therefore refuses to start a host with such a registration, naming the context; register
tenant-keyed contexts with `AddDbContext`. `TenantKeyRegistrationStartupValidator` (registered by
`AddBsftDataMultiTenancy`) refuses a context mapped with `UseTenantKeys` when `AddBsftDataTenantKeys` was not
called; once it was, the check does nothing. Until then it builds every registered context once at start to read
its model; a context whose construction throws `InvalidOperationException` or `ArgumentException` is passed without
a warning, any other exception fails the start. The feature-flag start check reads the stored rules once
per tenant of the registered `IBackgroundTenantSource` only when the flag store keeps its flags per tenant
(`IFeatureFlagStore.IsKeptPerTenant`, true under tenant keys) and then names the tenant of every rule type it cannot
evaluate. Otherwise it reads the catalog once, as without tenant keys, and names each rule once; without a source a
tenant-keyed catalog read outside a tenant is empty.

**Migrations.** The framework ships no migrations; a host adds them for its own contexts. For a database that is
created with the switch on, the migration is the plain `CreateTable`. Converting an existing table means adding
`TenantId`, filling it, making it `NOT NULL`, replacing the primary key and the indexes listed above, and dropping the
identity (`ALTER TABLE … ALTER COLUMN "Id" DROP IDENTITY` on PostgreSQL); existing ids stay valid, because the ids made
now are above 2⁵⁷ and therefore larger than any identity value an existing table holds. See migration-2.2.1-preview.2.md.

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
| `Bsft.Data.Contracts` | `PagedRequest`, `PagedResult<T>`, `CursorPagedRequest`, `CursorPagedResult<T>`, `SortItem`, `SortDirection`, the filter AST (`FilterNode`, `AndNode`, `OrNode`, `ComparisonNode`, `PresenceNode`, `FilterOperator`), `SearchRequest`, `SearchMode`, `RankedItem<T>` | nothing at all |
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
