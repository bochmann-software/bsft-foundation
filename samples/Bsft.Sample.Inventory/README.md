# Bsft.Sample.Inventory

The inventory module of the sample: products as a CRUD resource, plus the stock reservation the order
saga asks for. It is a class library loaded by `Bsft.Sample.WebApi`, not a runnable program.

## Run

```bash
dotnet run --project samples/Bsft.Sample.WebApi
```

`InventoryModule` registers itself there; the resource is then at `/api/products`.

## What it demonstrates

`Bsft.Modules` — `InventoryModule` declares its name, version and dependency on `Orders`, registers
its services and seeds its translations.

`Bsft.Mediator` — `Commands/` and `Queries/` hold each message next to its handler, and every handler
implements the matching `ICommandHandler<>` / `IQueryHandler<,>`. `ProductCommandValidator` is shared
by the create and update commands.

`Bsft.Data` and `Bsft.Mapping` — `GetProductsQueryHandler` answers a `PagedRequest` through the query
configuration in `Mapping/ProductQueryConfiguration`, and `Mapping/ProductMappingProfile` maps the entity to
`ProductDto`.

`Bsft.EventAggregator` — `Events/StockReservationSubscriber` subscribes at module start and answers a
`StockReservationRequestedEvent` with a `StockReservationResultEvent`. It is deliberately not called
a handler: nothing dispatches to it, it holds its own subscription, and the conventions reserve
`*Handler` for a type that implements a handler contract.

`Controllers/ProductsController` is the reference implementation of
[docs/crud-conventions.md](../../docs/crud-conventions.md): the routes, the bodies and the thrown
failures that `BsftExceptionMiddleware` maps to status codes.
