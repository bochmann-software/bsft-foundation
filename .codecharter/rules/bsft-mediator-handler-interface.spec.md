# Mediator handler without its handler interface

The rule reads `ImplementedInterfaces` on the type and, one level up, on its base
class - the base-class hop is what keeps a handler that inherits its contract from
a shared base out of the findings, which is how the organization command family is
built.

Two limits are worth knowing before extending this. The interface list is empty for
a type whose contract comes from a project the analysed one does not reference
directly, so a handler in such a project would be flagged wrongly; the samples were
given a direct reference to `Bsft.Mediator.Abstractions` for exactly this reason.
And the base-class hop is one level, not the whole chain, because the model offers
no transitive interface set.

## hits

### a handler that implements nothing

```csharp
public sealed class CreateOrderCommandHandler
{
    public int Handle() => 0;
}
```

### a handler that implements the wrong contract

```csharp
public interface IDisposableThing { }
public sealed class ArchiveOrderCommandHandler : IDisposableThing { }
```

### a query handler named as one but wired as a service

```csharp
public interface IOrderReader { }
public sealed class GetOrderQueryHandler : IOrderReader { }
```

## misses

### a handler implementing the command contract directly

```csharp
public interface ICommand<TResult> { }
public interface ICommandHandler<TCommand, TResult> where TCommand : ICommand<TResult> { }
public sealed class CreateOrderCommand : ICommand<int> { }
public sealed class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand, int>
{
    public int Handle() => 0;
}
```

### a handler inheriting the contract from a shared base

```csharp
public interface ICommand<TResult> { }
public interface ICommandHandler<TCommand, TResult> where TCommand : ICommand<TResult> { }
public sealed class RenameRoleCommand : ICommand<int> { }
public abstract class RoleMutationHandlerBase<TCommand> : ICommandHandler<TCommand, int>
    where TCommand : class, ICommand<int>
{
    public int Handle() => 0;
}
public sealed class RenameRoleCommandHandler : RoleMutationHandlerBase<RenameRoleCommand> { }
```

### an abstract base is not itself a handler to register

```csharp
public interface ICommand<TResult> { }
public abstract class CommandHandlerBase<TCommand> where TCommand : ICommand<int> { }
```

### a notification handler with its contract

```csharp
public interface INotification { }
public interface INotificationHandler<TNotification> where TNotification : INotification { }
public sealed class OrderPlacedNotification : INotification { }
public sealed class OrderPlacedNotificationHandler : INotificationHandler<OrderPlacedNotification>
{
    public int Handle() => 0;
}
```
