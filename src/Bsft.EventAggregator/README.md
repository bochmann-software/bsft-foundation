# Bsft.EventAggregator

An in-process publish/subscribe seam: one `IEventAggregator` a component publishes to and another
subscribes to, without either knowing the other. It is the transport the saga engine, the cache
invalidation adapter and the mediator's notification forwarder ride on.

## Install

```bash
dotnet add package Bsft.EventAggregator
```

Requires nothing beyond `Microsoft.Extensions.DependencyInjection.Abstractions`. No further Bsft
packages — this is the bottom of that stack, not a consumer of it.

## Register

```csharp
services.AddBsftEventAggregator();
```

Nothing else is required. `SimpleEventAggregator` is registered as the singleton `IEventAggregator`,
so publishers and subscribers in the same process see the same instance. Everything that builds on the
aggregator — `Bsft.EventAggregator.Saga`, `Bsft.Caching.EventAggregator`, `Bsft.Mediator.EventAggregator`
— expects this call to have happened; none of them registers an aggregator of its own.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| — | — | The package has no options type. It is one registration with nothing to tune; delivery is synchronous, in-process and to every subscriber. |

Validation: none — there is nothing to configure.

## Extend

`IEventAggregator` is the seam, and the whole package. Replace `SimpleEventAggregator` by registering
your own implementation before calling `AddBsftEventAggregator` where delivery has to be ordered,
buffered, or handed to a scheduler rather than run on the publisher's thread.

A subscription lasts until it is unsubscribed, and the aggregator holds the handler strongly for that
whole time. Because it is a singleton, a subscriber that goes out of use without unsubscribing is kept
alive by it and keeps being invoked on every later publish. Whoever subscribes owns unsubscribing, and
that needs the very delegate that was passed in, so a lambda written inline at the subscribe call is a
subscription nobody can remove again. Keep it in a field, or subscribe from a type whose lifetime is
the process's anyway.

What the shipped implementation deliberately does not do is cross a process boundary: an event
published here reaches this instance and no other. Fanning out to a fleet is `Bsft.Messaging`'s job,
and mixing the two up is the mistake this section exists to prevent.

## Documentation

Topic page: [docs/event-aggregator.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/event-aggregator.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
