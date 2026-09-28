# Bsft.Mediator

The mediator implementation of the `Bsft.Mediator` family: the dispatcher that resolves a command,
query or notification to its handler, the pipeline that wraps it, and the four shipped behaviors —
logging, tracing, transactions and validation.

## Install

```bash
dotnet add package Bsft.Mediator
```

Requires `Bsft.Mediator.Abstractions` (pulled transitively), `Bsft.Core` for the startup-validator seam
and `Bsft.Instrumentation` for the instrumentation source. Nothing here needs ASP.NET Core:
a worker or a console tool dispatches through it unchanged.

## Register

```csharp
services.AddBsftMediator(mediator =>
{
    mediator.RegisterServicesFromAssembly(typeof(Program).Assembly);
    mediator.EnableTracing();
    mediator.AddBehavior(typeof(LoggingBehavior<,>));
});
```

Nothing else is required. `AddBsftMediator` takes a `MediatorConfiguration` rather than an options type,
because what it configures is which handlers and behaviors exist — a container decision, not a setting a
host would bind from a configuration file. **Behavior order is registration order, innermost last**, so
anything that must run on every dispatch (authorization above all) is registered before anything that
may short-circuit it, such as `Bsft.Caching`'s behavior.

`UseDefaults()` takes that decision out of the host's hands: it registers the cross-cutting behaviors
of every behavior package the application references — tracing, authorization, validation, audit,
retry, transaction, caching — in the order the framework fixes, and applies that order when the
pipeline is folded, so it holds however the packages were registered. One behavior is dropped with
`SuppressDefaultBehavior(MediatorDefaults.Caching)`; the whole chain stays the host's by omitting
`UseDefaults()`.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| `MediatorConfiguration.RegisterServicesFromAssembly(assembly)` | none | Registers every `ICommandHandler`, `IQueryHandler` and `INotificationHandler` the assembly declares. |
| `MediatorConfiguration.AddBehavior(type)` | none | Appends an open-generic `IPipelineBehavior<,>` to the pipeline, innermost last. |
| `MediatorConfiguration.EnableTracing()` | off | Adds `TracingBehavior`, which opens an activity on the `Bsft.Mediator` source per dispatch. |
| `MediatorConfiguration.ForwardNotifications()` | off | Publishes every notification onto the registered `INotificationForwarder`; `Bsft.Mediator.EventAggregator` supplies one. |
| `MediatorConfiguration.UseDefaults()` | off | Registers the canonical cross-cutting chain from the referenced behavior packages, in the framework's order. Implies `EnableTracing()`. |
| `MediatorConfiguration.SuppressDefaultBehavior(name)` | none | Drops one behavior from that chain; a name matching none throws. |

Validation: `MediatorStartupValidator` runs through `Bsft.Core`'s `IStartupValidator` and fails the host
when a registered behavior cannot be constructed, when a request has no handler, or when it has more
than one — before the first request rather than on the first dispatch of the affected message. A
second handler is as quiet a mistake as a missing one: dispatch resolves a single service, so the
container decides which of them runs and the other never does. A command without a result is allowed
one handler of each shape, `ICommandHandler<T>` and `ICommandHandler<T, Unit>`, because dispatch
prefers the first and only falls back to the second.

## Extend

`IPipelineBehavior<TRequest, TResponse>` is the extension point that matters: everything cross-cutting
in this framework — caching, resilience, auditing, the four behaviors here — is one. It is declared in
[`Bsft.Mediator.Abstractions`](../Bsft.Mediator.Abstractions/README.md) with the rest of the contract, so
a behavior can be written without referencing this package.

`IRequestMetadataProvider` is the seam a behavior reads the concrete request type and its attributes
through, registered here as a singleton and declared in the abstractions package so a behavior
satellite can take it without taking this one. It is a cache rather than a policy, so the
registration is `TryAdd` and a host that supplies its own provider keeps it. The shipped
`RequestMetadataProvider` is public, so that provider can decorate it rather than replace it
outright.

`IMediator` itself is replaceable but rarely should be: `Mediator` is `sealed`, and a wrapper that adds
behaviour belongs in the pipeline instead. The four shipped behaviors are `sealed` for the same reason —
compose them, do not inherit them. `IMediatorTelemetry` is the seam for emitting somewhere other than
the shipped `ActivitySource`. Both registrations are `TryAdd`, so a host that registers its own keeps
it whether it did so before or after `AddBsftMediator`.

## Documentation

Topic page: [docs/mediator.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/mediator.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
