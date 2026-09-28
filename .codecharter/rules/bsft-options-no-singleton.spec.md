# Options instance registered as a service

## hits

### registering an options type as a singleton by type argument

```csharp
using Microsoft.Extensions.DependencyInjection;

public sealed class FooOptions { public int Size { get; set; } }

public static class FooRegistration
{
    public static IServiceCollection Add(IServiceCollection services)
        => services.AddSingleton<FooOptions>();
}
```

### registering a built options instance

```csharp
using Microsoft.Extensions.DependencyInjection;

public sealed class BarOptions { public int Size { get; set; } }

public static class BarRegistration
{
    public static IServiceCollection Add(IServiceCollection services)
    {
        var options = new BarOptions();
        return services.AddSingleton(options);
    }
}
```

### TryAddSingleton with a named options instance

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public sealed class BazOptions { public int Size { get; set; } }

public static class BazRegistration
{
    public static void Add(IServiceCollection services)
    {
        var bazOptions = new BazOptions();
        services.TryAddSingleton(bazOptions);
    }
}
```

## misses

### the options pattern

```csharp
using Microsoft.Extensions.DependencyInjection;

public sealed class QuxOptions { public int Size { get; set; } }

public static class QuxRegistration
{
    public static IServiceCollection Add(IServiceCollection services)
    {
        services.AddOptions<QuxOptions>().Configure(o => o.Size = 1).ValidateOnStart();
        return services;
    }
}
```

### a service whose name merely contains Options

```csharp
using Microsoft.Extensions.DependencyInjection;

public interface IProfileSource { }
public sealed class OptionsProfileSource : IProfileSource { }

public static class QuuxRegistration
{
    public static IServiceCollection Add(IServiceCollection services)
        => services.AddSingleton<IProfileSource, OptionsProfileSource>();
}
```

### registering a validator, which is the prescribed pattern

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

public sealed class CorgeOptions { public int Size { get; set; } }

public sealed class CorgeOptionsValidator : IValidateOptions<CorgeOptions>
{
    public ValidateOptionsResult Validate(string? name, CorgeOptions options) => ValidateOptionsResult.Success;
}

public static class CorgeRegistration
{
    public static IServiceCollection Add(IServiceCollection services)
        => services.AddSingleton<IValidateOptions<CorgeOptions>, CorgeOptionsValidator>();
}
```

### registering a snapshot built from options, which is not the options object

```csharp
using Microsoft.Extensions.DependencyInjection;

public sealed class GraultOptions { public int Size { get; set; } }
public sealed class GraultSnapshot { public GraultSnapshot(GraultOptions options) { } }

public static class GraultRegistration
{
    public static IServiceCollection Add(IServiceCollection services)
    {
        var snapshot = new GraultSnapshot(new GraultOptions());
        return services.AddSingleton(snapshot);
    }
}
```
