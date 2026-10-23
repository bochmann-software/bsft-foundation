# Bsft.Hosting

The host defaults: Swagger, API versioning, CORS, rate limiting, response
compression, JSON, HTTPS redirection, health checks, exception handling and
startup validation — registered by one call and ordered by another.

## Setup

```csharp
builder.Services.AddBsftCorrelation();
builder.Services.AddBsftDefaults();

var app = builder.Build();

app.UseBsftDefaults();
```

**`AddBsftCorrelation()` is required.** `UseBsftDefaults()` puts
`UseBsftCorrelation()` into the pipeline but does not register its services, so
omitting it fails at the first request. See [correlation.md](correlation.md).

Both `AddBsftDefaults` and `UseBsftDefaults` are all-or-nothing conveniences over
the individual calls; each feature also has its own `AddBsft*` / `UseBsft*` pair
if you want to compose the host yourself. That is why every feature keeps its own
options type and its own configuration section instead of becoming a group on one
hosting options type: a host may call `AddBsftCors` alone and never call
`AddBsftDefaults` at all.

## What the two calls do

`AddBsftDefaults(...)` registers, in this order: logging, Swagger, API
versioning, CORS, rate limiting, compression, JSON defaults, HTTPS defaults,
health checks and startup validation.

`UseBsftDefaults()` builds the pipeline in this order:

1. HTTPS redirection
2. correlation
3. `BsftExceptionMiddleware`
4. rate limiting
5. CORS
6. compression
7. Swagger
8. health checks

The order is the point. Correlation comes before the exception middleware, so a
failure is logged with its id; the exception middleware wraps everything after
it; rate limiting sits early enough to reject before real work happens.

`BsftExceptionMiddleware` maps a caught `BsftException` subtype (and any other exception, as a generic 500) to a
status code, a title and a detail, and writes the ProblemDetails document through `IProblemDetailsService` when
the host registered one (`AddProblemDetails()`), falling back to writing the document itself otherwise. A host
that wants the same shape every other ProblemDetails response gets — a field added to all of them, an
endpoint-specific tweak keyed on `HttpContext.GetEndpoint()`'s metadata, or a different writer entirely — gets it
here too, with no middleware of its own to buffer and rewrite the body:

```csharp
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = ctx =>
    {
        if (ctx.HttpContext.GetEndpoint()?.Metadata.GetMetadata<LegacyErrorShapeAttribute>() is not null)
            ctx.ProblemDetails.Extensions["code"] = ctx.ProblemDetails.Extensions["errorCode"];
    });
```

The default body — status code, content type and field order — is unchanged for a host that does not register
`AddProblemDetails`, or that registers it without `CustomizeProblemDetails`. See
migration-2.2.0.md.

It does **not** call `UseAuthentication` / `UseAuthorization`. Add those after
it, either directly or with `UseBsftAuth()` from `Bsft.Auth`:

```csharp
app.UseBsftDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

## Configuring

`BsftDefaultsConfiguration` is a fluent facade over the individual option types:

```csharp
builder.Services.AddBsftDefaults(defaults => defaults
    .Swagger(swagger => swagger.Title = "Orders API")
    .Cors(cors => cors.AllowOrigins("https://app.example.com").AllowCredentials())
    .RateLimiting(rate => { rate.PermitLimit = 200; rate.WindowSeconds = 60; })
    .DisableHttpsRedirection());
```

| Method | Configures |
| --- | --- |
| `Swagger` | `BsftSwaggerOptions` |
| `ApiVersioning` | `BsftVersioningOptions` |
| `Cors` | `BsftCorsOptions` |
| `RateLimiting` | `BsftRateLimitingOptions` |
| `Compression` | `BsftCompressionOptions` |
| `Json` | `BsftJsonOptions` |
| `Logging` | `BsftHostLoggingOptions` |
| `DisableHttpsRedirection()` | `BsftHttpsOptions.RedirectionMode` |
| `FilterTransport` | `BsftFilterTransportOptions` |

### Binding from configuration

Every `AddBsft*` call in this package has an `IConfiguration` overload that binds
its own section, and `AddBsftDefaults(configuration)` passes the configuration to
all of them at once. A section that is absent leaves the defaults in place, and a
`configure` delegate passed alongside runs *after* the section, so code wins where
code spoke.

```csharp
builder.Services.AddBsftDefaults(builder.Configuration);
```

| Options type | Section |
| --- | --- |
| `BsftCompressionOptions` | `Bsft:Hosting:Compression` |
| `BsftCorsOptions` | `Bsft:Hosting:Cors` |
| `BsftFilterTransportOptions` | `Bsft:Hosting:Filtering` |
| `BsftHttpsOptions` | `Bsft:Hosting:Https` |
| `BsftJsonOptions` | `Bsft:Hosting:Json` |
| `BsftHostLoggingOptions` | `Bsft:Hosting:Logging` |
| `BsftRateLimitingOptions` | `Bsft:Hosting:RateLimiting` |
| `BsftSwaggerOptions` | `Bsft:Hosting:Swagger` |
| `BsftVersioningOptions` | `Bsft:Hosting:Versioning` |

`BsftHostLoggingOptions` binds `Bsft:Hosting:Logging` and **not** `Bsft:Logging`,
which belongs to `Bsft.Logging.BsftLoggingOptions`. The two describe different
things, and a host that calls both entry points would otherwise bind two shapes
out of one section, each dropping the keys it does not know. `FileLogging(...)`
is a delegate and stays configured in code.

`BsftHealthCheckConfiguration` has no section: it collects the checks to build,
which no configuration provider can express.

### Option defaults

`BsftSwaggerOptions` — `Title` defaults to the entry assembly name, `Version` to
`"v1"`, `IncludesXmlComments` to `true`; `AddBearerAuth()` adds the bearer scheme
to the document.

`BsftVersioningOptions` — `DefaultVersion` `1.0`,
`ShouldAssumeDefaultVersionWhenUnspecified` `true`, `ShouldReportApiVersions`
`true`.

`BsftCorsOptions` — starts closed. `AllowOrigins(params string[])`,
`AllowCredentials()` and `AllowAny()` (development only) are the fluent way of
writing the three settable members `Origins`, `AreCredentialsAllowed` and
`IsAnyOriginAllowed`, which is what the configuration binder writes. A blank
origin is rejected on start: it can never match a request, so it is a typo rather
than a policy.

`BsftRateLimitingOptions` — `PermitLimit` 100, `WindowSeconds` 60; both must be
at least one, and both are per caller: the window is partitioned on the remote
address, so one caller spending its permits costs that caller alone. Behind a
reverse proxy the remote address is the proxy's and every caller lands in one
bucket, so a host deployed that way configures the forwarded-headers middleware;
this package does not enable it, because trusting a forwarded header without
knowing which hop set it lets a caller choose its own bucket.
`RetryAfterSeconds` (1) is the `Retry-After` of a rejection whose limiter cannot
estimate one, and `Policies` holds the named policies; see [Rate limiting](#rate-limiting).
`UseBsftRateLimiting()` adds the limiter middleware but does not
apply the policy globally, so an endpoint asks for it by name through
`HostingDefaults.RateLimitingPolicyName`. It needs `AddBsftRateLimiting()` (or
`AddBsftDefaults()`): ASP.NET Core never disposes the middleware's endpoint
limiter nor `RateLimiterOptions.GlobalLimiter`, each runs a timer that keeps a
stopped host reachable, and the container disposes both with the host. A global
limiter therefore belongs to one host and is not shared between hosts.

`BsftCompressionOptions` — `EnableBrotli` `false`, `EnableForHttps` `false`.
Compression over HTTPS is off by default because combining it with
attacker-influenced content has historically been exploitable; turn it on
knowingly.

`BsftJsonOptions` — `ShouldSerializeEnumsAsString` `true`,
`ShouldOmitNullValues` `true`, `UsesCaseInsensitiveDeserialization` `true`,
`DefaultDateFormat` `null`.

`BsftHostLoggingOptions` — host policy only: per-category levels (`DefaultLevel`
`Information`; `MicrosoftLevel`, `SystemLevel`, `AspNetCoreLevel` `Warning`),
`IncludeScopes` `true`, `EnableConsole` / `EnableDebug` `true`, and
`UseJsonFormat` `false`, which switches the **console** to JSON.

File logging belongs to `Bsft.Logging` and is reached through the nested
`FileLogging(...)`, which configures `Bsft.Logging.BsftLoggingOptions`
(`EnableFileLogging`, `LogDirectory`, `MaxFileSizeMb`, `RetainDays`,
`EnableJsonFormat`, `MinimumLevel` — see [logging.md](logging.md)):

```csharp
builder.Services.AddBsftDefaults(defaults => defaults
    .Logging(log =>
    {
        log.DefaultLevel = LogLevel.Debug;
        log.FileLogging(file =>
        {
            file.LogDirectory = "logs";
            file.RetainDays = 14;
        });
    }));
```

The file logger's `MinimumLevel` is seeded from the most permissive of the four
category levels, so the file never drops an entry that the host's filters let
through to the console — loosening `AspNetCoreLevel` to `Trace` widens the file
too. Setting `MinimumLevel` inside `FileLogging(...)` overrides that, for a file
that is deliberately quieter than the rest. Note that `UseJsonFormat`
and `EnableJsonFormat` are deliberately separate: the console format and the file
format are independent choices.

The host's own registration is `AddBsftHostLogging(...)`, not `AddBsftLogging` —
that name belongs to `Bsft.Logging`, and a single `AddBsftLogging` in both
packages would be an ambiguous call in any file importing both namespaces.

## Rate limiting

`AddBsftRateLimiting` registers ASP.NET Core's rate limiter with one default policy and any number of named
ones; `UseBsftRateLimiting` adds the middleware. No policy applies globally: an endpoint or a route group asks
for one by name with `RequireRateLimiting(name)` or `[EnableRateLimiting(name)]`.

### The default policy

`HostingDefaults.RateLimitingPolicyName` (`"BsftFixedWindow"`) is a fixed window of
`BsftRateLimitingOptions.PermitLimit` requests per `WindowSeconds`, one window per remote address, without a
queue. It is unchanged by the named policies below.

### Named policies

`BsftRateLimitingOptions.Policies` binds from `Bsft:Hosting:RateLimiting:Policies:<name>`. A policy is a chain
of `Limiters`, and a request passes only when every limiter grants it. Each limiter (`PolicyLimiterOptions`)
has:

| Member | Default | Meaning |
| --- | --- | --- |
| `Kind` | `FixedWindow` | `FixedWindow`, `SlidingWindow`, `TokenBucket` or `Concurrency` (`RateLimiterKind`), the algorithms of `System.Threading.RateLimiting`. |
| `PartitionBy` | `ClientAddress` | What the limiter keeps one budget per (`RateLimitPartitionKind`): `ClientAddress`, `Principal`, `Custom` or `Global`. |
| `ClaimType` | `ClaimTypes.NameIdentifier` | The claim that identifies the principal for `Principal`. |
| `PartitionKeySelector` | `null` | The name of a selector in `BsftRateLimitingOptions.PartitionKeySelectors` for `Custom`. |
| `PermitLimit` | `100` | Requests per window, the bucket size of a token bucket, or requests in flight for `Concurrency`. |
| `WindowSeconds` | `60` | The window, or a token bucket's replenishment period. `Concurrency` ignores it. |
| `SegmentsPerWindow` | `4` | Segments of a sliding window. |
| `TokensPerPeriod` | `1` | Tokens a token bucket gains per period. |
| `QueueLimit` | `0` | Requests that may wait, oldest first, instead of being rejected. A waiting request keeps its connection. |
| `RetryAfterSeconds` | `null` | `Retry-After` of a rejection this limiter cannot estimate; `null` uses `BsftRateLimitingOptions.RetryAfterSeconds` (1). |

The partitions:

- `ClientAddress` is the remote address, as in the default policy; requests without one share a partition.
  Behind a reverse proxy it is the proxy's address unless the host configures the forwarded-headers middleware,
  which this package deliberately does not enable.
- `Principal` is the claim value of an authenticated principal. An anonymous request, or a principal without
  the claim, counts against its client address instead. **Authentication must run before
  `UseBsftRateLimiting`**; `UseBsftDefaults` adds the limiter before anything the host adds after it, so a host
  that calls `UseAuthentication` itself does so before `UseBsftDefaults`, or relies on the minimal hosting
  model's automatic `UseAuthentication`, which runs first.
- `Custom` is whatever a selector registered in code returns:
  `o.PartitionKeySelectors["session"] = context => context.User.FindFirst("sid")?.Value;`. A `null` or empty key
  falls back to the client address.
- `Global` is one partition for all requests: a limit on the endpoint group as a whole, normally the last link
  behind a per-caller one.

A per-guest concurrency limit next to an overall one, and a token bucket per guest, from configuration alone:

```json
{
  "Bsft": {
    "Hosting": {
      "RateLimiting": {
        "Policies": {
          "upload-streams": {
            "Limiters": [
              { "Kind": "Concurrency", "PartitionBy": "Principal", "PermitLimit": 3 },
              { "Kind": "Concurrency", "PartitionBy": "Global", "PermitLimit": 64, "RetryAfterSeconds": 5 }
            ]
          },
          "upload-intents": {
            "Limiters": [
              { "Kind": "TokenBucket", "PartitionBy": "Principal", "PermitLimit": 20, "TokensPerPeriod": 1, "WindowSeconds": 3 }
            ]
          }
        }
      }
    }
  }
}
```

```csharp
var uploads = app.MapGroup("/uploads").RequireRateLimiting("upload-streams");
```

A concurrency permit is held until the response has been sent, so a client with many slow requests (a slow
body, Slowloris) uses up its own budget rather than the instance's connections.

**Chains and permits.** A limiter that rejects a request rejects it for the whole chain, and the limiters that
had granted it hand back what they can: a concurrency permit is returned, a permit of a time-based limiter is
spent. Concurrency limiters are therefore asked first, whatever their position in `Limiters`, and the
time-based ones in the order listed. A request that a later time-based limiter rejects has spent a permit of
each earlier time-based limiter, two in fact, because ASP.NET Core's middleware tries again before it rejects.
That only costs a caller that is being limited already; put the limiter most likely to reject first.

Validation runs on start and names the configuration path of each offending member: a policy without
limiters, a policy named like the default one, a non-positive limit, window, segment count or token count, a
negative queue, a principal limiter without a claim type, and a custom limiter naming a selector that is not
registered.

### Rejections

A rejected request is answered with 429 and:

- `Retry-After`, in whole seconds rounded up: the rejecting limiter's own estimate where it has one (a fixed
  window answers its window), otherwise the limiter's `RetryAfterSeconds` or
  `BsftRateLimitingOptions.RetryAfterSeconds`.
- A ProblemDetails body with `type`, `title` (`"Too many requests."`), `status`, `detail`, `errorCode`
  `RATE_LIMITED` (`HostingDefaults.RateLimitedErrorCode`), `retryAfterSeconds` and, when the request has one,
  `correlationId`. It is written through `IProblemDetailsService` when the host registered it, as
  `BsftExceptionMiddleware` does, and directly otherwise.
- One count on the counter `bsft.rate_limiting.rejections` (`HostingDefaults.RateLimitRejectionsInstrument`)
  of the `Bsft.Hosting` instrumentation source, tagged `policy` with the policy's name, or `global` for a
  rejection by `RateLimiterOptions.GlobalLimiter`.

This is `RateLimiterOptions.OnRejected`. A host that set its own `OnRejected` before `AddBsftRateLimiting`
keeps it, and one that sets it afterwards replaces this one.

## Health checks

```csharp
builder.Services.AddBsftHealthChecks(health => health
    .AddExternalService("billing", "https://billing.example.com/health")
    .AddDiskStorage(minFreeMb: 500));

// a database probe is the host's own, on the same builder
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>(tags: ["ready"]);

app.UseBsftHealthChecks();
```

`AddDiskStorage` rejects a non-positive threshold at registration.
`AddExternalService` rejects anything but an absolute HTTP or HTTPS URL, so a
mis-configured address cannot become a probe of the local file system, and it
brings the HTTP client factory it needs rather than leaving the host to guess.

The two checks above are the ones this package can carry out on its own. A
database or tenant-resolution probe would need EF Core and `Bsft.Data`, which
this package stays free of on purpose, so a host adds those itself onto the same
builder and tags them `"ready"` for the readiness endpoint to pick up.

## Background work

Two things every host writes by hand around a `BackgroundService`, in
`Bsft.Hosting.BackgroundWork`. They ship in their own package of the same name, which depends on no web
package, so a console tool or a CLI takes them without ASP.NET Core, Swashbuckle or API versioning;
`Bsft.Hosting` references that package, so a web host has them too. Their registrations
(`AddBsftBackgroundWorkRunOnce`, `AddBsftBatchingWriter`, `AddBsftPipelineEnrichment`) are in the namespace
`Bsft.Hosting.BackgroundWork.DependencyInjection`.

### A startup operation that has to succeed

`RunOnceWithRetryBackgroundServiceBase` is the shape of an operation that has to
happen exactly once and has to keep trying until it does: apply a migration,
register the instance somewhere, prime a cache. It attempts the operation, backs
off after a failure and comes back, and stops for one of two reasons only — the
operation succeeded, or the host is going away.

```csharp
internal sealed class RegisterInstanceService : RunOnceWithRetryBackgroundServiceBase
{
    private readonly IServiceScopeFactory _scopeFactory;

    public RegisterInstanceService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<BsftRunOnceOptions> options,
        TimeProvider timeProvider,
        ILogger<RegisterInstanceService> logger)
        : base(options, timeProvider, logger) => _scopeFactory = scopeFactory;

    protected override Task RunOnceAsync(CancellationToken ct) =>
        _scopeFactory.RunScopedAsync<IInstanceRegistry>((registry, token) => registry.RegisterAsync(token), ct);
}

builder.Services.AddBsftBackgroundWorkRunOnce<RegisterInstanceService>(options =>
{
    options.InitialRetryDelay = TimeSpan.FromSeconds(5);
    options.MaxRetryDelay = TimeSpan.FromMinutes(5);
    options.BackoffFactor = 2;
});

// or bound from configuration, section
// "Bsft:Hosting:RunOnce:MyApp.Startup.RegisterInstanceService"
builder.Services.AddBsftBackgroundWorkRunOnce<RegisterInstanceService>(builder.Configuration);
```

The options are *named* after the service type, so several startup operations
coexist with separate schedules, and they are validated by `ValidateOnStart()`.

| Option | Default | Means |
| --- | --- | --- |
| `StartupDelay` | `0` | How long to wait before the first attempt. |
| `InitialRetryDelay` | 5 s | The delay after the first failure. |
| `MaxRetryDelay` | 5 min | The cap the widening delay stops at. |
| `BackoffFactor` | `2` | What the delay is multiplied by after each failure; `1` retries on a fixed delay. |
| `MaxAttempts` | `0` | How many attempts the operation gets; `0` is until it succeeds or the host stops. |

Two failure modes are handled here rather than in every copy of the loop. An
operation that throws *synchronously*, before it ever returns a task, would
otherwise escape `ExecuteAsync` and take the process down under the default
`BackgroundServiceExceptionBehavior`. And an operation cancelled by a token of its
own — a command timeout — is a failed attempt, not a shutdown: reading it as one
would leave the host running with the work never done and nothing in the log. The
attempt is therefore inspected through its task rather than caught, the same
discipline `PollLoop` applies to a loop that never ends. Waiting goes through the
injected `TimeProvider`, so a test drives the whole backoff schedule on virtual
time.

`Bsft.Core` would be the tidier home for this and is not available: it declares
**no package references at all**, which is the property the `.Abstractions` rule
in [package-conventions.md](package-conventions.md) relies on, and a hosted
service needs `Microsoft.Extensions.Hosting`. `Bsft.Hosting` already has every one
of those references through its `Microsoft.AspNetCore.App` framework reference, so
the base class costs its closure nothing.

### Running against a scoped service

`RunScopedAsync` on `IServiceScopeFactory` creates a scope, resolves one service
from it and disposes the scope afterwards, whatever the operation did:

```csharp
await _scopeFactory.RunScopedAsync<IOrderService>((orders, ct) => orders.SweepAsync(ct), stoppingToken);

var count = await _scopeFactory.RunScopedAsync<IOrderService, int>((orders, ct) => orders.CountAsync(ct));

_scopeFactory.RunScoped<IOrderReader>(reader => reader.Warm());
var name = _scopeFactory.RunScoped<IOrderReader, string>(reader => reader.Describe());
```

The async overloads take an `AsyncServiceScope` and dispose it asynchronously,
which is what a scoped `DbContext` and a pooled connection want. The result of the
result-returning overload outlives the scope, so it must not be — or hold —
something the scope owns; return a projection rather than a tracked entity.

The point is the failure path: a scope disposed only where the operation
*succeeded* leaks one scope per failure, which is a slow memory leak nobody
attributes to the loop that caused it.

## Bounded pipelines

A hot path that has to record something (a usage measurement, an insight, an audit
breadcrumb) must not wait for the store that keeps it. `Bsft.Hosting.BackgroundWork.Pipelines`
is the generic shape of that hand-off: a bounded buffer that never blocks the
producer, a hosted worker that commits in batches, and an optional enrichment
stage in front.

```csharp
builder.Services
    .AddBsftBatchingWriter<UsageRecord, UsageBatchCommitter>(options =>
    {
        options.Capacity = 5_000;
        options.MaxBatchSize = 200;
        options.FlushInterval = TimeSpan.FromSeconds(1);
    })
    .AddBsftPipelineEnrichment<UsageRecord, UsageEnricher>(); // optional, after the writer

// the producer
public sealed class UsageRecorder(IBoundedBuffer<UsageRecord> buffer)
{
    public void Record(UsageRecord usage) => buffer.TryEnqueue(usage); // never waits
}
```

`TryEnqueue` returns at once. When the buffer is full the item is dropped and
counted; there is no mode in which the producer waits. Whoever cannot accept a
lost item needs a transactional store, not this buffer. `IBatchCommitter<TItem>`
commits one batch and fails by throwing; the worker logs the failure, counts the
whole batch and goes on with the next one. A batch closes when it holds
`MaxBatchSize` items or `FlushInterval` after its first item, whichever comes first.

On stop the buffer refuses new items and the worker commits what is queued within
`FlushOnStopTimeout`. That budget is the worker's own: the host's stop token is not
consulted, because a stage in front of the worker may already have used up the host's
shutdown time. A commit that ignores its token is abandoned when the budget passes and
the items still queued are counted as failed, so `StopAsync` cannot hang on a
committer. Keep `FlushOnStopTimeout` (plus `EnrichmentOnStopTimeout`, if a stage is
registered) inside `HostOptions.ShutdownTimeout`; the options validator checks that at start
and refuses a pair that does not fit.

The enrichment stage (`IEnrichmentStage<TItem>` for the producer,
`IPipelineEnricher<TItem>` for the work) runs each item through the enricher with
a per-item `EnrichmentTimeout`. A fault, a `null` result, a timeout and an abort
each count as one failed enrichment, and the raw item goes on in place of the
enriched one. The enrichment is raced against its limit rather than trusted to
honor its token, so an enricher that never returns can hold neither the worker nor
the shutdown, and the late fault of an abandoned enrichment is observed. When the
stage itself is full the item skips enrichment and goes to the writer raw. On stop
the stage enriches what is queued for at most `EnrichmentOnStopTimeout` (or until
the host's stop token fires); the rest goes on raw, counted as skipped and logged
once. The stage does not preserve order: an item that finds it full goes on raw
ahead of older items still waiting for enrichment. Register the stage after the
writer, which `AddBsftPipelineEnrichment` enforces. The writer also waits for the
stage to finish (up to `EnrichmentOnStopTimeout` + `FlushOnStopTimeout`) before it
completes the buffer, so a host that stops every service at once
(`HostOptions.ServicesStopConcurrently`) loses nothing either. Registering it twice for one item type changes nothing; the first committer
registered for an item type stays.

A host runs one pipeline per item type. The options are named after the item type, read
once when the pipeline is built (a reload is not applied) and bound from `Bsft:Hosting:Pipelines:<item type full name>` by the configuration
overload:

| Option | Default | Means |
| --- | --- | --- |
| `Capacity` | 1000 | Items the buffer holds before it drops. |
| `MaxBatchSize` | 100 | Items per commit at most. |
| `FlushInterval` | 1 s | How long a batch waits for more items after its first. |
| `FlushOnStopTimeout` | 5 s | How long the final flush may take. |
| `EnrichmentCapacity` | 1000 | Items awaiting enrichment before they skip it. |
| `EnrichmentTimeout` | 30 s | How long one enrichment may take. |
| `EnrichmentOnStopTimeout` | 5 s | How long the stage keeps enriching its queue once the host stops it. |

`IPipelineCounters<TItem>` (read-only) exposes `Enqueued`, `Dropped`, `Committed`, `Failed`,
`FailedBatches`, `EnrichmentFailed` and `EnrichmentSkipped`. The same outcomes go
to the counter `bsft.pipeline.items` of the instrumentation source
`BackgroundWorkDefaults.PipelineInstrumentationSourceName` (`Bsft.Hosting`, the name the source has
always had; `HostingDefaults.PipelineInstrumentationSourceName` carries the same value), tagged with
`pipeline` (the item type's full name) and `outcome`. The pieces are in the package
`Bsft.Hosting.BackgroundWork`, not in `Bsft.Hosting`: they are hosted services and options of the generic
host, and a tool that has no web host should not have to ship the web API stack to batch writes. The
package depends on `Bsft.Instrumentation` and `Microsoft.Extensions.Hosting` (for `HostOptions`), which are
both Foundation or generic-host packages.

## Startup validation

`AddBsftStartupValidation()` — included in `AddBsftDefaults()` — runs every
registered `IStartupValidator` during startup through a hosted service. That is
what turns a mediator command with no handler, an incomplete mapping profile or a
search group with no index into a boot failure instead of a runtime surprise. The
contract lives in [core.md](core.md).

## HTTPS redirection

```csharp
app.UseBsftHttpsRedirection(HttpsRedirectionMode.Disabled);
```

The parameter is an enum, not a bool. Note the polarity when migrating: the old
boolean parameter meant *disabled*, whereas the enum's default,
`HttpsRedirectionMode.Enabled`, means the redirect is on. See
the 1.3 migration notes.

`UseBsftDefaults()` reads the same decision from `BsftHttpsOptions`, which
`DisableHttpsRedirection()` and the `Bsft:Hosting:Https` section both write. A
pipeline built without `AddBsftHttpsDefaults` still redirects: the safe default
is on.

## Observers

`services.AddBsftObservers()` registers what a type that tells several observers about an event needs:
the `IObserverGuard` that contains an observer failure, an `IObserverFailureReporter` that logs it at
warning level (the observer type and the exception type; the exception itself, which may hold data of
the observed operation, only when `ObserverFailureLoggingOptions.IncludeExceptionDetails` is set through
`AddBsftObservers(options => ...)`) and the open generic `ObserverComposite<TObserver>`.
`services.AddBsftObserver<TObserver, TImplementation>()` adds one observer, in registration order and once
per implementation; observers are singletons, so a contract registered as scoped or transient is refused.
All of it is `TryAdd`. The types themselves, and how to write a composite over a
contract of your own, are in [core.md](core.md#observers).

## Constants

`HostingDefaults` holds the names a host may have to spell itself:
`CorsPolicyName` (`"BsftCorsPolicy"`), `RateLimitingPolicyName`
(`"BsftFixedWindow"`), `RateLimitedErrorCode` (`"RATE_LIMITED"`),
`RateLimitRejectionsInstrument` (`"bsft.rate_limiting.rejections"`) and
`ConsoleFormatterName` (`"bsft"`).

## Example

The smallest useful host:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBsftCorrelation();
builder.Services.AddBsftDefaults(d => d.Swagger(s => s.Title = "Orders API"));
builder.Services.AddControllers();

var app = builder.Build();

app.UseBsftDefaults();
app.MapControllers();
app.Run();
```

See the getting-started guide for the same host with the mediator
wired in, and `samples/Bsft.Sample.WebApi` for the full composition.
