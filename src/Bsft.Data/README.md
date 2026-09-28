# Bsft.Data

The query layer of a Bsft application: a whitelisted filter, sort and pagination surface for list
endpoints, index-backed full-text search declared beside it, and multi-tenancy enforced in the
database rather than remembered at every call site.

## Install

```bash
dotnet add package Bsft.Data
```

Requires `Bsft.Data.Contracts` (pulled transitively; it carries the wire types a client shares),
`Bsft.Core`, `Bsft.Instrumentation`, `Microsoft.EntityFrameworkCore` with its relational
package, and `Microsoft.AspNetCore.Http.Abstractions`. Full-text search additionally needs one of the
provider packages — `Bsft.Data.Postgres`, `Bsft.Data.SqlServer` or `Bsft.Data.Sqlite` — which supply
the `ISearchProvider` this package validates and calls.

## Register

```csharp
services.AddBsftDataQueryFiltering(cfg => cfg.FromAssembly(typeof(ProductQueryConfiguration).Assembly));

services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());

// or with the header name and claim type bound from configuration, section "Bsft:Data"
services.AddBsftDataMultiTenancy(configuration, cfg => cfg.ResolveFromHeader());

// the framework's unit of work over your own context: IUnitOfWork + ITransactionalUnitOfWork
services.AddBsftDataUnitOfWork<AppDbContext>();

// hand out a tenant identifier nothing else uses, and take it back again
services.AddBsftSandboxTenants();
services.AddSingleton<ISandboxTenantRegistry, MyRegistry>();   // required, and yours
```

The unit of work is what the packages that *stage* rather than write commit through — the API key
expiry reminders and the notification digest sweep among them. A commit is the context's
`SaveChangesAsync`; a unit is one transaction on its connection, run through the provider's execution
strategy and joining a transaction the host already opened rather than nesting a second one. On a
non-relational provider the work simply runs. Registered with `TryAdd`, so a host with a unit of work
of its own keeps it.

The first two are independent: a single-tenant application still filters and searches, and a multi-tenant
one that exposes no list endpoint still has to scope its writes. Multi-tenancy needs three more things
from the application: `TenantMiddleware` in the pipeline, so the tenant is resolved before a request
touches the database, `modelBuilder.UseTenantFilter(this)` in the context's `OnModelCreating`,
which is what makes the isolation a query filter rather than a convention, and the save guard on the
context's options, which is the write half of the same isolation:

```csharp
services.AddDbContext<AppDbContext>((sp, options) => options
    .UseSqlServer(connectionString)
    .AddInterceptors(sp.GetRequiredService<TenantSaveChangesInterceptor>()));
```

Leaving that call out is the one omission nothing at runtime reports: reads stay filtered while inserts
are written with no tenant and an update moving a row to another tenant is not refused. A startup
validator therefore fails the host when a context implementing `ITenantScopedDbContext` carries no such
interceptor. Adding it in the context's own `OnConfiguring` satisfies the check just as well.
Registering it in the container as an `IInterceptor` or `ISaveChangesInterceptor` does not, and is not
a way to wire it at all: EF Core does not resolve an interceptor from the application's container, so
one registered only there never runs. A context the check cannot read at startup, because constructing
it needs something the host does not have that early, is reported as a warning and passed rather than
failed, and one reached only through `AddDbContextFactory` may go unexamined.

The context implements
`ITenantScopedDbContext` for the filter, so the filter reads the tenant off the context executing the
query instead of capturing one scope's `ITenantContext` — EF Core caches the model for the life of the
process, and a captured tenant would still be the first request's tenant on every later one. Register
such a context with `AddDbContext`, never `AddDbContextPool`: a pooled instance keeps the
`ITenantContext` it was constructed with and freezes on the first tenant that rented it.
`AddBsftDataMultiTenancy` registers a startup validator that fails the host on that combination.

`AddBsftSandboxTenants` registers `ISandboxTenantProvisioner` and nothing to remember what it handed
out: a startup validator fails the host until an `ISandboxTenantRegistry` is registered, because an
in-memory default would look like the seam working and would hand out a colliding identifier the first
time the process recycled. A sandbox tenant is entered with the same `EnterTenant` as any other, and
teardown removes the registry entry plus whatever each registered `ISandboxTenantCleanup` speaks for.

## Configure

`BsftDataOptions.SectionName` is `"Bsft:Data"`. `BsftSandboxTenantOptions.SectionName` is
`"Bsft:SandboxTenants"` and carries `IdPrefix` (`sandbox`), `DefaultLifetime` (7 days) and
`MaxProvisionAttempts` (5).

| Option | Default | Effect |
| --- | --- | --- |
| `BsftDataOptions.MultiTenancy.HeaderName` | `"X-Tenant-Id"` | The request header `HeaderTenantResolver` reads the tenant from. |
| `BsftDataOptions.MultiTenancy.ClaimType` | `"tenant_id"` | The claim type `ClaimTenantResolver` reads the tenant from. |

Both defaults are `DataDefaults.TenantHeaderName` and `DataDefaults.TenantClaimType`, so the
authentication stack and the resolver name the same claim without repeating the literal.

Validation: the header name must not be blank and must be a valid HTTP field name — a name a request
cannot carry would leave the tenant silently unresolved — and the claim type must not be blank. A
failure names the member and the offending value, and it is thrown from `AddBsftDataMultiTenancy` as
well as on start.

A name stated in code (`ResolveFromHeader("X-Account")`) is applied after the section is bound and
therefore wins over it. Passing no argument leaves whatever the section says.

The other two configuration surfaces are deliberately not options types and have no section:
`QueryFilteringConfiguration` carries assemblies to scan and `MultiTenancyConfiguration` selects a
resolver type, and neither an assembly nor a type is a value a configuration provider can supply.

## Extend

`ITenantResolver` decides where the current tenant comes from — `ResolveFromHeader`,
`ResolveFromClaim` and `ResolveFromSubdomain` select the three shipped ones, `ResolveUsing<T>()`
registers an application's own. `ISearchProvider` translates a search into the database's own
full-text operator; the three provider packages implement it and a host may add another.
`ISandboxTenantRegistry` remembers the sandbox tenant identifiers `AddBsftSandboxTenants` handed out
and has no default — startup fails without one, because an identifier is only isolated if something
durable remembers it was given away — and each `ISandboxTenantCleanup` removes one package's rows
when a sandbox is taken away. `ISearchScopeProvider` supplies the tenant and org-unit scope a search
is narrowed to and has no package default: this package does not reference `Bsft.Organization`, so a
host declaring a scoped search group registers one, and startup says so when none is there.
`IDataTelemetry` receives the search metrics and is registered with `TryAdd`, so a host's own
registration wins. `QueryConfigurationBase<TEntity, TDto>` is the extension point an application
writes against: one subclass per resource, discovered by the assembly scan.

## Documentation

Topic page: [docs/data.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/data.md).
Search: [docs/search.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/search.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
