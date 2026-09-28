# Pipeline behavior outside a Bsft.<X>.Mediator package

The rule matches on the name and the namespace, not on the implemented interface:
`TypeModel.Interfaces` and `ImplementsInterface` are not evaluable (see
`docs/package-conventions.md`, "The conventions rules"), and `TypeModel.Namespace`
answers with a `NamespaceModel` that carries no string helpers, so the namespace is
read out of `FullName`. Both misses below are what makes the `.Mediator.` test a
test rather than a tautology.

## hits

### a behavior left in its concern package

```csharp
namespace Bsft.Caching;

public sealed class CachingBehavior<TRequest, TResponse>
{
    public int Handle() => 0;
}
```

### a behavior in a feature folder of a concern package

```csharp
namespace Bsft.Auth.Authorization;

public sealed class AuthorizationBehavior<TRequest, TResponse>
{
    public int Handle() => 0;
}
```

## misses

### a behavior in the concern's mediator satellite

```csharp
namespace Bsft.Caching.Mediator;

public sealed class CachingBehavior<TRequest, TResponse>
{
    public int Handle() => 0;
}
```

### a behavior the mediator itself ships

```csharp
namespace Bsft.Mediator.Services;

public sealed class LoggingBehavior<TRequest, TResponse>
{
    public int Handle() => 0;
}
```

### a type whose name merely starts with Behavior

```csharp
namespace Bsft.Workflow;

public sealed class BehaviorDescriptor
{
    public string Name { get; set; } = string.Empty;
}
```
