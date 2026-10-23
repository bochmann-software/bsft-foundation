# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project uses a single `BsftFoundationVersion` across all packages.

## [Unreleased]

## [2.2.0]

A minor release. From this version on the Foundation packages and the commercial framework built on them carry
the same version number. The previews of this release were published as 2.1.1-preview.1 to 2.1.1-preview.5; 2.1.1
itself is not released.

### Breaking

- `AddBsftBackgroundWorkRunOnce` moved with its class `BackgroundWorkServiceCollectionExtensions` from
  `Bsft.Hosting.DependencyInjection` to `Bsft.Hosting.BackgroundWork.DependencyInjection`: a source and a binary
  break for its callers, which recompile with the new `using` (see `Bsft.Hosting.BackgroundWork` below).
- `UseBsftRateLimiting` requires `AddBsftRateLimiting` (or `AddBsftDefaults`), and `RateLimiterOptions.GlobalLimiter`
  is disposed with the host, so a global limiter belongs to one host. A 429 carries `Retry-After` and a
  ProblemDetails body instead of an empty body.
- `LogFileEntry.CorrelationId` is `null` for an entry logged outside any correlation scope instead of `-`.
- The `net10.0` groups depend on EF Core 10.0.x instead of 9.0.19, and on `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 instead
  of 9.0.4.

### Added

- **Rate limiting in `Bsft.Hosting`: named policies, principal partitions, concurrency limiters and `Retry-After`.**
  `BsftRateLimitingOptions.Policies` (`Bsft:Hosting:RateLimiting:Policies:<name>`) adds named policies next to the
  unchanged default `BsftFixedWindow`, each a chain of fixed window, sliding window, token bucket or concurrency
  limiters partitioned by client address, principal claim, a custom key or globally. A 429 carries `Retry-After` and a
  ProblemDetails body with `errorCode` `RATE_LIMITED`; `bsft.rate_limiting.rejections` counts rejections per policy.
  See docs/hosting.md, "Rate limiting".
- **`Bsft.Hosting.BackgroundWork`.** The background-work pieces of `Bsft.Hosting` (`RunOnceWithRetryBackgroundServiceBase`,
  `BsftRunOnceOptions`, the `IServiceScopeFactory` helpers and the bounded pipelines) now ship in their own package,
  which depends on `Bsft.Instrumentation` and the generic host only: no ASP.NET Core, Swashbuckle, Asp.Versioning or
  OpenAPI, so a console tool takes the pipelines without the web API stack. `Bsft.Hosting` references it. Namespaces of the
  types are unchanged; the registration classes `BackgroundWorkServiceCollectionExtensions` and
  `PipelineServiceCollectionExtensions` moved to `Bsft.Hosting.BackgroundWork.DependencyInjection`, so a caller adds
  that one `using`. `Bsft.Hosting` forwards `RunOnceWithRetryBackgroundServiceBase`, `BsftRunOnceOptions` and
  `ServiceScopeFactoryExtensions` to the new package, so binaries compiled against `Bsft.Hosting` 2.1.0 that use them
  keep running. The namespace move of `BackgroundWorkServiceCollectionExtensions` cannot be forwarded: it is a source
  and binary break for callers of `AddBsftBackgroundWorkRunOnce`, which recompile with the new `using`.
  `BackgroundWorkDefaults.PipelineInstrumentationSourceName` owns the source name (`Bsft.Hosting`);
  `HostingDefaults.PipelineInstrumentationSourceName` keeps the same value. See docs/hosting.md, "Background work".

- **Bounded pipelines in `Bsft.Hosting.BackgroundWork`.** `Bsft.Hosting.BackgroundWork.Pipelines` hands items from a
  hot path to a batching background writer without ever blocking the producer: `IBoundedBuffer<TItem>`
  drops and counts when full, a hosted worker commits batches through `IBatchCommitter<TItem>`
  (a failing batch is logged, counted and skipped; the final flush on stop is bounded by
  `FlushOnStopTimeout`), and an optional enrichment stage with a per-item timeout sits in front
  (`IEnrichmentStage<TItem>`, `IPipelineEnricher<TItem>`; a fault, a `null` or a hang passes the raw
  item on). Registered with `AddBsftBatchingWriter` and `AddBsftPipelineEnrichment`, read through
  `IPipelineCounters<TItem>` and emitted as `bsft.pipeline.items`. `Bsft.Hosting` now references
  `Bsft.Instrumentation`. See docs/hosting.md, "Bounded pipelines".
- **Observers in `Bsft.Core` and `Bsft.Hosting`.** `Bsft.Core.Observers` keeps an observer from
  breaking the operation it watches: `ObserverComposite<TObserver>` tells every observer in
  registration order (`Notify`, `NotifyAsync`, which checks the token before each observer),
  `IObserverGuard`/`ObserverGuard` contains the failure of an observer call, counts it (`Failures`) and
  hands it to an `IObserverFailureReporter`, counting a reporter that throws in `ReporterFailures`, so
  that `Bsft.Core` takes no logging dependency. A cancellation of the observed operation is not
  contained. `Bsft.Hosting` adds `AddBsftObservers` (guard, a logging reporter and the open generic
  composite) and `AddBsftObserver<TObserver, TImplementation>`, which refuses a contract registered as
  scoped or transient; the reporter logs the observer type and the exception type, and the exception
  itself only with `ObserverFailureLoggingOptions.IncludeExceptionDetails`. See docs/core.md,
  "Observers", and docs/hosting.md, "Observers".
- **Concurrency in `Bsft.Core`.** `Bsft.Core.Concurrency` adds `KeyedGate<TKey>` (`IKeyedGate<TKey>`),
  one first-in-first-out gate per key with a key comparer and no entry left behind once the last user
  leaves, and `SlotPool` (`ISlotPool`), a fixed number of slots admitted in the order callers asked,
  with documented cancellation and double-dispose semantics. See docs/core.md, "Keyed gate and slot
  pool".
- **`LogReadRequest.CorrelationId` in `Bsft.Logging`.** `LogFileReader` reads and tails only the entries
  of one correlation id (exact match on the innermost `CorrelationId` scope the file logger wrote),
  combinable with the level, search and paging filters. `LogFileEntry.CorrelationId` is null for an
  entry logged outside any scope instead of the writer's `-` marker. A tail with a filter scans only what
  was appended since its last poll. See docs/logging.md.

- **Persisted log scope properties in `Bsft.Logging`.** `BsftLoggingOptions.PersistedScopeKeys` (default
  `CorrelationId` only, output unchanged) makes the file logger write further scope keys, innermost value per key,
  as a `{Key=value;Key=value}` block in the text format and a `scope` object in JSON, bounded by
  `MaxScopePropertyChars`. `LogFileEntry.ScopeProperties` exposes them and `LogReadRequest.ScopeFilter` filters
  reads and tails on them (exact, AND); a property that does not fit is dropped whole and the line marked. The
  correlation scope key is the public `Bsft.Correlation.LogScopeKeys.CorrelationId` in
  `Bsft.Correlation.Abstractions`, used by the file logger, the reader and the console formatter, and the
  correlation slot of a text line is escaped so an id cannot forge a property block. See docs/logging.md.

- **`Bsft.Data.Testing.TestRelationalModelWarmup`.** `EnsureRelationalModelBuilt(this DbContext)` builds a
  context's runtime relational model through the same guard `Bsft.Data`'s warm-up hosted service runs, for a test
  that opens a relational context without a host. See docs/data.md.

### Changed

- **The `net10.0` groups depend on EF Core 10.** `Bsft.Data`, `Bsft.Data.SqlServer`, `Bsft.Data.Sqlite`,
  `Bsft.Data.Postgres`, `Bsft.Testing` and the `*.EFCore` packages referenced EF Core 9.0.19 in their `net10.0` group;
  they now reference EF Core 10.0.x there, and `Bsft.Data.Postgres` and `Bsft.Messaging.EFCore.Postgres` reference
  `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 instead of 9.0.4. A `net10.0` host that pins Npgsql or EF Core itself
  raises the pin to 10.0.x (NU1605 otherwise). The `net8.0` group stays on EF Core 9.0.19. EF Core 10 changed `ExecuteUpdateAsync` to take its setters as a
  delegate, so an assembly compiled against EF Core 9 fails under EF Core 10 with `MissingMethodException`.

### Fixed

- **Restore: `System.Security.Cryptography.Pkcs` raised to 10.0.13 on net10.0 for `Bsft.Data.SqlServer`.** The
  `Microsoft.Data.SqlClient` 6.1.6 that `Microsoft.EntityFrameworkCore.SqlServer` brings on net10.0 depends on
  `System.Security.Cryptography.Pkcs` 9.0.11, which carries GHSA-2fmv-rv2c-59x7 and GHSA-846f-f764-76mx (denial of
  service). With `TreatWarningsAsErrors` the NU1902 and NU1903 audit warnings failed the restore of every project
  that references the package. `Bsft.Data.SqlServer` now references the patched version directly on net10.0, so
  consumers resolve it too. The net8.0 graph is unchanged.

- **Hosting: a disposed host is no longer kept alive by the rate limiter.** ASP.NET Core's rate limiting middleware
  creates an endpoint limiter that runs a timer and never disposes it, and nobody disposes
  `RateLimiterOptions.GlobalLimiter` either (dotnet/aspnetcore#66434); the timers kept every stopped host reachable
  for the rest of the process. `UseBsftRateLimiting`, and `UseBsftDefaults` through it, now hands both limiters to
  the container, which disposes them with the host. `UseBsftRateLimiting` now requires `AddBsftRateLimiting` (or
  `AddBsftDefaults`). A global limiter belongs to one host. See docs/hosting.md.

- **Data: concurrent first queries of a new EF Core model no longer fail at startup.** EF Core builds a model's
  relational model lazily with a check-then-act gap, so two contexts of the same type running their first command
  at the same moment could throw "The model must be finalized and its runtime dependencies must be initialized
  before 'GetRelationalModel' can be used" (EF Core 9 and 10). `Bsft.Data` now registers a hosted service that
  builds the relational model of each relational context type as its first instance initializes, one at a time,
  from before the first hosted service starts. `AddBsftDataQueryFiltering`, `AddBsftDataUnitOfWork<TContext>` and
  `AddBsftDataMultiTenancy` register it; `AddBsftDataModelWarmup()` is the entry point for a host using none of
  them. `BsftDataOptions.ShouldWarmRelationalModels = false` turns it off. `Bsft.Data` now depends on
  `Microsoft.Extensions.Hosting.Abstractions`. See docs/data.md, "The relational model warm-up".

- **Hosting: exception-mapped ProblemDetails bodies now go through `IProblemDetailsService`.**
  `BsftExceptionMiddleware` used to serialize a mapped exception's ProblemDetails itself, so a host's
  `AddProblemDetails(o => o.CustomizeProblemDetails = ...)` and a custom `IProblemDetailsWriter` never saw an
  exception-mapped 4xx/5xx body. The middleware now writes through `IProblemDetailsService.TryWriteAsync` when
  the service is registered, falling back to the direct write it has always done when it is not registered or
  every writer declines. The default body is unchanged when nothing customises it.

## [2.1.0]

The first release of Bsft Foundation on nuget.org and in a repository of its own. The packages
continue the version line of the framework they were cut from, so their history starts at 2.0.0,
which was not published here; see [docs/migration-2.1.0.md](docs/migration-2.1.0.md).

### Added

- **Foundation packages now also target net10.0.** Every package ships `net8.0` and `net10.0`
  assets. On `net10.0` the packages depend on the 10.0.x line of Microsoft.Extensions.* and
  ASP.NET Core; the `net8.0` dependencies are unchanged. The tests run, and the coverage gate holds
  each framework at 100 %, on both.

### Changed

- **The authentication test doubles left `Bsft.Testing` (breaking).** `Bsft.Testing` no longer
  references an authentication package: `TestUserContext`, `TestPermissionSource`,
  `TestPrincipalProvider`, `TestAuthenticationHandler`, `AddBsftTestUser`, `AddBsftTestPermissions`
  and `AddBsftTestAuthentication` are no longer part of it, and `AddBsftTestContexts` and
  `BsftWebApplicationFactory` install the test tenant and correlation only. A test that relied on
  the test user or the `BsftTest` authentication scheme registers its own.

## 2.0.0

The last version before this repository, not published on nuget.org. It is listed because 2.1.0
builds on it: a major release of the packages, see [docs/migration-2.0.0.md](docs/migration-2.0.0.md).

### Changed

- **Names follow [docs/naming.md](docs/naming.md) (breaking).** Two-letter acronyms in capitals
  (`Bsft.*.EFCore`, `Bsft.Messaging.RabbitMQ`), no abbreviations (`Org` → `Organization`, `Config`,
  `Dedup`, `Seq`, `Ref`, `Dest`, `Spec` written out), American spelling (`Canceled`, `Behavior`),
  one suffix per role (`*Validator`, `*Sweeper`, `*Reader`, `*Event`, `*QueryParameters`),
  `Get`/`Find`/`List` by what a method returns, and canonical parameter names; behavior is
  unchanged. Wire and persisted names follow their C# names with no shim: configuration keys
  (`…:EFCore`, `Bsft:RabbitMQ`), the sort query key (`Sort[i].Direction`), the mediator behavior
  name `OrganizationAuthorization`, the metric `bsft.saga.timeout.canceled` and the log property
  `PurgedCount`. The migration guide lists each with its step.
- **`RequiredPermissionsMetadata` consolidated (breaking).** The per-package copies of
  `RequiredPermissionsMetadata` are one public record in `Bsft.Http`.
- **Naming checks.** `verify-structure.mjs` checks the `name-*` rules (acronyms, abbreviations,
  spelling, type suffixes, options validators, hosted services, duplicate type names, namespace-type
  clashes, parameter names, test class names, and the written-out contracts: endpoint names, routes,
  table names, bound keys, log properties and contract constants), each with a firing and a
  non-firing fixture.
- **Namespaces follow features, not roles (breaking,
  [ADR-0013](docs/design/adr-0013-namespaces-by-feature.md)).** Every package drops its role
  namespaces (`Services`, `Models`, `Commands`, `Queries`, `Endpoints`, `Stores`, `Exceptions`,
  `Options`, `Extensions` and the folder namespaces called `Abstractions`); types live in the
  namespace of their feature or in the package root, and each namespace equals its folder, which the
  build enforces with IDE0130.
- **`AddBsft*`, `MapBsft*` and `UseBsft*` live in `<Package>.DependencyInjection` (breaking).** A host
  imports one `using <Package>.DependencyInjection;` per package it wires.
- **Structure check.** `.github/verify-structure.mjs` checks the package layout of ADR-0013 and
  [docs/package-conventions.md](docs/package-conventions.md) repo-wide: namespace equals folder, no
  role folders, nesting, one-file namespaces, small packages flat, the package root as a dependency
  sink, the direction of `DependencyInjection`, no namespace cycles, `.Abstractions` folders with a
  counterpart, test projects mirroring their package, `docs/packages.md` rows, solution membership
  and one type per file. Each rule has a fixture that breaks it.
- **Coverage measures async methods, iterators and lambdas.** Coverlet no longer excludes
  `CompilerGeneratedAttribute`, which had dropped every state machine and closure from the
  measurement; `tests/check-state-machine-coverage.sh` fails when a package with async code reports
  none. Still at 100 % line coverage with no new exclusion.
- **Conventions rules.** `bsft-single-service-collection-extensions` is replaced by
  `bsft-di-entry-point-namespace`, which requires the DI entry point classes to live in a namespace
  ending in `.DependencyInjection`; a package may now have several of them, one per feature it wires.
- **All `.Abstractions` packages declare the namespace of the package they abstract (breaking).**
  `Bsft.Messaging.Abstractions` declares `namespace Bsft.Messaging` instead of
  `Bsft.Messaging.Abstractions`, and likewise `Bsft.Correlation.Abstractions`,
  `Bsft.Mediator.Abstractions` and `Bsft.Outbox.Abstractions`; package and assembly names are
  unchanged, `.Contracts` packages are unchanged. The conventions rule
  `bsft-namespace-follows-assembly` enforces the layout.
- **Saga: `SagaStepConfigurator` takes its builder as a type argument (breaking).**
  `SagaStepConfigurator<TState, TEvent>` is now `SagaStepConfigurator<TState, TEvent, TBuilder>`, and
  the definition tracking of `ISagaBuilderFactory` moved to `ISagaDefinitionRegistry`, which the
  factory extends. The builder and the configurator no longer depend on each other. Fluent flow
  definitions compile unchanged; only code naming the configurator type spells the third argument.

[Unreleased]: https://github.com/bochmann-software/bsft-foundation/compare/v2.2.0...HEAD
[2.2.0]: https://github.com/bochmann-software/bsft-foundation/compare/v2.1.0...v2.2.0
[2.1.0]: https://github.com/bochmann-software/bsft-foundation/releases/tag/v2.1.0
