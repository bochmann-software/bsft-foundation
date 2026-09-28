# Bsft.Sample.Inventory.Core

The contract half of the inventory module: the entity, its DTO, the module's DbContext interface, and
the events other modules and the saga react to. It exists so that a consumer — the host's
`AppDbContext`, the order saga — can reference what inventory exposes without referencing how it
works.

## Run

It is a class library with no entry point. Start `samples/Bsft.Sample.WebApi`.

## What it demonstrates

Rule 3 of [docs/architecture-rules.md](../../docs/architecture-rules.md): a module owns its tables and
sees them through its own `IInventoryDbContext`, which the application's DbContext implements. Nobody
reaches into another module's tables.

`Events/` carries the module's public vocabulary: the `*Notification` types the mediator publishes
after a write, and the `StockReservation*` events the saga exchanges with the module over
`Bsft.EventAggregator`. Keeping them here rather than in `Bsft.Sample.Inventory` is what lets the saga
in `Bsft.Sample.WebApi` name them without depending on the module's implementation.
