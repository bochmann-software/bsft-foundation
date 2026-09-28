# Bsft.Mediator.Abstractions

The CQRS contract of the `Bsft.Mediator` family: the message markers (`ICommand`, `IQuery`,
`INotification`), the handler interfaces, `IMediator` and the pipeline seam. Reference this from a
module that declares messages and handlers; the host references the implementation.

## Install

```bash
dotnet add package Bsft.Mediator.Abstractions
```

Requires nothing. That is the point: a domain assembly that declares `CreateOrderCommand` and its
handler compiles against this package alone and never acquires a dispatcher, a container or a logger.

## Register

```csharp
// Nothing. An .Abstractions package has no DI entry point.
services.AddBsftMediator(m => m.RegisterServicesFromAssembly(typeof(CreateOrderCommand).Assembly));
```

Nothing else is required here. The host registers the implementation from `Bsft.Mediator`, pointing it
at the assemblies whose handlers it should find.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| — | — | No options type, and no configuration: this package is contracts only. `TransactionalAttribute` is the one declarative knob, and it is placed on a message rather than configured on the container. |

Validation: none — the package registers nothing.

## Extend

`IMediator` — dispatches a command, query or notification to its handler.
`ICommandHandler<TCommand>` / `ICommandHandler<TCommand, TResult>` / `IQueryHandler<TQuery, TResult>` /
`INotificationHandler<TNotification>` — what an application implements. The framework finds them by
assembly scan, so the interface is the whole registration contract.
`IPipelineBehavior<TRequest, TResponse>` — wraps a dispatch. Logging, tracing, transactions,
validation, caching and resilience are all behaviors, and so is anything cross-cutting you add.
`INotificationForwarder` — receives every published notification, for bridging the mediator onto another
transport; `Bsft.Mediator.EventAggregator` implements it over the in-process aggregator.
`IRequestMetadataProvider` / `IRequestMetadata` — the concrete type of a dispatched request and the
attributes on it, cached per request type. A behavior is closed over `ICommand<TResult>`, so an
attribute on the message is invisible to its type parameter; this is where an attribute-driven
behavior reads it instead of keeping a reflection cache of its own. `Bsft.Mediator` registers the
implementation.
`ICorrelationIdAccessor` — supplies the id the logging and tracing behaviors stamp onto a dispatch,
without this package depending on the correlation family.

`TransactionalAttribute` on a handler is what `TransactionBehavior` looks for; a request whose handler
does not carry it is dispatched outside a transaction.

## Documentation

Topic page: [docs/mediator.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/mediator.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
