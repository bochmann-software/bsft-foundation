# Bsft.Correlation

The server half of the `Bsft.Correlation` family: the request-scoped correlation context, the
ASP.NET Core middleware that reads or mints the `X-Correlation-Id` of a request, and the logging enricher
that puts it into every log scope. It also references `Bsft.Correlation.Http`, so one call wires the
outbound handler that carries the id to the next service.

## Install

```bash
dotnet add package Bsft.Correlation
```

Requires `Bsft.Correlation.Abstractions` and `Bsft.Correlation.Http` (both pulled transitively). It
carries a `FrameworkReference` to `Microsoft.AspNetCore.App` for the middleware, which is why the
contract and the outbound handler live in packages of their own: a Blazor WebAssembly or console client
references those and never acquires the server stack. No further Bsft packages.

## Register

```csharp
services.AddBsftCorrelation();

app.UseBsftCorrelation();
```

`UseBsftCorrelation` belongs early in the pipeline — before anything whose log output should carry the
ID. To propagate the ID outbound, add the handler to the clients that need it:

```csharp
services.AddHttpClient<IPaymentsClient, PaymentsClient>()
    .AddHttpMessageHandler<CorrelationIdDelegatingHandler>();
```

`AddBsftCorrelation` registers that handler through `Bsft.Correlation.Http`, so no separate
registration is needed and the type is used from the `Bsft.Correlation.Http` namespace. Add
`app.UseMiddleware<CorrelationIdLoggingEnricher>()` only if you enrich the log scope somewhere other
than where `UseBsftCorrelation` sits; the correlation middleware already opens a scope of its own.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| — | — | The package has no options type. The header name is fixed at `CorrelationDefaults.CorrelationId` (`X-Correlation-Id`) so that the middleware, the handler and every adapter package agree on one constant. |

Validation: none — there is nothing to configure and therefore nothing that can be misconfigured. What
the middleware does validate is the *inbound* value: a client-supplied ID is accepted only when it
matches `^[A-Za-z0-9._-]{1,128}\z`, and a fresh GUID is minted otherwise. That is not a preference, it
is what stops a caller from injecting CRLF into the response header or forging a log line.

## Extend

`ICorrelationContext` is the only seam, and it is declared in
[`Bsft.Correlation.Abstractions`](../Bsft.Correlation.Abstractions/README.md) so that a consumer can
depend on the ID without depending on ASP.NET Core. This package supplies `CorrelationContext` as its
scoped implementation and registers it under both the concrete type and the interface, because the
middleware writes through the class and everything else reads through the interface.

`CorrelationContext` is the one type here deliberately left open for inheritance: a test host derives
from it to pin a fixed ID (`Bsft.Testing`'s `TestCorrelationContext`). The middleware and the enricher
are `sealed`, as is the delegating handler in
[`Bsft.Correlation.Http`](../Bsft.Correlation.Http/README.md).

## Documentation

Topic page: [docs/correlation.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/correlation.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
