# Migration 2.0.0

2.0.0 is a major release. It collects the breaking changes that could not ship in a minor release,
above all the reorganisation of every namespace by feature. This page lists what a consumer of the
Bsft Foundation packages changes, in the order a consumer applies it: the `.Abstractions`
namespaces first, then the feature namespaces together with the renames of the naming rules
([Naming](#naming)).

## Changed: `SagaStepConfigurator` takes the builder as a type argument

`SagaStepConfigurator<TState, TEvent>` held its `SagaBuilder<TState>`, and the builder created the
configurator, so each type depended on the other. The configurator is now
`SagaStepConfigurator<TState, TEvent, TBuilder>`: `StartWith`, `WaitFor` and `Or` return
`SagaStepConfigurator<TState, TEvent, SagaBuilder<TState>>`, and `Then`, `When` and `WhenNot` return
`TBuilder`, which is the same `SagaBuilder<TState>` as before. A fluent flow definition compiles
unchanged.

The builder's definition tracking moved to `ISagaDefinitionRegistry` (`Definitions`,
`Register<TState>`), which `ISagaBuilderFactory` extends, so code that reads
`ISagaBuilderFactory.Definitions` is unchanged as well.

| Change | Consumer action |
| --- | --- |
| `SagaStepConfigurator<TState, TEvent>` is now `SagaStepConfigurator<TState, TEvent, TBuilder>` | Only code that names the type spells the third argument: `SagaStepConfigurator<TState, TEvent, SagaBuilder<TState>>`, or use `var` |
| `ISagaBuilderFactory.Definitions` and `Register<TState>` are declared on `ISagaDefinitionRegistry`, which `ISagaBuilderFactory` extends | None for callers; an own implementation of `ISagaBuilderFactory` keeps both members, since it implements both interfaces |

## Changed: `.Abstractions` packages declare the namespace of the package they abstract

An `.Abstractions` package now declares the namespace of the package it abstracts, as
`Microsoft.Extensions.Logging.Abstractions` declares `Microsoft.Extensions.Logging`:
`Bsft.Messaging.Abstractions` declares `namespace Bsft.Messaging`, and its `Outbox/` folder
`namespace Bsft.Messaging.Outbox`. The seam and the implementation's entry points share one
namespace, so a consumer imports one namespace instead of two, and a type can move between the two
assemblies without a source break (docs/package-conventions.md, section 3).

This step changes no package or assembly name; [Naming](#naming) renames some packages.
`.Contracts` packages are unchanged. In the Foundation four `.Abstractions` packages move:
`Bsft.Correlation`, `Bsft.Mediator`, `Bsft.Messaging` and `Bsft.Outbox`, each with `.Abstractions`
as package name and without it as namespace.

`Abstractions/` folders inside a non-`.Abstractions` package (`Bsft.Mapping.Abstractions`,
`Bsft.Scheduling.Abstractions`, `Bsft.Modules.Abstractions` and similar) are folder namespaces, not
packages. The feature namespaces of the next section dissolve them.

| Change | Consumer action |
| --- | --- |
| `namespace Bsft.<X>.Abstractions[.<Folder>]` is now `namespace Bsft.<X>[.<Folder>]` | Rewrite the `using` directives and qualified names |
| Reflection over namespaces (`typeof(T).Namespace`, `Type.GetType("Bsft.Messaging.Abstractions.IMessageBus, ...")`, serialized type names) | Update the string; assembly-qualified names keep the assembly name `Bsft.Messaging.Abstractions` and change only the namespace part |

## Changed: namespaces follow features (ADR-0013)

Every package now names its namespaces after what the code is for, not after what kind of type it
is ([ADR-0013](design/adr-0013-namespaces-by-feature.md), docs/package-conventions.md, sections 2
and 3). The role folders `Services`, `Models`, `Commands`, `Queries`, `Endpoints`, `Stores`,
`Exceptions`, `Options`, `Extensions` and the folder namespaces called `Abstractions` are gone. A
type now lives in the namespace of its feature, or in the package root when the whole package speaks
it (an options class, an exception every feature throws, the package's central seam). Each namespace
equals its folder, which the build enforces with IDE0130.

This step renames no package, type or member: only namespaces move. [Naming](#naming) renames
packages, types and members as well. Behavior is unchanged, with the persisted names below and those
of the naming section as the only data a consumer has to look at.

Apply this section after the `.Abstractions` section above.

### `AddBsft*`, `MapBsft*` and `UseBsft*` live in `<Package>.DependencyInjection`

Every package keeps the extension methods that wire it into a host in one namespace,
`<Package>.DependencyInjection`, as `Microsoft.Extensions.DependencyInjection` and the ASP.NET Core
packages do for theirs. In 1.x they sat in the package root or in an `Extensions` folder. A host
therefore imports one namespace per package it wires:

```csharp
using Bsft.Correlation.DependencyInjection;
using Bsft.Hosting.DependencyInjection;
using Bsft.Mediator.DependencyInjection;

builder.Services.AddBsftCorrelation();
builder.Services.AddBsftDefaults();
builder.Services.AddBsftMediator(mediator => mediator
    .RegisterServicesFromAssembly(typeof(Program).Assembly));
```

A call the compiler cannot resolve fails with CS1061; add the directive.

### Names the framework persists

A few names are derived from a type's full name and outlive the process. 2.0.0 keeps none of the
1.x names; each one below changes with its type.

| Name | Where it is stored | 2.0.0 |
| --- | --- | --- |
| Logging categories | `Logging:LogLevel` keys and log queries | A category is the full name of the logging type, so a filter on an old namespace prefix no longer matches. Filter on the package root (`Bsft.Messaging`) or the new feature namespace |
| EF Core model snapshots of a consumer's migrations | `*ModelSnapshot.cs` | Entity type names in the snapshot are full names. The next `dotnet ef migrations add` rewrites them; the generated migration contains no schema operation. Review it and keep it |

The same holds for a consumer's own types: an event registered with `AddEvent<T>()` and no name is
published under `T`'s full name, so moving such a type renames the event.

| Change | Consumer action |
| --- | --- |
| Types moved from role namespaces (`Services`, `Models`, `Commands`, `Endpoints`, `Abstractions`, ...) to feature namespaces | Rewrite the `using` directives and qualified names; the build names each one |
| `AddBsft*`, `MapBsft*`, `UseBsft*` moved to `<Package>.DependencyInjection` | `using <Package>.DependencyInjection;` |
| Log level filters on removed namespaces | Filter on the package root or the new namespace |
| Reflection over namespaces (`typeof(T).Namespace`, `Type.GetType("...")`, serialized type names) | Update the string |
| Own analyzers or conventions that expect `Services`/`Models` folders | Nothing required: the IDE0130 setting lives in the framework's own `.editorconfig` and does not reach a consumer. Follow docs/package-conventions.md where you want the same layout |

## Naming

2.0.0 applies [naming.md](naming.md) to every identifier of the framework: two-letter acronyms in
capitals (`AI`, `EF`, `MQ`, `IP`, `UI`), no abbreviations (`Org`, `Config`, `Dedup`, `Seq`, `Ref`,
`Dest`, `Spec`), American spelling (`Canceled`, `Behavior`), one suffix per role (`*Validator`,
`*Sweeper`, `*Reader`, `*Event`, `*QueryParameters`), the retrieval verbs `Get`/`Find`/`List` by what
a method returns, and canonical parameter names. Apply this section together with the feature
namespaces above.

Behavior is unchanged. What a consumer stores or sends is listed under
[Wire and persisted names](#wire-and-persisted-names), each with the step it needs; there is no
compatibility shim for any of them (naming.md N7.1, N7.4).

A named argument (`AddBsftFoo(config: ...)`, `new Bar(time: ...)`) fails with CS1739 and takes the
parameter name of naming.md N6.1: `cancellationToken`, `configuration`, `timeProvider`, `principal`,
`httpContext`, `serviceProvider`, or the unabbreviated word (`organizationUnitId`, `destination`).

### Packages

Package ids and assembly names follow the namespace root (naming.md N3.6). NuGet ids compare without
case, so the old spelling still restores the package; write the new one so that the id matches the
namespace.

| 1.x | 2.0.0 |
| --- | --- |
| `Bsft.EventAggregator.Saga.EfCore` | `Bsft.EventAggregator.Saga.EFCore` |
| `Bsft.Idempotency.EfCore` | `Bsft.Idempotency.EFCore` |
| `Bsft.Localization.EfCore` | `Bsft.Localization.EFCore` |
| `Bsft.Messaging.EfCore` | `Bsft.Messaging.EFCore` |
| `Bsft.Messaging.EfCore.Postgres` | `Bsft.Messaging.EFCore.Postgres` |
| `Bsft.Messaging.RabbitMq` | `Bsft.Messaging.RabbitMQ` |
| `Bsft.Outbox.EfCore` | `Bsft.Outbox.EFCore` |
| `Bsft.Scheduling.EfCore` | `Bsft.Scheduling.EFCore` |

### Types renamed

A type whose namespace changed only because a namespace segment was renamed (`EfCore` → `EFCore`) is
not listed here. These public types changed their simple name; the namespace column is the 2.0.0
one.

| Namespace (2.0.0) | 1.x | 2.0.0 |
| --- | --- | --- |
| `Bsft.Data` | `QueryConfigBuilder` | `QueryConfigurationBuilder` |
| `Bsft.Data.MultiTenancy.Sandboxes` | `SandboxTenantSpec` | `SandboxTenantSpecification` |
| `Bsft.Data.Postgres` | `CreateFullTextIndexRequest` | `PostgresCreateFullTextIndexRequest` |
| `Bsft.Data.Postgres` | `FullTextMigrationBuilderExtensions` | `PostgresFullTextMigrationBuilderExtensions` |
| `Bsft.Data.Search` | `IOrgScopedEntity` | `IOrganizationScopedEntity` |
| `Bsft.Data.SqlServer` | `CreateFullTextIndexRequest` | `SqlServerCreateFullTextIndexRequest` |
| `Bsft.Data.SqlServer` | `FullTextMigrationBuilderExtensions` | `SqlServerFullTextMigrationBuilderExtensions` |
| `Bsft.Data.Sqlite` | `CreateFullTextIndexRequest` | `SqliteCreateFullTextIndexRequest` |
| `Bsft.Data.Sqlite` | `FullTextMigrationBuilderExtensions` | `SqliteFullTextMigrationBuilderExtensions` |
| `Bsft.EventAggregator.Saga.EFCore` | `EfCoreSagaInstanceReader` | `EFCoreSagaInstanceReader` |
| `Bsft.EventAggregator.Saga.EFCore` | `EfCoreSagaPersistenceWindow` | `EFCoreSagaPersistenceWindow` |
| `Bsft.EventAggregator.Saga.EFCore` | `EfCoreSagaStore` | `EFCoreSagaStore` |
| `Bsft.EventAggregator.Saga.EFCore` | `EfCoreSagaTimeoutReader` | `EFCoreSagaTimeoutReader` |
| `Bsft.EventAggregator.Saga.EFCore` | `EfCoreSagaTimeoutStore` | `EFCoreSagaTimeoutStore` |
| `Bsft.EventAggregator.Saga.EFCore` | `IEfCoreSagaStore` | `IEFCoreSagaStore` |
| `Bsft.EventAggregator.Saga.EFCore` | `IEfCoreSagaTimeoutStore` | `IEFCoreSagaTimeoutStore` |
| `Bsft.EventAggregator.Saga.EFCore.DependencyInjection` | `SagaEfCoreServiceCollectionExtensions` | `SagaEFCoreServiceCollectionExtensions` |
| `Bsft.Hosting.DependencyInjection` | `LoggingServiceCollectionExtensions` | `HostLoggingServiceCollectionExtensions` |
| `Bsft.Idempotency.EFCore.DependencyInjection` | `IdempotencyEfCoreServiceCollectionExtensions` | `IdempotencyEFCoreServiceCollectionExtensions` |
| `Bsft.Localization.EFCore.DependencyInjection` | `LocalizationEfCoreServiceCollectionExtensions` | `LocalizationEFCoreServiceCollectionExtensions` |
| `Bsft.Logging.LogFiles` | `LogReadQuery` | `LogReadRequest` |
| `Bsft.Mediator` | `Mediator` | `DefaultMediator` |
| `Bsft.Messaging.EFCore.DependencyInjection` | `MessagingEfCoreServiceCollectionExtensions` | `MessagingEFCoreServiceCollectionExtensions` |
| `Bsft.Messaging.EFCore.Inbox` | `InboxPruner` | `InboxSweeper` |
| `Bsft.Messaging.RabbitMQ` | `BsftRabbitMqOptions` | `BsftRabbitMQOptions` |
| `Bsft.Messaging.RabbitMQ` | `RabbitMqMessageFormatException` | `RabbitMQMessageFormatException` |
| `Bsft.Messaging.RabbitMQ` | `RabbitMqQueueType` | `RabbitMQQueueType` |
| `Bsft.Messaging.RabbitMQ.DependencyInjection` | `RabbitMqServiceCollectionExtensions` | `RabbitMQServiceCollectionExtensions` |
| `Bsft.Outbox.EFCore` | `EfCoreOutboxStore` | `EFCoreOutboxStore` |
| `Bsft.Scheduling.EFCore.DependencyInjection` | `SchedulingEfCoreServiceCollectionExtensions` | `SchedulingEFCoreServiceCollectionExtensions` |

### Members renamed

The public members (methods, properties, constants and enum values) whose name changed, by the
declaring type's 2.0.0 name. Implementations and overrides follow their interface or base member and
are not repeated.

| Type (2.0.0) | 1.x | 2.0.0 |
| --- | --- | --- |
| `Bsft.Caching.Invalidation.CacheTagIndex` | `GetKeys` | `ListKeys` |
| `Bsft.Caching.Invalidation.ICacheTagIndex` | `GetKeys` | `ListKeys` |
| `Bsft.Core.Polling.IPollReporter` | `ReportPassCancelledWithoutStop` | `ReportPassCanceledWithoutStop` |
| `Bsft.Data.Contracts.Pagination.SortItem` | `Dir` | `Direction` |
| `Bsft.Data.Search.IOrganizationScopedEntity` | `OrgUnitId` | `OrganizationUnitId` |
| `Bsft.Data.Search.SearchScopeState` | `AllowedOrgUnitIds` | `AllowedOrganizationUnitIds` |
| `Bsft.EventAggregator.Saga.Diagnostics.ISagaTelemetry` | `TimeoutCancelled` | `TimeoutCanceled` |
| `Bsft.EventAggregator.Saga.EFCore.DependencyInjection.SagaEFCoreServiceCollectionExtensions` | `AddBsftSagaEfCore` | `AddBsftSagaEFCore` |
| `Bsft.EventAggregator.Saga.EFCore.DependencyInjection.SagaEFCoreServiceCollectionExtensions` | `AddBsftSagaEfCoreWithTimeouts` | `AddBsftSagaEFCoreWithTimeouts` |
| `Bsft.EventAggregator.Saga.EFCore.EFCoreSagaStore` | `GetAsync` | `FindAsync` |
| `Bsft.EventAggregator.Saga.EFCore.EFCoreSagaStore` | `RemoveAsync` | `DeleteAsync` |
| `Bsft.EventAggregator.Saga.EFCore.EFCoreSagaTimeoutStore` | `GetDueAsync` | `ListDueAsync` |
| `Bsft.EventAggregator.Saga.ISagaStore` | `GetAsync` | `FindAsync` |
| `Bsft.EventAggregator.Saga.ISagaStore` | `RemoveAsync` | `DeleteAsync` |
| `Bsft.EventAggregator.Saga.InMemorySagaStore` | `GetAsync` | `FindAsync` |
| `Bsft.EventAggregator.Saga.InMemorySagaStore` | `RemoveAsync` | `DeleteAsync` |
| `Bsft.EventAggregator.Saga.Timeouts.ISagaTimeoutStore` | `GetDueAsync` | `ListDueAsync` |
| `Bsft.EventAggregator.Saga.Timeouts.InMemorySagaTimeoutStore` | `GetDueAsync` | `ListDueAsync` |
| `Bsft.Idempotency.EFCore.DependencyInjection.IdempotencyEFCoreServiceCollectionExtensions` | `AddBsftIdempotencyEfCore` | `AddBsftIdempotencyEFCore` |
| `Bsft.Localization.EFCore.DependencyInjection.LocalizationEFCoreServiceCollectionExtensions` | `AddBsftLocalizationEfCore` | `AddBsftLocalizationEFCore` |
| `Bsft.Localization.ITranslationReader` | `GetForKey` | `ListForKey` |
| `Bsft.Localization.ITranslationReader` | `GetForLocale` | `ListForLocale` |
| `Bsft.Localization.ITranslationReader` | `GetForLocaleOrFallback` | `ListForLocaleOrFallback` |
| `Bsft.Localization.ITranslationReader` | `GetLocales` | `ListLocales` |
| `Bsft.Mediator.IRequestMetadata` | `GetAttribute` | `FindAttribute` |
| `Bsft.Mediator.IRequestMetadata` | `GetAttributes` | `ListAttributes` |
| `Bsft.Mediator.MediatorDefaults` | `OrgAuthorization` | `OrganizationAuthorization` |
| `Bsft.Messaging.EFCore.DependencyInjection.MessagingEFCoreServiceCollectionExtensions` | `AddBsftInboxEfCore` | `AddBsftInboxEFCore` |
| `Bsft.Messaging.EFCore.DependencyInjection.MessagingEFCoreServiceCollectionExtensions` | `AddBsftMessagingEfCore` | `AddBsftMessagingEFCore` |
| `Bsft.Messaging.EFCore.Postgres.DependencyInjection.PostgresServiceCollectionExtensions` | `AddBsftMessagingEfCorePostgres` | `AddBsftMessagingEFCorePostgres` |
| `Bsft.Messaging.RabbitMQ.DependencyInjection.RabbitMQServiceCollectionExtensions` | `AddBsftRabbitMqMessaging` | `AddBsftRabbitMQMessaging` |
| `Bsft.Modules.IModuleCatalog` | `GetModules` | `ListModules` |
| `Bsft.Modules.IModuleHost` | `GetModuleInfo` | `ListModuleInfo` |
| `Bsft.Modules.ModuleCatalog` | `FromConfigFile` | `FromConfigurationFile` |
| `Bsft.Modules.ModuleCatalog` | `GetModules` | `ListModules` |
| `Bsft.Modules.ModuleHost` | `GetModuleInfo` | `ListModuleInfo` |
| `Bsft.Modules.ModulesConfiguration` | `FromConfigFile` | `FromConfigurationFile` |
| `Bsft.Scheduling.CronExpression` | `GetNextOccurrence` | `FindNextOccurrence` |
| `Bsft.Scheduling.EFCore.DependencyInjection.SchedulingEFCoreServiceCollectionExtensions` | `AddBsftSchedulingEfCore` | `AddBsftSchedulingEFCore` |
| `Bsft.Scheduling.EFCore.DependencyInjection.SchedulingEFCoreServiceCollectionExtensions` | `AddBsftStoredSchedulesEfCore` | `AddBsftStoredSchedulesEFCore` |
| `Bsft.Scheduling.IJobStore` | `GetAsync` | `FindAsync` |
| `Bsft.Scheduling.IJobStore` | `GetClaimableAsync` | `ListClaimableAsync` |
| `Bsft.Scheduling.InMemoryJobStore` | `GetAsync` | `FindAsync` |
| `Bsft.Scheduling.InMemoryJobStore` | `GetClaimableAsync` | `ListClaimableAsync` |
| `Bsft.Scheduling.JobSchedule` | `GetNextOccurrence` | `FindNextOccurrence` |
| `Bsft.Scheduling.StoredSchedules.IStoredScheduleService` | `GetAsync` | `FindAsync` |
| `Bsft.Scheduling.StoredSchedules.IStoredScheduleStore` | `GetAsync` | `FindAsync` |
| `Bsft.Scheduling.StoredSchedules.IStoredScheduleStore` | `GetByTenantAsync` | `ListByTenantAsync` |
| `Bsft.Scheduling.StoredSchedules.IStoredScheduleStore` | `GetDueAsync` | `ListDueAsync` |
| `Bsft.Scheduling.StoredSchedules.InMemoryStoredScheduleStore` | `GetAsync` | `FindAsync` |
| `Bsft.Scheduling.StoredSchedules.InMemoryStoredScheduleStore` | `GetByTenantAsync` | `ListByTenantAsync` |
| `Bsft.Scheduling.StoredSchedules.InMemoryStoredScheduleStore` | `GetDueAsync` | `ListDueAsync` |
| `Bsft.Testing.Sagas.ISagaTestHarness` | `GetPendingTimeoutsAsync` | `ListPendingTimeoutsAsync` |
| `Bsft.Testing.Sagas.ISagaTestHarness` | `GetSagaAsync` | `FindSagaAsync` |
| `Bsft.Testing.Sagas.SagaTestHarness` | `GetPendingTimeoutsAsync` | `ListPendingTimeoutsAsync` |
| `Bsft.Testing.Sagas.SagaTestHarness` | `GetSagaAsync` | `FindSagaAsync` |

### Removed and consolidated types

- **`RequiredPermissionsMetadata`** was declared once per package that maps routes. It is now one
  record, `Bsft.Http.RequiredPermissionsMetadata`, with the same shape; code that looked for several
  copies by type now looks for one.

### Wire and persisted names

A name derived from a C# name follows it (naming.md N7.1), and in 2.0.0 a name written out as a
contract of its own follows the glossary as well (naming.md N7.4). Everything below changed, with the
step it needs.

| Name | Where it lives | 1.x → 2.0.0 | Consumer action |
| --- | --- | --- | --- |
| Package ids | `PackageReference`, `PackageVersion` | Table [Packages](#packages) | Update the ids |
| Namespaces and type names, hence logging categories | `Logging:LogLevel` keys, log queries, dashboards | `*.EfCore` → `*.EFCore`, `Bsft.Messaging.RabbitMq` → `Bsft.Messaging.RabbitMQ`, `*Pruner` → `*Sweeper` and the other type renames | A case-only change needs nothing where the filter compares without case (`Logging:LogLevel` does); rename filters naming a renamed type |
| Metric `bsft.saga.timeout.cancelled` | Metrics backends | → `bsft.saga.timeout.canceled` | Update dashboards and alerts |
| Configuration sections of the EF Core and RabbitMQ packages | `appsettings*.json`, environment variables | `…:EfCore` → `…:EFCore`, `Bsft:RabbitMq` → `Bsft:RabbitMQ` | None: configuration keys compare without case |
| Mediator behavior name | Behavior ordering configuration, `DescribeKnownNames` | `OrgAuthorization` → `OrganizationAuthorization` | Rename it where a host names the behavior |
| Sort direction | Query key of `PagedRequest.Sort` | `Sort[i].Dir` → `Sort[i].Direction` (`SortItem.Dir` → `Direction`) | Update clients that sort |
| Log properties | Structured log sinks, log queries, dashboards, alerts | `PrunedCount` → `PurgedCount` (every retention sweeper's summary line, which now reads "Purged …") | Update queries and alerts that match the property names or the message text |
| EF Core model snapshots | `*ModelSnapshot.cs` | Entity type names are full names | The next migration rewrites them |

| Change | Consumer action |
| --- | --- |
| Packages and types renamed | Build; the [package](#packages) and [type](#types-renamed) tables give each new name |
| Members renamed | Build; the [member table](#members-renamed) gives each new name |
| Parameters renamed | Only named arguments break (CS1739); use the canonical name |
| Wire and persisted names | The table above, row by row, before the first 2.0.0 instance starts |
