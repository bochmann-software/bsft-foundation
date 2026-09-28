# Service-collection entry point not named after its feature

## hits

### a bare ServiceCollectionExtensions

```csharp
namespace Bsft.Foo;

public static class ServiceCollectionExtensions
{
    public static int AddBsftFoo() => 0;
}
```

## misses

### a feature-named entry point

```csharp
namespace Bsft.Baz;

public static class BazServiceCollectionExtensions
{
    public static int AddBsftBaz() => 0;
}
```

### a non-static type that merely carries the name

```csharp
namespace Bsft.Qux;

public sealed class ServiceCollectionExtensions
{
    public int Value { get; set; }
}
```
