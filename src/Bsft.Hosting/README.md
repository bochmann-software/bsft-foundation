# Bsft.Hosting

The ASP.NET Core host defaults of the BSFT framework: CORS, rate limiting, response compression, JSON,
HTTPS redirection, health checks, the exception middleware that turns a `BsftException` into RFC 9457
ProblemDetails, the CRUD convention's filter transport, host logging policy and startup validation, each
with its own `AddBsft*` registration and, where it has middleware, a `UseBsft*` step. Swagger, API
versioning and the one-call `AddBsftDefaults` / `UseBsftDefaults` that composes everything are in
`Bsft.Hosting.OpenApi`.

## Install

```bash
dotnet add package Bsft.Hosting
```

Requires `Bsft.Core`, `Bsft.Correlation`, `Bsft.Data.Contracts` (the filter AST the transport parses,
deliberately not `Bsft.Data`, so this package stays free of EF Core), `Bsft.Logging` (the file
provider the host logging policy switches on) and `Bsft.Hosting.BackgroundWork`, which holds the
background-work pieces below. It carries a `FrameworkReference` to
`Microsoft.AspNetCore.App` and no Swashbuckle or Asp.Versioning, so a host that pins its own Swashbuckle
version (to keep a generated OpenAPI document byte-identical, for one) can take it. A worker or console
host should reference the individual packages it needs rather than this one: a console tool that only
wants the background work takes `Bsft.Hosting.BackgroundWork`.

## Register

```csharp
using Bsft.Correlation.DependencyInjection;
using Bsft.Hosting;                     // BsftExceptionMiddleware
using Bsft.Hosting.DependencyInjection;

services.AddBsftCorrelation();
services.AddBsftHttpsDefaults(configuration);
services.AddBsftCors(configuration, cors => cors.AllowOrigins("https://app.example.com").AllowCredentials());
services.AddBsftRateLimiting(configuration);
services.AddBsftHealthChecks();
```

```csharp
app.UseBsftHttpsRedirection();
app.UseBsftCorrelation();
app.UseMiddleware<BsftExceptionMiddleware>();
app.UseAuthentication(); // before the limiter, so a Principal partition sees the caller
app.UseBsftRateLimiting();
app.UseBsftCors();
app.UseBsftHealthChecks();
```

The exception middleware has no `UseBsft*` step of its own: a host composing the pipeline adds it with
`app.UseMiddleware<BsftExceptionMiddleware>()` after correlation, which it needs registered
(`AddBsftCorrelation()`) to stamp the correlation id on the ProblemDetails. A hand-composed pipeline gets
only what it lists: `UseBsftDefaults` turns HTTPS redirection and HSTS on by default, so a host composing its
own calls `UseBsftHttpsRedirection()` first and leaves it out only when TLS terminates upstream.

Each feature has its own `AddBsft*` registration, and a `UseBsft*` step where it adds middleware —
`AddBsftCors`, `AddBsftRateLimiting`, `AddBsftCompression`, `AddBsftJsonDefaults`, `AddBsftHttpsDefaults`,
`AddBsftHostLogging`, `AddBsftSecurityHeaders` (`UseBsftSecurityHeaders`), `AddBsftFilterTransport`, `AddBsftHealthChecks`, `AddBsftStartupValidation` — so a
host composes the pipeline itself. The one-call `AddBsftDefaults` / `UseBsftDefaults`, which registers
all of them together with Swagger and API versioning and orders the middleware, is in
`Bsft.Hosting.OpenApi` (`using Bsft.Hosting.OpenApi.DependencyInjection;`).

CORS can also expose response headers, accept origins by a code-only predicate and use another policy name;
`IBsftCorsOriginEvaluator` hands the same origin decision to code outside CORS:

```csharp
services.AddBsftCors(configuration, cors => cors
    .ExposeHeaders("ETag", "Location")
    .AllowOriginsWhere(origin => origin.StartsWith("http://localhost", StringComparison.Ordinal))
    .AllowCredentials());
// cors.PolicyName = "App" renames the policy; UseBsftCors() applies the configured name.
// inject IBsftCorsOriginEvaluator and call IsOriginAllowed(origin), e.g. for a WebSocket handshake.
```

Background work lives in `Bsft.Hosting.BackgroundWork`, which this package references, and has its own pair,
registered on its own (`using Bsft.Hosting.BackgroundWork.DependencyInjection;`):

```csharp
services.AddBsftBackgroundWorkRunOnce<RegisterInstanceService>(configuration);
```

`RunOnceWithRetryBackgroundServiceBase` is a startup operation that keeps trying until it succeeds or
the host stops, with the backoff in named `BsftRunOnceOptions` and every wait on the injected
`TimeProvider`. `IServiceScopeFactory.RunScopedAsync` / `RunScoped` run one operation against one
scoped service and dispose the scope afterwards on every path, including the one where the operation
threw.

A hot path hands items to a background writer without ever waiting through a bounded pipeline:

```csharp
services.AddBsftBatchingWriter<UsageRecord, UsageBatchCommitter>(configuration)
        .AddBsftPipelineEnrichment<UsageRecord, UsageEnricher>(); // optional, after the writer
// producers inject IBoundedBuffer<UsageRecord> (or IEnrichmentStage<UsageRecord>) and call TryEnqueue
```

`TryEnqueue` drops and counts when the buffer is full, a failing batch is logged and skipped, and the
stop flush has its own deadline. Options live under `Bsft:Hosting:Pipelines:<item type full name>`
(`BsftBoundedPipelineOptions`); counters are read through `IPipelineCounters<TItem>`.

## Configure

Each feature keeps its own options type and its own section rather than nesting into one hosting
options type, because a host may call `AddBsftCors` alone and never call `AddBsftDefaults` (from
`Bsft.Hosting.OpenApi`). Every `AddBsft*` above has an `IConfiguration` overload binding the section
below; a `configure` delegate passed alongside runs after the section, so code wins where code spoke.

| Option | Default | Effect |
| --- | --- | --- |
| `BsftCompressionOptions.EnableBrotli` (`Bsft:Hosting:Compression`) | `false` | Adds the Brotli provider next to Gzip. |
| `BsftCompressionOptions.EnableForHttps` | `false` | Compresses HTTPS responses. Off by default: compression over TLS with attacker-influenced content has been exploitable. |
| `BsftCorsOptions.Origins` (`Bsft:Hosting:Cors`) | empty | The allowed origins. `AllowOrigins(...)` is the fluent way of writing it. |
| `BsftCorsOptions.AreCredentialsAllowed` | `false` | Allows cookies and authorization headers. Ignored when any origin is allowed. |
| `BsftCorsOptions.IsAnyOriginAllowed` | `false` | Any origin, method and header. Development only. |
| `BsftFilterTransportOptions.MaxFilterLength` (`Bsft:Hosting:Filtering`) | `8192` | Longest raw `filter` query value, rejected before any JSON is parsed. |
| `BsftFilterTransportOptions.MaxJsonReaderDepth` | `64` | Hard recursion limit handed to the JSON reader. |
| `BsftFilterTransportOptions.MaxDepth` | `24` | Deepest chain of nested `and`/`or` in the parsed AST. |
| `BsftFilterTransportOptions.MaxNodeCount` | `200` | Most nodes one filter may contain. |
| `BsftHttpsOptions.RedirectionMode` (`Bsft:Hosting:Https`) | `Enabled` | Whether `UseBsftDefaults` adds HSTS and the HTTPS redirect. |
| `BsftJsonOptions.ShouldSerializeEnumsAsString` (`Bsft:Hosting:Json`) | `true` | camelCase string enums instead of numbers. |
| `BsftJsonOptions.ShouldOmitNullValues` | `true` | Omits nulls when writing. |
| `BsftJsonOptions.UsesCaseInsensitiveDeserialization` | `true` | Case-insensitive property matching when reading. |
| `BsftJsonOptions.DefaultDateFormat` | `null` | Reserved; the serializer's own date handling is used. |
| `BsftHostLoggingOptions.DefaultLevel` (`Bsft:Hosting:Logging`) | `Information` | Minimum level for application code. |
| `BsftHostLoggingOptions.MicrosoftLevel` / `SystemLevel` / `AspNetCoreLevel` | `Warning` | Per-category filters. |
| `BsftHostLoggingOptions.UseJsonFormat` | `false` | Switches the **console** to JSON. The file format is `Bsft.Logging`'s own setting. |
| `BsftHostLoggingOptions.IncludeScopes` | `true` | Needed for the correlation id to reach the console. |
| `BsftHostLoggingOptions.EnableConsole` / `EnableDebug` | `true` | Which providers the host switches on. |
| `BsftRateLimitingOptions.PermitLimit` (`Bsft:Hosting:RateLimiting`) | `100` | Requests per window. At least one. |
| `BsftRateLimitingOptions.WindowSeconds` | `60` | Length of the fixed window. At least one. |
| `BsftRateLimitingOptions.Window` | `null` | The window as a `TimeSpan` (`"00:00:30"`); wins over `WindowSeconds` when set. At least one second. Limiters have one too. |
| `BsftRateLimitingOptions.EnableDefaultPolicy` | `true` | Registers the default policy `BsftFixedWindow`; `false` registers none. |
| `BsftRateLimitingOptions.GlobalLimiter` (`...:RateLimiting:GlobalLimiter`) | `null` | One limiter, configured like a named policy's, that covers every request (`RateLimiterOptions.GlobalLimiter`). |
| `BsftRateLimitingOptions.CustomPartitionKeyPrefix` | `custom:` | Prefix of a `Custom` selector's partition key; may be empty. |
| `BsftRateLimitingOptions.RetryAfterSeconds` | `1` | `Retry-After` of a rejection whose limiter cannot estimate one (a concurrency limiter). At least one. |
| `BsftRateLimitingOptions.Policies` (`...:RateLimiting:Policies:<name>`) | empty | Named policies, each a chain of `Limiters` a request has to pass all of. See below. |
| `BsftRateLimitingOptions.PartitionKeySelectors` | empty | Code only: named `Func<HttpContext, string?>` a `Custom` limiter refers to. |

Validation: the filter transport bounds are checked eagerly at `AddBsftFilterTransport` and again on
start — every bound must be positive and `MaxJsonReaderDepth` must be at least `MaxDepth`, otherwise
the reader rejects a filter before the friendlier AST check can. On start, a blank CORS origin, a
non-positive rate-limit value and an inconsistent named rate-limiting policy (named by its configuration path)
are each rejected, naming the member.

`BsftHostLoggingOptions.FileLogging(...)` carries a delegate and `BsftHealthCheckConfiguration` the
external-service and disk-storage checks to build, neither of which a configuration provider can
supply; they stay code-only and have no section. `BsftHostLoggingOptions` binds `Bsft:Hosting:Logging` and not `Bsft:Logging`, which is
`Bsft.Logging.BsftLoggingOptions`' section — a host calling both entry points would otherwise bind two
different shapes out of one section.

`HostingDefaults` holds the names a host may have to spell itself: `CorsPolicyName`
(`"BsftCorsPolicy"`), `RateLimitingPolicyName` (`"BsftFixedWindow"`, which endpoints must ask for by
name — the limiter is not applied globally) and `ConsoleFormatterName` (`"bsft"`).

A named policy is a chain of limiters (`PolicyLimiterOptions`): `Kind` is `FixedWindow`, `SlidingWindow`,
`TokenBucket` or `Concurrency`, `PartitionBy` is `ClientAddress`, `Principal` (claim `ClaimType`, default
`NameIdentifier`; anonymous falls back to the address), `Custom` (a selector of `PartitionKeySelectors`) or
`Global`. `Principal` budgets per authenticated caller; with `ClaimType` set to a client-id claim
(`client_id` or `azp`, whichever the issuer writes) it budgets per OAuth client instead.

An app declares, say, three uploads in flight per guest and 64 overall from configuration alone:

```json
"Bsft": { "Hosting": { "RateLimiting": { "Policies": {
  "upload-streams": { "Limiters": [
    { "Kind": "Concurrency", "PartitionBy": "Principal", "PermitLimit": 3 },
    { "Kind": "Concurrency", "PartitionBy": "Global", "PermitLimit": 64, "RetryAfterSeconds": 5 } ] }
} } } }
```

and maps `group.RequireRateLimiting("upload-streams")`. A rejection is a 429 with `Retry-After` and a
ProblemDetails body carrying `errorCode` `RATE_LIMITED` (`HostingDefaults.RateLimitedErrorCode`), counted on
`bsft.rate_limiting.rejections` per policy. A principal partition needs authentication to run before
`UseBsftRateLimiting`. Details in [docs/hosting.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/hosting.md#rate-limiting).

`UseBsftRateLimiting` needs `AddBsftRateLimiting` (or `AddBsftDefaults` from `Bsft.Hosting.OpenApi`).
ASP.NET Core never disposes its rate limiting middleware's endpoint limiter nor
`RateLimiterOptions.GlobalLimiter`, and each runs a timer that would keep a stopped host reachable; the
container disposes both when the host is disposed. A global limiter therefore belongs to one host and is
not shared between hosts.

## Extend

`IFilterQueryParser` (how the `filter` query value becomes a filter AST, and which transport bounds
reject it) is registered with `TryAdd`, so a host that registered its own parser keeps it.
`IStartupValidator` from `Bsft.Core` is the open seam of startup validation: this package contributes
`ConfigurationValidator` and the hosted service that runs every registered validator, and any package
or application may add its own. `IObserverFailureReporter` and `IObserverGuard` from `Bsft.Core` are registered by `AddBsftObservers` with `TryAdd`, so a host that registered its own keeps it. `BsftHealthCheckConfiguration` composes health checks from the
outside. Nothing else here is replaceable — the middleware, the console formatter and the per-feature
registrations are concrete on purpose.

`BsftHealthCheckConfiguration.AddHostedWorkStartGates()` opts in to the `hosted-work-start-gates` health check (tag `ready`), which is unhealthy while a registered start gate that implements `IHostedWorkStartGateOverdueSignal` reports itself overdue. The gates themselves are registered with `AddBsftHostedWorkStartGate<TGate>()` from `Bsft.Hosting.StartGates`. See [docs/hosting.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/hosting.md#start-gates-for-hosted-loops).

Exception mapping is extensible without replacing the middleware: `AddBsftExceptionMapper<T>()` registers an `IBsftExceptionMapper`
(asked before the built-in mapping, first result wins; status, title, detail, extensions and headers such as `Retry-After`),
`AddBsftExceptionObserver<T>()` an `IBsftExceptionObserver` told about every mapped exception (failures contained), and
`AddBsftExceptionHandler()` an `IExceptionHandler` for `UseExceptionHandler()` with the same mapping. A mapped 500 never
carries exception text. See [docs/hosting.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/hosting.md#mapping-your-own-exceptions).

## Documentation

Topic page: [docs/hosting.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/hosting.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
