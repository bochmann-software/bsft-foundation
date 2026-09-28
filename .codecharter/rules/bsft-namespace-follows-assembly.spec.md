# Namespace does not start with the package's namespace root

The spec engine compiles every case into an assembly named `TestAssembly`, so these cases exercise
the branch for an assembly without the `.Abstractions` suffix. The `.Abstractions` branch is covered
by the solution itself, whose 27 `.Abstractions` assemblies must report 0 hits; the old layout
(`namespace Bsft.Storage.Abstractions;`) matches the first clause of that branch by construction.

## hits

### a namespace outside the assembly name

```csharp
namespace Bsft.Foo;

public sealed class Widget
{
}
```

### a namespace that only shares a prefix with the assembly name

```csharp
namespace TestAssemblyExtras;

public sealed class Widget
{
}
```

### a type in the global namespace

```csharp
public sealed class Widget
{
}
```

## misses

### the assembly name itself

```csharp
namespace TestAssembly;

public sealed class Widget
{
}
```

### a folder namespace below the assembly name

```csharp
namespace TestAssembly.Downloads;

public sealed class Download
{
}
```

### the Program class of top-level statements

```csharp
System.Console.WriteLine("host");

public partial class Program
{
}
```
