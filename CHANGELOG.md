# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project uses a single `BsftFoundationVersion` across all packages.

## [Unreleased]

## [2.3.0]

A minor release, in lockstep with the commercial framework 2.3.0. The previews of this release were published as
2.2.1-preview.1 to 2.2.1-preview.29; 2.2.1 itself is not released. The sections 2.2.1-preview.1 to 2.2.1-preview.11
below describe the Foundation changes of those previews; this section lists the Foundation changes since
2.2.1-preview.11 and summarises the breaks of the whole round. See `docs/migration-2.3.0.md`, "Upgrading from
Foundation 2.2.0".

### Breaking

- The breaks of 2.2.1-preview.1 to 2.2.1-preview.11 (below): `CanonicalHash` moved to `Bsft.Core` and
  `RetryableAttribute` to `Bsft.Mediator.Abstractions` (CS0104 for a file that imports both namespaces); a nested
  `[Retryable]` request is no longer retried on its own; `ITenantScopeFactory.CreateScopeAsync` is a new default
  member, and background tenant passes open and dispose their scopes asynchronously; a late cancellation of a unit of
  work commits; `FilterOperator.In` is a new enum value; `TimeOrderedIdGenerator.Shared` and `ForNode(int)` are
  removed; a `TimeProvider` is a required constructor parameter of `PollLoop`, `LoggingBehavior<,>`, `OutboxDispatcher`,
  `IntegrationEventConsumerHost`, `EFCoreSagaStore`, `SearchExecutor<,>`, `BsftConsoleFormatter` and the file logger
  types; `BsftResilienceStreamOptions.TimeProvider` has no default; `TestClock` drives timers.
- **`Bsft.Hosting`, new `Bsft.Hosting.OpenApi`:** Swagger, API versioning and `AddBsftDefaults` / `UseBsftDefaults`
  move to the new package `Bsft.Hosting.OpenApi`, so `Bsft.Hosting` carries no Swashbuckle or Asp.Versioning
  dependency. A host calling them adds the package reference and `using Bsft.Hosting.OpenApi.DependencyInjection;`;
  nothing is forwarded, so compiled callers recompile.
- **`Bsft.Hosting`:** the built-in CORS origin evaluator and `UseBsftCors` use the options of the last `AddBsftCors`
  call.
- **`Bsft.Resilience`:** the standard HTTP pipeline retries POST and PATCH only when the request carries an
  `Idempotency-Key` header (`ResilienceDefaults.IdempotencyKeyHeaderName`); the other methods are retried as before.
- **`Bsft.Retention`:** a generic pass is named the C# way (`Namespace.Pass<Namespace.Argument>`), which keys its
  options section; `RetentionSweeper<TPass>` starts its first sweep only after `StartedAsync` and drains the batch in
  flight on stop.
- **`Bsft.Idempotency`:** new enum value `IdempotencyAuditAction.ResponseWithheld` (3); `AddBsftIdempotency` calls
  `AddDataProtection()`.
- **`Bsft.Scheduling`:** a custom `IJobStore` persists `JobRun.Result` in `SettleAsync` to keep typed run results.
- **Hosted loops:** the dispatchers, sweepers and workers of the Foundation packages are registered through
  `AddBsftGatedHostedService`, so their hosted descriptor is a factory descriptor (`ImplementationType` is null) and a
  later `AddHostedService<Loop>()` no longer dedupes against it; use `HasBsftGatedHostedService<T>()` and
  `RemoveBsftGatedHostedService<T>()`.
- **`Bsft.Messaging.EFCore.Postgres`:** depends on the new `Bsft.Outbox.Postgres` (restored transitively); a
  whitespace-only listener connection string fails with the "no connection string" error.

### Added

- **New packages:** `Bsft.Hosting.OpenApi` (see above), `Bsft.Hosting.StartGates` (start gates for hosted loops,
  `AddBsftHostedWorkStartGate<TGate>()` and `AddBsftGatedHostedService<TService>()`, over `IHostedWorkStartGate` in
  `Bsft.Core.Hosting`; with no gate nothing changes) and `Bsft.Outbox.Postgres` (`IOutboxWakeSignal` on PostgreSQL
  `LISTEN`, `AddBsftOutboxPostgresWake`).
- **`Bsft.Outbox`:** `AddBsftOutboxDrainWorker<TPayload>(name, ...)`, a hosted loop that drains the backlog with a
  shutdown grace (`BsftOutboxDrainWorkerOptions.ShutdownGrace`, default 5 s), start gates and a wake signal;
  `IOutboxDrainPass<TPayload>` and `AddBsftOutboxDrainPass<TPayload, TPass>()` around each drain round;
  `IOutboxDrainer.DrainOnceAsync(mayStartRow, cancellationToken)` as a default member.
- **`Bsft.Scheduling`, `Bsft.Scheduling.EFCore`:** typed run results. A job records a result about its run
  through `JobExecutionContext.ResultRecorder` (`IJobRunResultRecorder.Record<TResult>(schema, result)`); it is
  kept as a `JobRunResult` (schema name plus JSON) on `JobRun.Result`, written when the run settles, and
  returned by `IJobRunReader` as `JobRunSummary.Result`. `BsftSchedulingOptions.MaxResultBytes` (default 4096,
  at most 64 KiB) bounds it; a larger one is refused with `PayloadTooLargeException`
  (`SchedulingErrorCodes.ResultTooLarge`). Jobs that record nothing behave as before. Needs a migration: two
  nullable columns `ResultSchema` and `Result` on `JobRuns`. Also `AddBsftSchedulingProducer()`, a producer-only
  host role without the dispatcher, the seeders or any hosted service, and opt-in retention for settled JobRuns
  (`AddBsftJobRunEFCoreRetention()`, `BsftSchedulingOptions.JobRunRetention` and `JobRunRetentionKeepLatest`).
- **`Bsft.Data.Contracts`, `Bsft.Data`:** keyset paging (`CursorPagedRequest`, `CursorPagedResult<T>`,
  `AddBsftDataKeysetPaging`, `IKeysetPageReader`, `IKeysetCursorSerializer`).
- **`Bsft.Core`:** `InternetDomain`, `EmailAddress`, `EmailAddressCollisions` and `UnicodeNormalization`, one canonical
  rule for e-mail addresses and domains (IDN domains as A-labels); `ISubjectDataEraser` and `IAccountDeletionSource`;
  `IHostedWorkStartGate`, `IHostedWorkStartGateOverdueSignal`, `WaitAllAsync` and `HasOpenedAsync`.
- **`Bsft.Hosting`:** `AddBsftExceptionMapper<TMapper>()`, `AddBsftExceptionObserver<TObserver>()` and
  `AddBsftExceptionHandler()` (an `IExceptionHandler` for `UseExceptionHandler()`), byte-identical bodies without
  them; `AddBsftSecurityHeaders` / `UseBsftSecurityHeaders` (opt-in); `BsftRateLimitingOptions.GlobalLimiter`,
  `Window`, `EnableDefaultPolicy` and `CustomPartitionKeyPrefix`; `BsftCorsOptions.ExposedHeaders`, `OriginPredicate`
  and `PolicyName` with `IBsftCorsOriginEvaluator`; `BsftHealthCheckConfiguration.AddHostedWorkStartGates()`.
- **`Bsft.Logging`:** `BsftLoggingOptions.MaxRetainedFiles` and `MaxTotalSizeMb`; `AddBsftFileLogger(IConfiguration)`.
- **`Bsft.Retention`:** `BsftRetentionOptions.UseAbsoluteExpiry`.
- **`Bsft.Idempotency`:** sealed replays of secret-bearing responses (`[SensitiveIdempotentResponse]`,
  `.WithSensitiveResponse()`), answered `409` `IDEMPOTENCY_RESPONSE_WITHHELD` when a replay cannot be unsealed.
- **`Bsft.Http`:** `SameOriginRequest.IsSameOrigin`.
- **`Bsft.Hosting.BackgroundWork`:** `PollingBackgroundServiceBase`, a polling `BackgroundService` over `PollLoop`. A derived
  service writes `TryTickAsync` (`true` runs the next pass at once, `false` waits `PollInterval`); `Enabled` (false ends the
  service at once, logged once) and `OnStartingAsync` are virtual. A failed pass is logged and retried after the interval, a
  host stop ends the service cleanly, and a poll interval that is zero, negative or infinite faults the service at start. The
  `TimeProvider` is optional, and it composes with `AddBsftGatedHostedService`. Opt-in.

### Fixed

- **`Bsft.Resilience`:** a POST whose response was lost no longer runs twice.
- **`Bsft.Retention`:** a delete cancelled at stop is no longer logged as an error, and no sweep runs while hosted
  services still initialise.
- **`Bsft.Logging`:** `RetainDays` is also applied after a size roll; delete failures are reported, never thrown.
- **`Bsft.Messaging.EFCore.Postgres`:** a notification that arrives while the listener subscribes or runs its catch-up
  drain is answered at once.
- **`Bsft.Correlation`:** `CorrelationIdMiddleware` uses no regex timeout, so no `RegexMatchTimeoutException`.
- **`Bsft.Instrumentation`:** no NU1510 warnings on `net10.0`.

### Security

- The ASP.NET Core package group is raised to 8.0.34 and 10.0.15 (GHSA-wr8v-9x5q-ggcm).

## [2.2.1-preview.11]

A preview of 2.2.1, following 2.2.1-preview.10. The Foundation packages not listed below are unchanged and published
at this version because both version lines move in lockstep. See `docs/migration-2.2.1-preview.11.md`.

### Added

- **`Bsft.Auditing`:** `AuditActivityInput.OccurredAt`, an optional instant that `AuditRecorder` stamps on the event;
  without it the event carries the instant of recording from the injected clock, as before.

### Changed

- **`Bsft.Auth.Credentials`:** the hashing queue wait (`Hashing.MaxQueueWaitTime`) ends on time under a burst, made in
  the kernel under the real clock. `LegacyCredentialOptions.AllowImportedFormat` takes an optional clock overload and
  holds a window back until the registration supplies the host's clock.
- **`Bsft.Security`:** `LegacySecretOptions.AllowPlaintextReads` takes a clock overload and holds a window back until the
  registration supplies the host's clock; a window longer than 180 days by that clock fails at startup. The snapshot
  `AddBsftSecurity` registers honours a `Configure<BsftSecurityOptions>` that ran before the call.
- **`Bsft.Data`:** `SearchExecutor<TEntity, TDto>` takes a `TimeProvider` as its third constructor parameter and
  measures the search duration on it. Breaking for a consumer that constructs it by hand; the `AddXxx` registration
  resolves the clock from the container.
- **`Bsft.Hosting`:** `BsftConsoleFormatter` requires a `TimeProvider` on both constructors. Breaking for a consumer
  that constructs it by hand.
- **`Bsft.Logging`:** `BsftFileLoggerProvider`, `BsftFileLogger` and `LogFileReader` require a `TimeProvider` on every
  constructor. Breaking for a consumer that constructs them by hand. The provider's final drain no longer re-enters its
  own write gate, and its shutdown budget and wait run on the injected clock and on a signal instead of a thread join
  and a timed monitor.
- **`Bsft.Resilience`:** `BsftResilienceStreamOptions.TimeProvider` has no default any more and `Validate` rejects an
  options instance without one, so a `WithRetry` caller sets it. Breaking for a caller that relied on the system
  clock.
- **`Bsft.Testing`:** `TestClock` drives timers and timestamps. `Advance` and a forward `SetUtcNow` fire the timers
  that fall due, in due order, a zero due time fires at once, `GetTimestamp` follows the clock, time is additive under
  concurrent and nested calls, and a throwing callback does not stop the move. New members: `PendingTimerCount`,
  `WaitForTimerAsync` and `WaitForAnyTimerAsync`. Breaking in behaviour for a test that relied on the timers staying on
  real time.

## [2.2.1-preview.10]

A preview of 2.2.1, following 2.2.1-preview.10.

### Changed

- **`Bsft.Core`, `Bsft.Data`, `Bsft.Mediator`, `Bsft.Messaging`, `Bsft.Messaging.EFCore`,
  `Bsft.Messaging.EFCore.Postgres`, `Bsft.Messaging.RabbitMQ`, `Bsft.EventAggregator.Saga`,
  `Bsft.EventAggregator.Saga.EFCore`, `Bsft.Idempotency.EFCore`, `Bsft.Retention`, `Bsft.Scheduling`: waits, timeouts
  and measured durations run on the injected `TimeProvider`.** `PollLoop`, `LoggingBehavior`, `EFCoreSagaStore`,
  `IntegrationEventConsumerHost` and `OutboxDispatcher` require a `TimeProvider` in their constructors, which the
  `AddXxx` registrations resolve from the container (`TryAddSingleton(TimeProvider.System)`).
  `TimeOrderedIdGenerator.ForNode(int)` and `Shared` are replaced by `ForNode(int, TimeProvider)` and
  `ForRandomNode(TimeProvider)`, which gives each clock a node id of its own for up to 4096 clocks per process. See
  `docs/migration-2.2.1-preview.10.md`.

## [2.2.1-preview.9]

A preview of 2.2.1, following 2.2.1-preview.8. No change in the Foundation packages; they are published at this
version because both version lines move in lockstep.

## [2.2.1-preview.8]

A preview of 2.2.1, following 2.2.1-preview.7. No change in the Foundation packages; they are published at this
version because both version lines move in lockstep.

## [2.2.1-preview.7]

A preview of 2.2.1, following 2.2.1-preview.6. Fixes only: background passes bind tenant scopes asynchronously and
dispose their scopes asynchronously.

### Added

- **`InboxSweeper`** in `Bsft.Messaging.EFCore`: a constructor `(..., IBackgroundTenantSource tenants,
  ITenantScopeFactory tenantScopes)`, which the container picks when both are registered.
- **`EFCoreOutboxStore`** in `Bsft.Outbox.EFCore`: a constructor `(scopeFactory, rows, ITenantScopeFactory
  tenantScopes)`. `Bsft.Outbox.EFCore` now references `Bsft.Core`.

### Fixed

- **`InboxSweeper`** (`Bsft.Messaging.EFCore`) and **`IdempotencyRecordSweeper`** (`Bsft.Idempotency.EFCore`) open a
  tenant's sweep scope through `ITenantScopeFactory.CreateScopeAsync` when a factory is registered, instead of a plain
  `IServiceScopeFactory.CreateScope`, and dispose it asynchronously.
- **`EFCoreOutboxStore`** (`Bsft.Outbox.EFCore`) and **`EFCoreJobStore`** (`Bsft.Scheduling.EFCore`) open each
  operation's scope through `CreateScopeAsync` while a background pass works for a tenant and a factory is
  registered, and dispose it asynchronously.
- **`JobExecutor`** (`Bsft.Scheduling`) disposes a job's scope asynchronously, the tenant's and the plain one, so a
  scoped service that is only `IAsyncDisposable` no longer faults the cleanup.

### Changed

- **Hosts with `AddBsftDataMultiTenancy` take the new path on their own.** Its `TenantScopeFactory` (`TryAdd`) is
  picked up by these sweepers and stores in every host with a tenant source. It enters the tenant through
  `EnterTenant`, which throws `InvalidOperationException` when `ITenantContext` is not Bsft.Data's `TenantContext`. A
  host with a context of its own registers an `ITenantScopeFactory` of its own before `AddBsftDataMultiTenancy`, or
  goes back to Bsft.Data's `TenantContext`; otherwise every pass fails from preview.7 on.
- **Substituted `ITenantScope`s are disposed with `DisposeAsync`** instead of `Dispose`.

## [2.2.1-preview.6]

A preview of 2.2.1, following 2.2.1-preview.5. Additive: an asynchronous way to open a tenant's scope.

### Added

- **`ITenantScopeFactory.CreateScopeAsync(tenantId, cancellationToken)`** in `Bsft.Core` (`Bsft.Core.Tenancy`), for
  a host that binds a tenant's scope with I/O. A default interface member checks the token and calls `CreateScope`,
  so existing implementations keep working; a test double from a mocking library has to set it up, because mocks do
  not run default members.

### Changed

- **`JobExecutor`** in `Bsft.Scheduling` opens a job's tenant scope through `CreateScopeAsync`, with the run's token.

## [2.2.1-preview.5]

A preview of 2.2.1, following 2.2.1-preview.4. Additive: a placement for a default-slot replacement in the mediator.

### Added

- **`DefaultBehaviorPlacement`** in `Bsft.Mediator` and the overload
  `MediatorConfiguration.ReplaceDefaultBehavior(name, behaviorType, placement)`: with `AfterDefaults` the replacement
  runs where `AddBehavior` registers, after the default chain, and stays the slot's behavior for the startup checks.
  `InSlot`, and the two-argument call, keep the slot's position.

## [2.2.1-preview.4]

A preview of 2.2.1, following 2.2.1-preview.3. Additive: an optional observer of the shared transaction's own
connection steps.

### Added

- **`ISharedTransactionStepObserver`** in `Bsft.Data` (`Bsft.Data.Transactions`), with `SharedTransactionStepInfo` and
  `SharedTransactionStepKind`: a scoped observer is told when the outermost unit of the shared transaction starts and
  completes opening the connection, beginning, probing, committing, rolling back or releasing the transaction and
  closing the connection, the steps it runs outside EF Core. Without an observer nothing changes.

## [2.2.1-preview.3]

A preview of 2.2.1, following 2.2.1-preview.2. Additive: dynamic fields in a query configuration and the filter
operator `in`, with one fixed meaning of a missing value.

### Added

- **`FilterOperator.In`** in `Bsft.Data.Contracts` (`"in"` on the wire, declared last): set membership over a JSON
  array, compiled by `Bsft.Data` as `values.Contains(member)` (one parameterized collection), or as an `or` over `eq`
  for a case-folded string. Only a field that names `In` in `AllowFilter` accepts it, a dynamic field only when its
  resolver names it (`Bsft.Data.Filtering.DynamicFieldPredicateExtensions.ToInPredicate`). An empty array is a 400 with `FILTER_IN_EMPTY`,
  a `null` element a 400 with `FILTER_IN_NULL_ELEMENT`, a set over 100 values or sets over 500 in one filter a 400
  with `FILTER_IN_TOO_MANY` (`Bsft.Data.Filtering.FilterInValues`; `QueryConfigurationBuilder.MaxInValues` and
  `MaxInValuesPerFilter`).
- **Dynamic fields** in `Bsft.Data`: `QueryConfigurationBuilder.DynamicFields(prefix, IDynamicFieldResolver<TEntity>)`
  and `DynamicField<TEntity>`. Filter and sort fields under the prefix are resolved per request; the compiler builds
  `eq null`, `neq null`, the negations and `in` from the resolver's presence test and positive predicates.
- **Missing values**: `eq null` is "not set", `neq null` equals `pr`, `neq v` matches a field that is not set, and
  no positive operator matches one, in memory and in SQL alike.
- **`ValidationException`** in `Bsft.Core`: a constructor taking an own `errorCode`.

## [2.2.1-preview.2]

A preview of 2.2.1, following 2.2.1-preview.1. Additive and opt-in: tenant keys for the Foundation tables; without
`UseTenantKeys` every schema and query is unchanged. Additive for every host: fixes in the shared transaction and the
mediator retry, plus a warning per retry.

### Added

- **Tenant keys.** `Bsft.Core.Persistence.ITimeOrderedIdGenerator` and `TimeOrderedIdGenerator`: 64-bit ids of 41
  bits of milliseconds since 2025-01-01, 12 bits of node id and 10 bits of sequence, strictly rising per process, also
  under parallel calls and when the clock steps back. `Bsft.Data`'s `modelBuilder.UseTenantKeys()`, called before the
  entity configurations, maps `IdempotencyRecords`, `OutboxMessages` and `InboxMessages` with a required `TenantId`
  and the key `(TenantId, Id)` instead of an identity column; the `Id` comes from the generator and the `TenantId`
  from the scope (`ITenantContext`, then `BackgroundTenant.Current`). `AddBsftDataTenantKeys`, required with it, sets the node
  id (`Bsft:Data:TenantKeys:NodeId`, 0 to 4095); a multi-tenancy host that maps tenant keys without it fails at start. The idempotency and inbox stores work on the scope's tenant only, and
  the outbox store and the sweepers restrict a tenant pass to its tenant. `TenantKeyStartupValidator` refuses to
  start a host that registers a tenant-keyed context with `AddDbContextPool`, `AddDbContextFactory` or singleton
  options, and warns when no node id is configured.
- **`IOutboxRowScope<TEntity>`** in `Bsft.Outbox.EFCore`: an optional interface a row source implements to narrow
  every statement of `EFCoreOutboxStore` and hold a claim to the claimed row's owner.
- **Resilience: a warning per retry.** `Bsft.Resilience.Mediator` logs each retry of a request as a structured warning
  (event `1`/`MediatorRequestRetrying`: request type, retry number, the SQLSTATE of the conflict that caused the retry,
  else of the failure's chain, and the exception type), without the exception or its message. Nothing is logged
  without a retry or without registered logging.

### Fixed

- **Data: a caught failure of a nested unit is the cause of the refused commit.** When a nested unit of the shared
  transaction fails and an outer handler catches the exception, the outermost unit's `AmbientTransactionMisuseException`
  now carries the nested unit's first failure as `InnerException`. A `40001` or `40P01` that reached no EF Core
  interceptor (a raw command on the shared connection, an error while rows were read) is therefore classified as a
  conflict, and `[Retryable]` re-runs the whole unit once in a fresh scope instead of failing. The nested failure counts
  only through that chain and ends with its unit; a handler that catches it and throws its own exception is not
  retried. The conflict classification asks every classifier about each link of the inner-exception chain, a refused
  overlapping unit is never a conflict, and `IAmbientTransaction.FindTransactionConflict` (default-implemented)
  returns the failure classified as the conflict.
- **Data: cancelling after the work completed no longer loses it.** Once the outermost work has returned, the final
  saves of the enlisted contexts and the abort probe run with `CancellationToken.None`, like the commit already did,
  so a caller that cancels after the work completed gets a committed unit instead of an `OperationCanceledException`
  and a rollback. A cancellation observed during the work still rolls everything back. The final statements are then
  bounded by the command timeout only.

## [2.2.1-preview.1]

A preview of 2.2.1, following 2.2.0. Additive and opt-in: the shared transaction across several contexts (with its
abort probe and per-scope ownership), the all-commands transaction and retry policies with their startup validators,
rescoping pipeline behaviors with request-scope seeds and request nesting, and a named tenant query filter on .NET 10.
One bug fix applies to every host: a `[Retryable]` request nested in a request that is already being retried is no
longer retried on its own, which multiplied the attempts. Everything else is unchanged for a host that opts into none
of the additions.

### Added

- **One transaction across several EF Core contexts.** `Bsft.Core.Persistence.IAmbientTransaction`, an
  `ITransactionalUnitOfWork` that a unit started inside it joins. `Bsft.Data` implements it with
  `AddBsftDataSharedTransaction` and `UseBsftSharedTransaction`: one connection per scope shared by every enlisted
  context, one transaction opened and committed once by the outermost unit, lazy enlistment of contexts resolved
  later, rollback and change-tracker clearing of every context on failure, and `ITransactionConflictClassifier` for
  the provider's conflict classification. `Bsft.Data.Postgres` adds `AddBsftDataPostgresTransaction`,
  `UseBsftSharedNpgsql` and the classification of `40001` and `40P01`. `Bsft.Mediator`'s `TransactionBehavior`
  runs `[Transactional]` requests in it (its multi-context startup check passes when one is registered), and
  `Bsft.Resilience.Mediator`'s `RetryBehavior` retries only the outermost unit, also on a classified conflict.
  Without the registration every package behaves as in 2.2.0.
- **Data: a swallowed command failure dooms the shared transaction.** A database command of an enlisted context that
  fails or is cancelled while the transaction is open dooms the unit: if the handler swallows the exception, the
  outermost unit throws instead of reporting a commit PostgreSQL answered with `ROLLBACK`, with the failure as inner
  exception, and a swallowed `40001`/`40P01` still triggers the retry. The commit runs with
  `CancellationToken.None`.
- **Mediator: transactions and retries for every command by policy.** `MediatorConfiguration.ApplyTransactionToAllCommands()`
  makes every command transactional without `[Transactional]`, and `BsftResilienceMediatorOptions.ShouldRetryAllCommands`
  retries every command without `[Retryable]`; queries still need the markers. Retry stays outermost-only, and both
  behaviors keep their slots in the default chain. See `docs/mediator.md`, "Every command without the attribute".
- **Data: a named tenant query filter (.NET 10).** `UseNamedTenantFilter(this)` and `UseNamedStaticTenantFilter`
  apply the tenant filter as the named filter `bsft.tenant` (`BsftDbContextOptionsExtensions.TenantFilterName`, or a
  name of your own), so a model can add further named filters without EF Core 10's
  `AnonymousAndNamedFiltersCombined` and a query can ignore the tenant filter alone.
- **Data: the store is asked before the commit.** `ITransactionAbortProbe` (`Bsft.Data.Transactions`) lets a
  provider report that the store already aborted the transaction; the outermost unit asks every registered probe
  before it commits and throws instead. `Bsft.Data.Postgres` registers one (`SELECT 1`, reporting `25P02`, about
  0.3 ms per outermost unit), which catches failures no interceptor sees, such as an error raised while a query's
  rows are read.
- **Data: a save EF Core rolled back to its own savepoint leaves the unit committable.** A failed `SaveChanges`
  inside the shared transaction, which EF Core protects with an automatic savepoint, does not doom the unit, so a
  handler that catches a unique violation can go on and commit the rest.
- **Data: contexts take the shared transaction as they initialize.** A context first resolved inside an open unit,
  by a nested command, finds the shared transaction in `Database.CurrentTransaction` before its first command, so a
  store that joins a caller's transaction (hierarchy, organization, billing and others) joins it instead of beginning
  a second one. A context disposed inside the unit is forgotten, after a unit the contexts give back their hold on
  the connection, and a cleanup that fails after the commit is logged instead of failing a unit that persisted.
- **Data: pooled contexts and context factories are refused.** `UseBsftSharedTransaction` throws when the options
  are built from the root provider, which `AddDbContextPool` and `AddDbContextFactory` do.
- **Data: one unit at a time per scope.** A unit started from a parallel flow of a scope whose transaction another
  flow holds open throws instead of joining it; `IAmbientTransaction.IsActive` is true only in the flow that opened
  the unit.
- **Core: a misuse of the ambient transaction has a type of its own.** `AmbientTransactionMisuseException`
  (`Bsft.Core.Persistence`, derived from `InvalidOperationException`) is what the shared transaction throws for a
  swallowed failure inside the unit, for a second outermost unit beside a running one and for an enlisted context
  with `EnableRetryOnFailure`. `RetryBehavior` never retries it, whatever `ShouldRetry` says, unless the swallowed
  failure it carries is a transaction conflict (a caught `40P01`), and the default `IsTransientFailure` refuses it.
- **Mediator: rescoping behaviors.** `IRescopingPipelineBehavior<TRequest, TResponse>` and
  `IPipelineContinuation<TResponse>` let a behavior run the rest of the pipeline in the request's scope or in a fresh
  one, which every registered `IRequestScopeSeed` seeds first. `IRequestNesting` (scoped, registered by
  `AddBsftMediator`) records whether an outer request of the scope is retried or holds a transaction open, and
  `IMediatorTransactionPolicy` exposes `ShouldApplyTransactionToAllCommands`. `IRequestMetadata` gains `IsCommand`.
- **Resilience: retries of the shared transaction run in a fresh scope.** `RetryBehavior` is a rescoping behavior:
  with an `IAmbientTransaction` registered, every attempt after the first resolves the handler, connection, contexts
  and transaction from a new scope seeded by the host's `IRequestScopeSeed`s.
- **Resilience: no retry of a request nested in a joined transaction, in a host that opts in.** With an
  `IAmbientTransaction` registered or under `ApplyTransactionToAllCommands()`, a `[Retryable]` request dispatched
  while an outer request of the same scope holds a mediator transaction open passes through untried, because the
  store has typically aborted the joined transaction with the failure. A host that opts into neither retries it as
  in 2.2.0.
- **Startup validators for the all-commands policies.** The host fails to start when
  `ApplyTransactionToAllCommands()` is on without `TransactionBehavior<,>` in the chain, or when
  `ShouldRetryAllCommands` is on without `ApplyTransactionToAllCommands()` or without a registered
  `IAmbientTransaction`: a retry in the request's scope writes again what the failed attempt's contexts still
  track as added.
- **Core: `CanonicalHash` moved from `Bsft.Http` to `Bsft.Core`.** `ToCanonicalHash` (namespace `Bsft.Core`) is
  available without an ASP.NET Core reference. `Bsft.Http` references `Bsft.Core` and keeps a hidden
  `Bsft.Http.CanonicalHash.ToCanonicalHash(IEnumerable<string?>)` with the old signature for binaries compiled
  against it; source that used the extension adds `using Bsft.Core;`.
- **Mediator: `[Retryable]` in `Bsft.Mediator.Abstractions`.** `Bsft.Mediator.RetryableAttribute` lets a contracts
  project mark requests without referencing `Bsft.Resilience.Mediator`; the former
  `Bsft.Resilience.Mediator.RetryableAttribute` stays (hidden) and `RetryBehavior` honours both.
- **Mediator: `ReplaceDefaultBehavior(name, behaviorType)`.** Puts a host's own open-generic behavior into one slot
  of the canonical chain at that slot's position; `AuthorizationCoverageValidator` counts a replaced
  `MediatorDefaults.Authorization` as authorization and no longer warns. Replacing or suppressing a slot twice
  throws `InvalidOperationException`.
- **Modules: `ModulesConfiguration.Add(Type)` and `ModuleCatalog.Add(Type)`** for module types known only at run
  time, checked at the call.
- **Hosting: `PagedQueryParameters`**, the minimal-API binding (`IBindableFromHttpContext`) of `PagedRequest` with
  the keys of the MVC binding, rejecting unusable values as a 400 keyed on the query key (also a repeated key and
  sort entries not numbered from 0 without gaps), and `BsftFilterTransportOptions.MaxPageSize` (default 100), which
  also caps the default page size.
- **Core: background work per tenant.** `Bsft.Core.Tenancy` with `IBackgroundTenantSource`, the ambient
  `BackgroundTenant`, `BackgroundTenantPasses`, `ITenantScopeFactory` and `ITenantScope`. With a tenant source
  registered, `OutboxDispatcher` (also its `ProcessOnceAsync`), `InboxSweeper`, `IdempotencyRecordSweeper` and
  `JobDispatcher` run each pass once per tenant, and `JobExecutor` opens a job's scope through
  `ITenantScopeFactory`. `Bsft.Data`'s `TenantContext` takes over `BackgroundTenant.Current` as it is when the context is created, and
  `AddBsftDataMultiTenancy` registers an `ITenantScopeFactory`. Without a source nothing changes.
- **Messaging: in-process outbox wake-up for silo hosts.** `BsftMessagingPostgresOptions.IsInProcessWake` makes a
  save wake the process's own dispatcher instead of `pg_notify` on one fixed connection; polling stays the
  guarantee. A start-up warning names the option when an `IBackgroundTenantSource` is registered and it is off.

### Changed

- **Modules: an abstract module type is refused with `ArgumentException`** by `Add<TModule>()` as by the new
  `Add(Type)`, where it threw `MissingMethodException` from the activator. A directory scan (`FromDirectory`, and
  the loader resolving a manifest entry) skips open generic types and types without a public parameterless
  constructor instead of failing on them.
- **Resilience: `MaxRetryAttempts` is at least `1`.** The options validation refuses `0`, which Polly refused only
  when it built the pipeline, failing every request. A request that is not to be retried is not marked
  `[Retryable]`.
- **CI: the shared-transaction PostgreSQL tests run against a PostgreSQL service** in every workflow that runs
  `Bsft.Data.Postgres.Tests`, through `BSFT_TEST_POSTGRES` like the other PostgreSQL suites; its fixture fails
  instead of skipping when `CI` is `true` and the variable is unset, and in every run when the server it names
  does not accept a connection.
- **Docs: retry count.** `MaxRetryAttempts` counts retries after the first attempt (`3` runs a request at most four
  times), and the ambient transaction's classifier, not `ShouldRetry`, is the intended source for database
  transients.

### Fixed

- **Resilience: a `[Retryable]` request nested in a request that is already being retried is no longer retried on
  its own**, in every host. In 2.2.0 it was retried inside each attempt of the outer request, which multiplied the
  attempts (four outer attempts of four inner ones each with the default budget). Its failure now reaches the outer
  attempt, which runs again. Mark the outer request `[Retryable]` where a nested retry was relied on. Everything
  else is unchanged for a host that opts into none of the additions.

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

[Unreleased]: https://github.com/bochmann-software/bsft-foundation/compare/v2.3.0...HEAD
[2.3.0]: https://github.com/bochmann-software/bsft-foundation/compare/v2.2.0...v2.3.0
[2.2.0]: https://github.com/bochmann-software/bsft-foundation/compare/v2.1.0...v2.2.0
[2.1.0]: https://github.com/bochmann-software/bsft-foundation/releases/tag/v2.1.0
