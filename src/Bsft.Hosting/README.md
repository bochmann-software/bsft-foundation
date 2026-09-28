# Bsft.Hosting

The ASP.NET Core host defaults of the BSFT framework: Swagger, API versioning, CORS, rate limiting,
response compression, JSON, HTTPS redirection, health checks, the exception middleware that turns a
`BsftException` into RFC 9457 ProblemDetails, the CRUD convention's filter transport, host logging
policy and startup validation — registered by one call and ordered by another.

## Install

```bash
dotnet add package Bsft.Hosting
```

Requires `Bsft.Core`, `Bsft.Correlation`, `Bsft.Data.Contracts` (the filter AST the transport parses,
deliberately not `Bsft.Data`, so this package stays free of EF Core) and `Bsft.Logging` (the file
provider the host logging policy switches on). It carries a `FrameworkReference` to
`Microsoft.AspNetCore.App` plus Swashbuckle and Asp.Versioning, so a worker or console host should
reference the individual packages it needs rather than this one.

## Register

```csharp
services.AddBsftCorrelation();
services.AddBsftDefaults(defaults => defaults
    .Swagger(swagger => swagger.Title = "Orders API")
    .Cors(cors => cors.AllowOrigins("https://app.example.com").AllowCredentials()));

// or bound from configuration, sections "Bsft:Hosting:*"
services.AddBsftDefaults(configuration);
```

```csharp
app.UseBsftDefaults();
```

`AddBsftCorrelation()` is not optional: `UseBsftDefaults()` puts `UseBsftCorrelation()` into the
pipeline but does not register its services. Each feature also has its own `AddBsft*` / `UseBsft*`
pair — `AddBsftCors`, `AddBsftSwagger`, `AddBsftApiVersioning`, `AddBsftRateLimiting`,
`AddBsftCompression`, `AddBsftJsonDefaults`, `AddBsftHttpsDefaults`, `AddBsftHostLogging`,
`AddBsftFilterTransport`, `AddBsftHealthChecks`, `AddBsftStartupValidation` — so a host can compose
the pipeline itself.

Background work has its own pair, registered on its own:

```csharp
services.AddBsftBackgroundWorkRunOnce<RegisterInstanceService>(configuration);
```

`RunOnceWithRetryBackgroundServiceBase` is a startup operation that keeps trying until it succeeds or
the host stops, with the backoff in named `BsftRunOnceOptions` and every wait on the injected
`TimeProvider`. `IServiceScopeFactory.RunScopedAsync` / `RunScoped` run one operation against one
scoped service and dispose the scope afterwards on every path, including the one where the operation
threw.

## Configure

Each feature keeps its own options type and its own section rather than nesting into one hosting
options type, because a host may call `AddBsftCors` alone and never call `AddBsftDefaults`. Every
`AddBsft*` above has an `IConfiguration` overload binding the section below; a `configure` delegate
passed alongside runs after the section, so code wins where code spoke.

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
| `BsftSwaggerOptions.Title` (`Bsft:Hosting:Swagger`) | entry assembly name | Document title. Must not be blank. |
| `BsftSwaggerOptions.Version` | `"v1"` | Document version, and the document's name. Must not be blank. |
| `BsftSwaggerOptions.IsBearerAuthEnabled` | `false` | Adds the bearer scheme. `AddBearerAuth()` is the fluent way of setting it. |
| `BsftSwaggerOptions.IncludesXmlComments` | `true` | Feeds every XML doc file next to the assembly into the document. |
| `BsftVersioningOptions.DefaultVersion` (`Bsft:Hosting:Versioning`) | `1.0` | Whole part is the major, first decimal the minor. Not negative. |
| `BsftVersioningOptions.ShouldAssumeDefaultVersionWhenUnspecified` | `true` | Unversioned requests get the default. |
| `BsftVersioningOptions.ShouldReportApiVersions` | `true` | Reports available versions in response headers. |

Validation: the filter transport bounds are checked eagerly at `AddBsftFilterTransport` and again on
start — every bound must be positive and `MaxJsonReaderDepth` must be at least `MaxDepth`, otherwise
the reader rejects a filter before the friendlier AST check can. On start, a blank CORS origin, a
non-positive rate-limit value, a blank Swagger title or version and a negative default API version are
each rejected, naming the member.

`BsftHostLoggingOptions.FileLogging(...)` carries a delegate and `BsftHealthCheckConfiguration` the
external-service and disk-storage checks to build, neither of which a configuration provider can
supply; they stay code-only and have no section. `BsftHostLoggingOptions` binds `Bsft:Hosting:Logging` and not `Bsft:Logging`, which is
`Bsft.Logging.BsftLoggingOptions`' section — a host calling both entry points would otherwise bind two
different shapes out of one section.

`HostingDefaults` holds the names a host may have to spell itself: `CorsPolicyName`
(`"BsftCorsPolicy"`), `RateLimitingPolicyName` (`"BsftFixedWindow"`, which endpoints must ask for by
name — the limiter is not applied globally) and `ConsoleFormatterName` (`"bsft"`).

## Extend

`IFilterQueryParser` (how the `filter` query value becomes a filter AST, and which transport bounds
reject it) is registered with `TryAdd`, so a host that registered its own parser keeps it.
`IStartupValidator` from `Bsft.Core` is the open seam of startup validation: this package contributes
`ConfigurationValidator` and the hosted service that runs every registered validator, and any package
or application may add its own. `BsftHealthCheckConfiguration` composes health checks from the
outside. Nothing else here is replaceable — the middleware, the console formatter and the per-feature
registrations are concrete on purpose.

## Documentation

Topic page: [docs/hosting.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/hosting.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
