---
_layout: landing
---

# Bsft Foundation

Bsft Foundation is the open-source core of the Bsft framework for .NET 8: a mediator with a
pipeline of cross-cutting behaviors, an event aggregator with durable sagas, correlation,
logging, mapping and modules, ASP.NET Core hosting defaults, data access with filtering,
paging and multi-tenancy, caching, localization, and the reliable-messaging set of
idempotency, outbox, messaging and scheduling. Every package is independent: add the one
you need, and each brings its own `AddBsft*` entry point.

Bsft Foundation is free and licensed under MIT.

## Install

The packages are on [nuget.org](https://www.nuget.org/packages?q=Bsft). A web API with the
hosting defaults, correlation and the mediator needs three of them:

```bash
dotnet new webapi -n Orders.Api
cd Orders.Api
dotnet add package Bsft.Hosting
dotnet add package Bsft.Correlation
dotnet add package Bsft.Mediator
```

## A first command

A command, its handler and the host that dispatches it:

```csharp
using Bsft.Core;
using Bsft.Correlation.DependencyInjection;
using Bsft.Hosting.DependencyInjection;
using Bsft.Mediator;
using Bsft.Mediator.DependencyInjection;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBsftCorrelation();
builder.Services.AddBsftDefaults(defaults => defaults
    .Swagger(swagger => swagger.Title = "Orders API"));

builder.Services.AddBsftMediator(mediator => mediator
    .RegisterServicesFromAssembly(typeof(Program).Assembly));

builder.Services.AddControllers();

var app = builder.Build();

app.UseBsftDefaults();
app.MapControllers();

app.Run();

public sealed record PlaceOrder(string OrderNumber, decimal Total) : ICommand<Guid>;

public sealed class PlaceOrderHandler : ICommandHandler<PlaceOrder, Guid>
{
    public Task<Guid> HandleAsync(PlaceOrder command, CancellationToken ct = default)
    {
        if (command.Total <= 0)
            throw new BusinessRuleException("An order total must be positive.");

        return Task.FromResult(Guid.NewGuid());
    }
}

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Place(PlaceOrder command, CancellationToken ct) =>
        Ok(await mediator.SendAsync(command, ct));
}
```

`AddBsftDefaults` registers Swagger, API versioning, CORS, rate limiting, compression, JSON
defaults, health checks and startup validation, and `UseBsftDefaults` puts them into the
pipeline in a working order. `AddBsftCorrelation()` is not optional: the defaults insert the
correlation middleware but do not register its services. [Hosting](../docs/hosting.md)
explains each of them.

## Where to go next

- [Packages](../docs/packages.md): every Foundation package, the page that documents it and its
  entry point.
- [Mediator](../docs/mediator.md), [event aggregator](../docs/event-aggregator.md) and
  [sagas](../docs/saga.md): the messaging core inside one process.
- [Data](../docs/data.md) and [messaging](../docs/messaging.md): persistence, and events between
  services.
- [Package conventions](../docs/package-conventions.md) and [naming](../docs/naming.md): how every
  package is laid out and named, and what a contribution follows.
- [API reference](api/index.md): every public type, generated from the XML documentation.
