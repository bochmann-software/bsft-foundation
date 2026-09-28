# Handler without a handler contract

The base-class half reads `DeclaredBaseTypeName`, which unlike `BaseType` survives a
base the analysis model does not contain - `AuthenticationHandler<TOptions>` and
`AuthorizationHandler<TRequirement>` are external, and the eight handlers deriving
from them are the reason this clause is not `BaseType.Name`.

The interface half has no such escape: `ImplementedInterfaces` holds only interfaces
the model knows, so a class implementing an *external* `I*Handler` is flagged
wrongly. One type in the tree is in that position and is exempted by name in
`.codecharter/config.yml`. The engine gap behind it is reported upstream.

## hits

### a handler that answers to nothing

```csharp
public sealed class OrderHandler
{
    public int Handle() => 0;
}
```

### a handler whose only interface is unrelated

```csharp
public interface IDisposableThing { }
public sealed class RetryHandler : IDisposableThing { }
```

## misses

### a handler implementing its own contract

```csharp
public interface IOrderHandler { }
public sealed class OrderHandler : IOrderHandler { }
```

### a handler deriving from an external handler base

```csharp
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyOptions>
{
    public int Handle() => 0;
}
public class ApiKeyOptions { }
public abstract class AuthenticationHandler<TOptions> { }
```

### a handler inheriting its contract from a base in the model

```csharp
public interface IMessageHandler { }
public abstract class MessageHandlerBase : IMessageHandler { }
public sealed class OrderMessageHandler : MessageHandlerBase { }
```

### an abstract base is not a registered handler

```csharp
public abstract class OrderHandler { }
```
