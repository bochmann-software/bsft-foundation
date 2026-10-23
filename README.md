# Bsft Foundation

The open-source core of the Bsft framework for .NET: a mediator with a composable pipeline, an event
aggregator and a saga engine, integration messaging with a transactional outbox, a data layer with
filtering, search and multi-tenancy, scheduling and retention, correlation, logging and
observability, caching, resilience, localization, a module loader and production hosting defaults.
Every package is small, is wired with one `AddBsft*` call and is held to 100 % line coverage,
mutation testing and a mechanical structure and naming check.

Licensed under the [MIT License](LICENSE). Packages are published on
[nuget.org](https://www.nuget.org/packages?q=Bsft) under the `Bsft.` prefix.

## Packages

| Area | Package | What it gives a host |
| --- | --- | --- |
| Core | [`Bsft.Core`](docs/core.md) | Exceptions, validation, the `Unit` type and the closure calculator every other package builds on. |
| Core | [`Bsft.Schema`](docs/schema.md) | A bounded JSON Schema subset: reads a schema from an untrusted party and validates documents against it. |
| Core | [`Bsft.Calendar`](docs/calendar.md) | A business calendar abstraction with business-day aware delay arithmetic. |
| Mediator | [`Bsft.Mediator.Abstractions`](docs/mediator.md) | Commands, queries, notifications, handlers and `IPipelineBehavior<,>`. |
| Mediator | [`Bsft.Mediator`](docs/mediator.md) | The dispatcher and its pipeline: logging, tracing, transactions, validation. |
| Mediator | [`Bsft.Mediator.EventAggregator`](docs/mediator.md) | Forwards mediator notifications onto the event aggregator. |
| Events and sagas | [`Bsft.EventAggregator`](docs/event-aggregator.md) | In-process publish/subscribe between components that do not know each other. |
| Events and sagas | [`Bsft.EventAggregator.Saga`](docs/saga.md) | A saga and flow engine on top of the event aggregator, with durable timeouts ([saga-timeouts.md](docs/saga-timeouts.md)). |
| Events and sagas | [`Bsft.EventAggregator.Saga.EFCore`](docs/saga.md) | EF Core persistence for saga state and timeouts. |
| Events and sagas | [`Bsft.EventAggregator.Saga.Correlation`](docs/saga.md) | Carries the correlation id through durable saga timeouts. |
| Messaging | [`Bsft.Messaging.Abstractions`](docs/messaging.md) | Integration events, the message bus contract and the outbox store contract. |
| Messaging | [`Bsft.Messaging`](docs/messaging.md) | An in-memory message bus, integration event staging and the outbox dispatcher. |
| Messaging | [`Bsft.Messaging.EFCore`](docs/messaging.md) | The EF Core transactional outbox: events stored with the state change, delivered by a leased dispatcher. |
| Messaging | [`Bsft.Messaging.EFCore.Postgres`](docs/messaging.md) | PostgreSQL `LISTEN`/`NOTIFY` wake-up, so the dispatcher drains on commit. |
| Messaging | [`Bsft.Messaging.RabbitMQ`](docs/messaging.md) | The RabbitMQ transport. |
| Messaging | [`Bsft.Messaging.Correlation`](docs/messaging.md) | Carries the correlation id across messages. |
| Messaging | [`Bsft.Outbox.Abstractions`](docs/outbox.md) | The lease protocol, payload and side effect of a durable outbox row. |
| Messaging | [`Bsft.Outbox`](docs/outbox.md) | The outbox kernel: drain pass, retry schedule and retirement policy every outbox shares. |
| Messaging | [`Bsft.Outbox.EFCore`](docs/outbox.md) | An EF Core implementation of the outbox lease protocol. |
| Data | [`Bsft.Data.Contracts`](docs/data.md) | Paging, filtering, sorting and search contracts with no dependencies, for servers and clients alike. |
| Data | [`Bsft.Data`](docs/data.md) | Query filtering, pagination, multi-tenancy and full-text search ([search.md](docs/search.md)). |
| Data | [`Bsft.Data.Postgres`](docs/search.md) | The PostgreSQL full-text search provider. |
| Data | [`Bsft.Data.SqlServer`](docs/search.md) | The SQL Server full-text search provider. |
| Data | [`Bsft.Data.Sqlite`](docs/search.md) | The SQLite FTS5 full-text search provider. |
| Data | [`Bsft.Idempotency`](docs/idempotency.md) | Idempotent unsafe endpoints: an `Idempotency-Key` header and replay of the first response. |
| Data | [`Bsft.Idempotency.EFCore`](docs/idempotency.md) | EF Core persistence for idempotency records. |
| Background work | [`Bsft.Scheduling`](docs/scheduling.md) | Recurring (cron, interval) and delayed background jobs on a leased, durable dispatcher. |
| Background work | [`Bsft.Scheduling.EFCore`](docs/scheduling.md) | EF Core persistence for job runs. |
| Background work | [`Bsft.Scheduling.Correlation`](docs/scheduling.md) | Carries the correlation id into job execution. |
| Background work | [`Bsft.Retention`](docs/retention.md) | A hosted sweeper that runs one bounded purge pass per set of records a package owns. |
| Correlation | [`Bsft.Correlation.Abstractions`](docs/correlation.md) | The correlation id contract, free of ASP.NET Core. |
| Correlation | [`Bsft.Correlation`](docs/correlation.md) | Correlation ids for ASP.NET Core, inbound and outbound. |
| Correlation | [`Bsft.Correlation.Http`](docs/correlation.md) | The outbound `HttpClient` half, safe for clients. |
| Observability | [`Bsft.Instrumentation`](docs/observability.md) | A vendor-neutral `ActivitySource`/`Meter` layer the packages emit through. |
| Observability | [`Bsft.Observability`](docs/observability.md) | OpenTelemetry wiring for traces, metrics and logs that discovers every Bsft instrumentation source. |
| Observability | [`Bsft.Logging`](docs/logging.md) | A rolling file logging provider with structured output, paging and live tailing. |
| Cross-cutting | [`Bsft.Mapping`](docs/mapping.md) | Convention-based object mapping and LINQ projections EF Core can translate. |
| Cross-cutting | [`Bsft.Caching`](docs/caching.md) | A memory or distributed cache with keys, tags and invalidation. |
| Cross-cutting | [`Bsft.Caching.Mediator`](docs/caching.md) | A pipeline behavior that caches query results. |
| Cross-cutting | [`Bsft.Caching.EventAggregator`](docs/caching.md) | Cache invalidation driven by in-process events. |
| Cross-cutting | [`Bsft.Caching.Data`](docs/caching.md) | Folds the current tenant into every cache key. |
| Cross-cutting | [`Bsft.Resilience`](docs/resilience.md) | Retry, circuit breaker and timeout for outgoing HTTP clients (Polly). |
| Cross-cutting | [`Bsft.Resilience.Mediator`](docs/resilience.md) | Retry as a pipeline behavior. |
| Cross-cutting | [`Bsft.Localization`](docs/localization.md) | Database-backed localization with tenant support. |
| Cross-cutting | [`Bsft.Localization.EFCore`](docs/localization.md) | EF Core persistence for translations and their seed. |
| Hosting | [`Bsft.Hosting`](docs/hosting.md) | ASP.NET Core host defaults: OpenAPI, versioning, CORS, rate limiting, health checks, ProblemDetails. |
| Hosting | [`Bsft.Hosting.BackgroundWork`](docs/hosting.md#background-work) | Run-once startup operations and bounded batching pipelines for a generic host, without the web stack. |
| Hosting | [`Bsft.Modules`](docs/modules.md) | Composes an application from independently versioned modules. |
| Hosting | [`Bsft.Http`](docs/http.md) | HTTP primitives: entity tags, conditional GET and conditional writes. |
| Hosting | [`Bsft.Http.Egress`](docs/http-egress.md) | An outbound egress guard against server-side request forgery, with its `HttpClient` wiring. |
| Testing | [`Bsft.Testing`](docs/testing.md) | Test harnesses and doubles for integration, mediator and saga tests. |

[docs/packages.md](docs/packages.md) maps every package to its topic page and to the method a host
calls to register it.

## Getting started

```bash
dotnet add package Bsft.Mediator
```

```csharp
builder.Services.AddBsftMediator(mediator =>
{
    mediator.RegisterServicesFromAssembly(typeof(Program).Assembly);
    mediator.EnableTracing();
});
```

Each package's README (shown on nuget.org as well) covers install, registration, options and the
seams it can be extended through; the topic pages under [docs/](docs/packages.md) explain how it
works. Contributors start with [docs/package-conventions.md](docs/package-conventions.md), which is
binding for every package, and [docs/naming.md](docs/naming.md).

## Building

```bash
dotnet build Bsft.sln -c Release
dotnet test Bsft.sln -c Release --no-build
node .github/verify-structure.mjs
```

The .NET 8 SDK and Node 24 are all it needs.

## Commercial extensions

Bochmann Software GmbH builds commercial packages on top of this foundation (authentication and
identity, multi-tenant organizations, billing, notifications, chat, AI and more). They are not part of
this repository and are not required for anything in it. Contact
[Bochmann Software](https://bochmann-software.com) for details.

## Contributing and security

Contributions are welcome; see [CONTRIBUTING.md](CONTRIBUTING.md). Please report vulnerabilities
privately as described in [SECURITY.md](SECURITY.md).
