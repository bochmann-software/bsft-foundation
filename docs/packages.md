# Packages

Every package under `src/`, the topic page that documents it and its DI entry
point. A package without a topic section is a documentation bug, see
[package-conventions.md](package-conventions.md#5-documentation). The entry
point column names the `IServiceCollection` method a host calls (`—` for
`.Abstractions` packages, which register nothing). `Bsft.Templates` is the one
row that names a command instead: it is a `dotnet new` template pack and ships
no assembly, so there is no entry point for a host to call.

| Package | Documented in | Entry point |
| --- | --- | --- |
| `Bsft.Caching.Data` | [caching.md](caching.md) | `CachingDataServiceCollectionExtensions.AddBsftCachingData` |
| `Bsft.Caching.EventAggregator` | [caching.md](caching.md) | `AddBsftCacheInvalidation` |
| `Bsft.Caching.Mediator` | [caching.md](caching.md) | `CachingMediatorServiceCollectionExtensions.AddBsftCachingMediator` |
| `Bsft.Caching` | [caching.md](caching.md) | `AddBsftCaching` |
| `Bsft.Calendar` | [calendar.md](calendar.md) | `CalendarServiceCollectionExtensions.AddBsftCalendar` |
| `Bsft.Core` | [core.md](core.md) | — (contracts, exceptions and pure functions; nothing to register) |
| `Bsft.Correlation.Abstractions` | [correlation.md](correlation.md) | — |
| `Bsft.Correlation.Http` | [correlation.md](correlation.md) | `AddBsftCorrelationHttp` |
| `Bsft.Correlation` | [correlation.md](correlation.md) | `AddBsftCorrelation` |
| `Bsft.Data.Contracts` | [data.md](data.md) | — |
| `Bsft.Data.Postgres` | [data.md](data.md) | `DataPostgresServiceCollectionExtensions.AddBsftDataPostgres` |
| `Bsft.Data.SqlServer` | [data.md](data.md) | `DataSqlServerServiceCollectionExtensions.AddBsftDataSqlServer` |
| `Bsft.Data.Sqlite` | [data.md](data.md) | `DataSqliteServiceCollectionExtensions.AddBsftDataSqlite` |
| `Bsft.Data` | [data.md](data.md) | `DataServiceCollectionExtensions.AddBsftDataQueryFiltering` / `.AddBsftDataMultiTenancy` |
| `Bsft.EventAggregator.Saga.Correlation` | [saga.md](saga.md) | `AddBsftSagaCorrelation` |
| `Bsft.EventAggregator.Saga.EFCore` | [saga.md](saga.md) | `AddBsftSagaEFCore<TContext>` |
| `Bsft.EventAggregator.Saga` | [saga.md](saga.md) | `AddBsftSaga` |
| `Bsft.EventAggregator` | [event-aggregator.md](event-aggregator.md) | `AddBsftEventAggregator` |
| `Bsft.Hosting` | [hosting.md](hosting.md) | `HostingServiceCollectionExtensions.AddBsftDefaults`, `ObserverServiceCollectionExtensions.AddBsftObservers` |
| `Bsft.Hosting.BackgroundWork` | [hosting.md](hosting.md#background-work) | `BackgroundWorkServiceCollectionExtensions.AddBsftBackgroundWorkRunOnce`, `PipelineServiceCollectionExtensions.AddBsftBatchingWriter` |
| `Bsft.Http.Egress` | [http-egress.md](http-egress.md) | `HttpEgressServiceCollectionExtensions.AddBsftHttpEgress` |
| `Bsft.Http` | [http.md](http.md) | — (static helpers only) |
| `Bsft.Idempotency.EFCore` | [idempotency.md](idempotency.md) | `AddBsftIdempotencyEFCore<TContext>` |
| `Bsft.Idempotency` | [idempotency.md](idempotency.md) | `AddBsftIdempotency` (`UseBsftIdempotency`) |
| `Bsft.Instrumentation` | [observability.md](observability.md) | `InstrumentationServiceCollectionExtensions.AddBsftInstrumentationSource` |
| `Bsft.Localization` | [localization.md](localization.md) | `LocalizationServiceCollectionExtensions.AddBsftLocalization` |
| `Bsft.Localization.EFCore` | [localization.md](localization.md) | `LocalizationEFCoreServiceCollectionExtensions.AddBsftLocalizationEFCore<TContext>` |
| `Bsft.Logging` | [logging.md](logging.md) | `LoggingServiceCollectionExtensions.AddBsftLogging` |
| `Bsft.Mapping` | [mapping.md](mapping.md) | `MappingServiceCollectionExtensions.AddBsftMapping` |
| `Bsft.Mediator.Abstractions` | [mediator.md](mediator.md) | — |
| `Bsft.Mediator.EventAggregator` | [mediator.md](mediator.md) | `AddBsftMediatorEventAggregator` |
| `Bsft.Mediator` | [mediator.md](mediator.md) | `AddBsftMediator` |
| `Bsft.Messaging.Abstractions` | [messaging.md](messaging.md) | — |
| `Bsft.Messaging.Correlation` | [messaging.md](messaging.md) | `AddBsftMessagingCorrelation` |
| `Bsft.Messaging.EFCore` | [messaging.md](messaging.md) | `AddBsftMessagingEFCore<TContext>` |
| `Bsft.Messaging.EFCore.Postgres` | [messaging.md](messaging.md) | `AddBsftMessagingEFCorePostgres<TContext>` |
| `Bsft.Messaging.RabbitMQ` | [messaging.md](messaging.md) | `AddBsftRabbitMQMessaging` |
| `Bsft.Messaging` | [messaging.md](messaging.md) | `AddBsftMessaging` |
| `Bsft.Modules` | [modules.md](modules.md) | `ModulesServiceCollectionExtensions.AddBsftModules` |
| `Bsft.Outbox.Abstractions` | [outbox.md](outbox.md) | — |
| `Bsft.Outbox.EFCore` | [outbox.md](outbox.md) | — |
| `Bsft.Outbox` | [outbox.md](outbox.md) | `OutboxServiceCollectionExtensions.AddBsftOutbox<TPayload>` |
| `Bsft.Observability` | [observability.md](observability.md) | `ObservabilityServiceCollectionExtensions.AddBsftObservability` |
| `Bsft.Resilience.Mediator` | [resilience.md](resilience.md) | `ResilienceMediatorServiceCollectionExtensions.AddBsftResilienceMediator` |
| `Bsft.Resilience` | [resilience.md](resilience.md) | `ResilienceServiceCollectionExtensions.AddBsftResilientHttpClient` |
| `Bsft.Retention` | [retention.md](retention.md) | `AddBsftRetention<TPass>` |
| `Bsft.Scheduling.Correlation` | [scheduling.md](scheduling.md) | `AddBsftSchedulingCorrelation` |
| `Bsft.Scheduling.EFCore` | [scheduling.md](scheduling.md) | `AddBsftSchedulingEFCore<TContext>` |
| `Bsft.Schema` | [schema.md](schema.md) | `SchemaServiceCollectionExtensions.AddBsftSchema` |
| `Bsft.Scheduling` | [scheduling.md](scheduling.md) | `AddBsftScheduling` |
| `Bsft.Testing` | [testing.md](testing.md) | `TestingServiceCollectionExtensions.AddBsftTestContexts` |
