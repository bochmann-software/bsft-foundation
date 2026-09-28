# Bsft.Sample.Orders.Core

The contract half of the orders module: the order entity and its status, the DTO, the module's
DbContext interface, and the events it publishes. It exists so that the host's `AppDbContext` and the
order saga can reference what orders exposes without referencing how it works.

## Run

It is a class library with no entry point. Start `samples/Bsft.Sample.WebApi`.

## What it demonstrates

Rule 3 of [docs/architecture-rules.md](../../docs/architecture-rules.md): `IOrderDbContext` is the
only view the module has of its own tables, and the application's DbContext implements it.

`Events/OrderCreatedNotification` is the in-process signal the saga starts from;
`Events/OrderPlacedIntegrationEvent` is the durable one that leaves the application through
`Bsft.Messaging`. The two are different types on purpose — a notification is an in-memory fact, an
integration event is a message with an id the inbox deduplicates on.
