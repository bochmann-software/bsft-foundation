# Bsft.Hosting.OpenApi

The OpenAPI and API-versioning part of the BSFT host defaults: Swashbuckle document generation, URL-segment
API versioning, and the one-call `AddBsftDefaults` / `UseBsftDefaults` that registers them together with every
`Bsft.Hosting` component and orders the middleware.

## Install

```bash
dotnet add package Bsft.Hosting.OpenApi
```

Requires `Bsft.Hosting` (pulled transitively), Swashbuckle.AspNetCore and Asp.Versioning.Mvc.ApiExplorer. They
live here rather than in `Bsft.Hosting` so that a host that pins its own Swashbuckle version can take CORS, rate
limiting and health checks from `Bsft.Hosting` without this package.

## Register

```csharp
using Bsft.Hosting.OpenApi.DependencyInjection;

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

`AddBsftCorrelation()` is not optional: `UseBsftDefaults()` puts `UseBsftCorrelation()` into the pipeline but
does not register its services. The error middleware that `UseBsftDefaults()` adds takes `ICorrelationContext`
per request to stamp the correlation id on the ProblemDetails, so without the registration the first request
fails to resolve it instead of producing the error response. `UseBsftDefaults()` adds, in this order, HTTPS
redirection, correlation, the exception middleware, rate limiting, CORS, compression, Swagger and health checks.

A host that composes the pipeline itself takes `AddBsftSwagger` / `UseBsftSwagger` and `AddBsftApiVersioning`
from this package and the remaining pairs from `Bsft.Hosting.DependencyInjection`.

The one call always brings Swashbuckle and Asp.Versioning at the versions this package is built against; that
is the deliberate price of the convenience. A host that pins its own Swashbuckle version does not reference this
package: it takes `Bsft.Hosting` alone and calls the component pairs it wants, in the order listed above.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| `BsftSwaggerOptions.Title` (`Bsft:Hosting:Swagger`) | entry assembly name | Document title. Must not be blank. |
| `BsftSwaggerOptions.Version` | `"v1"` | Document version, and the document's name. Must not be blank. |
| `BsftSwaggerOptions.IsBearerAuthEnabled` | `false` | Adds the bearer scheme. `AddBearerAuth()` is the fluent way of setting it. |
| `BsftSwaggerOptions.IncludesXmlComments` | `true` | Feeds every XML doc file next to the assembly into the document. |
| `BsftVersioningOptions.DefaultVersion` (`Bsft:Hosting:Versioning`) | `1.0` | Whole part is the major, first decimal the minor. Not negative. |
| `BsftVersioningOptions.ShouldAssumeDefaultVersionWhenUnspecified` | `true` | Unversioned requests get the default. |
| `BsftVersioningOptions.ShouldReportApiVersions` | `true` | Reports available versions in response headers. |

The other `Bsft:Hosting:*` sections `AddBsftDefaults` binds are the `Bsft.Hosting` components' own, listed in
that package's README.

Validation: on start, a blank Swagger title or version and a negative default API version are each rejected,
naming the member.

`UseBsftSwagger` serves the document and the Swagger UI under `/swagger` in the Development environment only,
and redirects `/` there.

## Extend

None. Swashbuckle's own `AddSwaggerGen` options and Asp.Versioning's options stay open to a host that
configures them after this package.

## Documentation

Topic page: [docs/hosting.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/hosting.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
