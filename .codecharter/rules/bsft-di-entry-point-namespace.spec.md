# DI entry point outside the package's DependencyInjection namespace

## hits

### service-collection entry point in the package root

```csharp
namespace Bsft.Bar;

public static class BarServiceCollectionExtensions
{
    public static int AddBsftBar() => 0;
}
```

### endpoint entry point in a feature namespace

```csharp
namespace Bsft.Bar.Management;

public static class BarEndpointRouteBuilderExtensions
{
    public static int MapBsftBar() => 0;
}
```

### middleware entry point in the package root

```csharp
namespace Bsft.Bar;

public static class BarApplicationBuilderExtensions
{
    public static int UseBsftBar() => 0;
}
```

## misses

### every entry point in DependencyInjection

```csharp
namespace Bsft.Baz.DependencyInjection;

public static class BazServiceCollectionExtensions
{
    public static int AddBsftBaz() => 0;
}

public static class BazEndpointRouteBuilderExtensions
{
    public static int MapBsftBaz() => 0;
}

public static class BazApplicationBuilderExtensions
{
    public static int UseBsftBaz() => 0;
}
```

### several sub-feature entry points in one DependencyInjection namespace

```csharp
namespace Bsft.Qux.DependencyInjection;

public static class CorsServiceCollectionExtensions
{
    public static int AddBsftCors() => 0;
}

public static class SwaggerServiceCollectionExtensions
{
    public static int AddBsftSwagger() => 0;
}
```

### a non-static class with the suffix

```csharp
namespace Bsft.Grault;

public sealed class GraultServiceCollectionExtensions
{
    public int Count { get; set; }
}
```
