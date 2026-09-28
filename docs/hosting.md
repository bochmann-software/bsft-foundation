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
`UseBsftRateLimiting()` adds the limiter middleware but does not
apply the policy globally, so an endpoint asks for it by name through
`HostingDefaults.RateLimitingPolicyName`.

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
`Bsft.Hosting.BackgroundWork`.

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

## Constants

`HostingDefaults` holds the names a host may have to spell itself:
`CorsPolicyName` (`"BsftCorsPolicy"`), `RateLimitingPolicyName`
(`"BsftFixedWindow"`) and `ConsoleFormatterName` (`"bsft"`).

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
