# Bsft.Messaging.Correlation

The bridge between the `Bsft.Correlation` family and the `Bsft.Messaging` family: an integration event
staged while handling a request carries that request's correlation id into a delivery that may happen much
later and in another service.

## Install

```bash
dotnet add package Bsft.Messaging.Correlation
```

Requires `Bsft.Correlation` and `Bsft.Messaging.Abstractions`, and no further Bsft packages. It bridges
the two deliberately rather than either family referencing the other: the messaging packages must not
drag the ASP.NET Core request pipeline into a background dispatcher.

## Register

```csharp
services.AddBsftMessagingCorrelation();
```

Nothing else is required, and the call has no order to observe: it registers its accessor with a plain
`Add` rather than `TryAdd`, so it replaces the ambient-only default of `AddBsftMessaging` whichever way
round the two are called. Both families still have to be registered themselves —
`AddBsftCorrelation` for the request context and `AddBsftMessaging` for the ambient message scope.

## Configure

None. The package has no options type: what it does is decided entirely by the two families it joins.

## Extend

`IMessageCorrelationAccessor` is the seam this package fills, and it is declared in
[`Bsft.Messaging.Abstractions`](../Bsft.Messaging.Abstractions/README.md). What this package supplies is
`CorrelationContextMessageAccessor`, which prefers the ambient id of a message being handled over the
scope's own: without that precedence a chain of integration events — a handler that stages the next one —
would start a fresh correlation on every hop, because the delivery runs in a new service scope whose
`CorrelationContext` mints a new id. Replace the accessor to take the id from somewhere else; there is no
seam of this package's own to replace.

## Documentation

Topic page: [docs/messaging.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/messaging.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
