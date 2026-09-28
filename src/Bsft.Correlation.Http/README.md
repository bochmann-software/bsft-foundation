# Bsft.Correlation.Http

The outbound half of the `Bsft.Correlation` family: an `HttpClient` delegating handler that attaches the
`X-Correlation-Id` of the current scope to every request it sends, so the next service continues the same
trace instead of starting its own.

## Install

```bash
dotnet add package Bsft.Correlation.Http
```

Requires `Bsft.Correlation.Abstractions` (pulled transitively) and
`Microsoft.Extensions.DependencyInjection.Abstractions` for the entry point. No further Bsft packages, and
no ASP.NET Core: this package is deliberately referenceable from a Blazor WebAssembly front end, a console
tool or an integration-test client, and a test pins that closure. A host that also serves requests takes
`Bsft.Correlation`, which references this package and registers the handler for you.

## Register

```csharp
services.AddBsftCorrelationHttp();

services.AddHttpClient<IBillingServiceClient, BillingServiceClient>()
    .AddHttpMessageHandler<CorrelationIdDelegatingHandler>();
```

Register it per client rather than globally, and only on clients that reach your own services. The
handler attaches the header to whatever the client sends, a request following a redirect to another
host included. The identifier is not a credential, but it says how a deployment traces itself, and a
third party has no business reading that.

Nothing else is required. `AddBsftCorrelationHttp()` registers the handler and nothing more — it does not
supply an `ICorrelationContext`, because where the ID comes from differs per host: a server gets it from
`AddBsftCorrelation()` and its inbound middleware, a client registers its own implementation. The call is
idempotent, so a host that calls `AddBsftCorrelation()` (which calls this) and this method itself still
ends up with one handler registration.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| — | — | No options type. The header name is fixed at `CorrelationDefaults.CorrelationId` (`X-Correlation-Id`) so that the middleware, this handler and every adapter package cannot disagree about it. An outbound request that already carries the header keeps its value. |

Validation: none — there is nothing to configure and therefore nothing that can be misconfigured.

## Extend

`ICorrelationContext` — declared in
[`Bsft.Correlation.Abstractions`](../Bsft.Correlation.Abstractions/README.md), it is what the handler reads
the outgoing ID from and the only seam involved. `Bsft.Correlation` supplies the request-scoped
implementation for a server; a client that mints or receives its ID some other way implements the interface
itself. `CorrelationIdDelegatingHandler` is `sealed`.

## Documentation

Topic page: [docs/correlation.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/correlation.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
