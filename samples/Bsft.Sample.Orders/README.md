# Bsft.Sample.Orders

The orders module of the sample: placing an order, reading it back, and publishing the integration
event that leaves the application. It is a class library loaded by `Bsft.Sample.WebApi`, not a
runnable program.

## Run

```bash
dotnet run --project samples/Bsft.Sample.WebApi
```

`OrdersModule` registers itself there; the resource is then at `/api/orders`.

## What it demonstrates

`Bsft.Modules` — `OrdersModule` is the module contract, with its own translations.

`Bsft.Mediator` — `Commands/CreateOrderCommandHandler` and the two query handlers implement the
matching mediator interfaces; the command handler writes the order and publishes both a domain
notification and an integration event.

`Bsft.Messaging` — `CreateOrderCommandHandler` stages `OrderPlacedIntegrationEvent` in the outbox
inside the same transaction as the write, and `Events/OrderPlacedIntegrationEventHandler` consumes it
on the other side of the dispatcher, under the correlation id restored from the message. It logs that
id on purpose: it is the same one the request that placed the order carried, minutes earlier and on
another thread.

`Bsft.Data` and `Bsft.Mapping` — paged reads through `Mapping/OrderQueryConfiguration` and
`Mapping/OrderMappingProfile`.
