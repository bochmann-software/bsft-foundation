# Bsft.Correlation

One id per request, propagated into logs, into outgoing HTTP calls and — through
the adapters — into sagas and scheduled jobs. It is the thread that makes a
distributed trace readable after the fact.

## Contract

```csharp
public interface ICorrelationContext
{
    string CorrelationId { get; }
}
```

The implementation, `CorrelationContext`, is registered **scoped** and mints a
fresh `Guid` per scope by default. Both the interface and the concrete class are
resolvable — the middleware takes the concrete type because it has to *set* the
id, application code should depend on `ICorrelationContext`.

## Setup

```csharp
services.AddBsftCorrelation();

app.UseBsftCorrelation();
```

`AddBsftCorrelation()` takes no options. It registers the scoped
`CorrelationContext` (and `ICorrelationContext` pointing at the same instance)
plus a transient `CorrelationIdDelegatingHandler`.

If you use `Bsft.Hosting`, note that `app.UseBsftDefaults()` already calls
`app.UseBsftCorrelation()` internally — but it does **not** register the
services. `AddBsftCorrelation()` is therefore required whenever
`UseBsftDefaults()` is used, or the middleware fails to resolve its context.

## Inbound: the middleware

`CorrelationIdMiddleware` reads the `X-Correlation-Id` request header, and
writes the effective id back on the response under the same name.

An inbound value is accepted only if it matches `^[A-Za-z0-9._-]{1,128}\z`;
anything else is discarded and a fresh id is generated. The header is attacker-
controlled and ends up in log files, so an unvalidated value would be a log
injection vector and an unbounded allocation. The header name is not configurable;
it is the single shared constant `CorrelationDefaults.CorrelationId`
(`"X-Correlation-Id"`), which the middleware, the delegating handler and every
other component that reads or writes the header reference so the name cannot
drift between them.

## Logging

`CorrelationIdMiddleware` also pushes the id into the `ILogger` scope under the
key `CorrelationId`, so every log line written during the request carries it
without the call sites mentioning it. Nothing else is needed:

```csharp
app.UseBsftCorrelation();
```

`CorrelationIdLoggingEnricher` is a second middleware that pushes the same scope,
reading the id from `ICorrelationContext` instead of setting it. In the standard
pipeline it is redundant — adding it after `UseBsftCorrelation()` opens the
`CorrelationId` scope twice. It exists for a pipeline in which the id reaches the
`ICorrelationContext` some other way and `CorrelationIdMiddleware` is not in
play; the framework itself contains no such pipeline, so treat it as a legacy
type kept for compatibility.

## Outbound: HTTP calls

`CorrelationIdDelegatingHandler`, from `Bsft.Correlation.Http`, adds the header to
outgoing requests, so the downstream service continues the same trace instead of
starting its own:

```csharp
services.AddHttpClient("billing")
    .AddHttpMessageHandler<CorrelationIdDelegatingHandler>();
```

`AddBsftCorrelation()` registers the handler, so a host that runs the pipeline
needs nothing further. A project that only *makes* calls — a Blazor WebAssembly
front end, a console tool, an integration-test client — references
`Bsft.Correlation.Http` alone and calls `AddBsftCorrelationHttp()`, which registers
the handler and nothing else. It supplies no `ICorrelationContext`: where the id
comes from is the client's decision, so it registers its own implementation of the
contract from `Bsft.Correlation.Abstractions`. Both calls are idempotent, so making
them both leaves one handler registration.

`Bsft.Resilience` wires this for you — `AddBsftResilientHttpClient(name)`
registers the handler and orders it *inside* the resilience handler, so a
retried attempt still carries the id. See [resilience.md](resilience.md).

## Beyond the request

A correlation id is only useful if it survives leaving the request. Two adapters
carry it further, each documented on its own page:

- `Bsft.EventAggregator.Saga.Correlation` — `AddBsftSagaCorrelation()` and
  `UseBsftSagaCorrelation()` carry the id into saga steps and through durable
  timeouts. See [saga-timeouts.md](saga-timeouts.md).
- `Bsft.Scheduling.Correlation` — `AddBsftSchedulingCorrelation()` carries the id of
  the enqueuing request into the job execution. See [scheduling.md](scheduling.md).
- `Bsft.Messaging.Correlation` — `AddBsftMessagingCorrelation()` puts the id of the
  staging request on the published message, where it survives leaving the
  process. See [messaging.md](messaging.md).

## Example

```csharp
public sealed class InvoiceService(ICorrelationContext correlation, ILogger<InvoiceService> logger)
{
    public void Export(Guid invoiceId)
    {
        logger.LogInformation(
            "Exporting invoice {InvoiceId} under {CorrelationId}",
            invoiceId, correlation.CorrelationId);
    }
}
```

## The package split

Correlation ships as three packages, because the halves have entirely different
dependency needs:

| Package | Contains | Depends on |
| --- | --- | --- |
| `Bsft.Correlation.Abstractions` | `ICorrelationContext` and the `CorrelationDefaults.CorrelationId` header-name constant | nothing at all |
| `Bsft.Correlation.Http` | `CorrelationIdDelegatingHandler` and the `AddBsftCorrelationHttp()` wiring | the abstractions and `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `Bsft.Correlation` | `CorrelationContext`, `CorrelationIdMiddleware`, `CorrelationIdLoggingEnricher`, the `AddBsftCorrelation()` / `UseBsftCorrelation()` wiring | ASP.NET Core (`Microsoft.AspNetCore.App`), plus the other two |

The split exists for the client side. A Blazor WebAssembly front end reads the id
through `ICorrelationContext` — a pure `System.*` contract — and attaches it to its
own calls with the delegating handler, but cannot reference `Bsft.Correlation`,
whose `FrameworkReference` on `Microsoft.AspNetCore.App` for the middleware makes
it a server-only package. A console tool or an integration-test client is in the
same position. A test pins the dependency closure of each client-safe package —
empty for the abstractions, the two entries above for `.Http` — because the next
dependency to threaten a client bundle is the one nobody thought to forbid.

What is *not* in the client-safe packages is as deliberate as what is.
`CorrelationContext` is the scoped writer the middleware sets, and the
middleware, the logging enricher and the pipeline extension all need ASP.NET Core;
a client neither runs them nor should advertise them.

**The outbound handler is its own package since 1.3.5, and its namespace is
`Bsft.Correlation.Http`.** It is a live implementation, which
[package-conventions.md](package-conventions.md#1-package-families) keeps out of an
`.Abstractions` package by name, and it is the half a client without a request
pipeline still needs. That is the satellite section 1 describes for a live
implementation a dependency-light consumer must be able to take on its own, and this
package is the case the rule is written from. `Bsft.Correlation`
references it, so `AddBsftCorrelation()` keeps registering the handler and a host
that has both halves changes nothing but the `using` directive.

Take the abstractions alone when a project reads the correlation id but neither
hosts the request pipeline nor makes outgoing calls; take `Bsft.Correlation.Http`
when it makes them.

## See also

- [logging.md](logging.md) — the file logger writes the id into every line.
- [observability.md](observability.md) — the id is attached to mediator spans.
