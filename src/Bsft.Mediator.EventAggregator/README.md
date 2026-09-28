# Bsft.Mediator.EventAggregator

The adapter between the `Bsft.Mediator` and `Bsft.EventAggregator` families: an
`INotificationForwarder` that republishes every mediator notification onto the in-process event
aggregator, so a saga or a cache subscriber can react to it without knowing the mediator exists.

## Install

```bash
dotnet add package Bsft.Mediator.EventAggregator
```

Requires `Bsft.Mediator.Abstractions` and `Bsft.EventAggregator`. It supplies neither a mediator nor an
aggregator: both families must already be registered.

## Register

```csharp
services.AddBsftEventAggregator();
services.AddBsftMediator(mediator =>
{
    mediator.RegisterServicesFromAssembly(typeof(Program).Assembly);
    mediator.ForwardNotifications();
});
services.AddBsftMediatorEventAggregator();
```

Both halves are required and neither is enough on its own: `ForwardNotifications()` tells the mediator to
call an `INotificationForwarder`, and `AddBsftMediatorEventAggregator` is what supplies one. Without the
first, this package is registered and never called; without the second, the mediator has nothing to
forward to.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| — | — | No options type. What is forwarded is every notification the mediator publishes, and where it goes is decided by the registered `IEventAggregator`. |

Validation: none — there is nothing to configure.

## Extend

`INotificationForwarder` is the seam, declared in `Bsft.Mediator.Abstractions`; this package is one
implementation of it. Register your own instead to forward onto something else — a message bus, an
outbox — and note that forwarding is in addition to normal handler dispatch, not instead of it.

The aggregator is in-process, so a forwarded notification reaches this instance and no other. Crossing a
process boundary is `Bsft.Messaging`'s job.

## Documentation

Topic page: [docs/mediator.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/mediator.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
