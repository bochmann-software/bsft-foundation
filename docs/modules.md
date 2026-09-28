# Bsft.Modules

A module is an independently developed slice of an application that registers its
own services and has its own startup and shutdown. The loader resolves the
declared dependencies, sorts them topologically, and starts them in an order that
respects the graph.

## Contract

The contracts (`IModule`, `IModuleCatalog`, `IModuleHost`, `ModuleDescriptor`,
`ModuleInfo`) live in `Bsft.Modules.Abstractions`, their implementations
(`ModuleCatalog`, `ModuleHost`, `ModuleLoader`) in `Bsft.Modules.Services`, and
`ModuleDependencyException` in `Bsft.Modules.Exceptions`; the wiring
(`AddBsftModules`, `UseBsftModules`, `ModulesConfiguration`) stays in
`Bsft.Modules`. A module implementation therefore imports
`Bsft.Modules.Abstractions`.

```csharp
public interface IModule
{
    string Name { get; }
    string Version { get; }
    string[] DependsOn => [];
    void ConfigureServices(IServiceCollection services, IConfiguration config);
    Task OnStartAsync(IServiceProvider services, CancellationToken ct = default);
    Task OnStopAsync(CancellationToken ct = default);
}
```

`DependsOn` is a default interface member, so a module without dependencies
simply omits it. The names in it are module `Name` values, not assembly names.

## Setup

```csharp
services.AddBsftModules(builder.Configuration, modules => modules
    .Add<OrdersModule>()
    .Add<InventoryModule>());

// ...

app.UseBsftModules();
```

`AddBsftModules` takes the `IConfiguration` positionally, because it hands it to
each module's `ConfigureServices`. Everything the modules register happens
*during* this call — it is not deferred.

`UseBsftModules()` starts the module host, and additionally maps
`GET /api/modules` when the app builder is also an `IEndpointRouteBuilder` (which
it is in a normal ASP.NET Core host). The endpoint requires an authenticated
caller: it answers with the inventory of an installation and the version of every
part of it.

## Sources

`ModulesConfiguration` offers three ways to populate the catalog:

| Method | Meaning |
| --- | --- |
| `Add<TModule>()` | A module compiled into the application. |
| `FromConfigurationFile(path)` | A JSON manifest listing modules and their assemblies. |
| `FromDirectory(path)` | Scan a directory of assemblies for `IModule` implementations. |

The JSON shape is `{ "modules": [ { "name", "assembly", "enabled", "dependsOn" } ] }`,
and it is the contract rather than the classes that read it, which are internal.
where `enabled` defaults to `true`. The manifest carries no version, so the
catalog records `"0.0.0"` and the endpoint reports what the loaded module says
about itself instead.

`dependsOn` is an addition to what a module declares in code, not a replacement:
a deployment may know of an ordering constraint the module's author could not,
and a module's own declarations are not a deployment's to drop.

The directory scan skips a file that is not a managed assembly at all — a native
library sitting in a plugin folder is not an error. An assembly whose types
cannot be read is a different matter and throws `ModuleDependencyException`
naming the file, as does an assembly a manifest names that cannot be loaded.

## Ordering rules

The loader is strict, and each rule exists to turn a silent misconfiguration into
a startup failure:

- Only modules with `Enabled == true` are loaded.
- A module type must have a parameterless constructor; it is instantiated before
  the container exists, so it cannot take dependencies.
- Names are compared **case-insensitively**. A collision throws
  `ModuleDependencyException`.
- A dependency that is not in the catalog throws `ModuleDependencyException`.
- The sort is Kahn's algorithm, dependencies first. A cycle throws
  `ModuleDependencyException` naming the members of the cycle.

`ConfigureServices` runs in sorted order, `OnStartAsync` in the same order, and
`OnStopAsync` in **reverse** — so a module never shuts down while something that
depends on it is still running.

`GET /api/modules` projects from the catalog rather than the loaded list, so
disabled modules are visible as disabled instead of vanishing. The projection is
built once, when the host is: the set of modules cannot change afterwards.

## Localization convention

A module's display name and description are resolved by convention from the
localization keys `modules.{Name}.displayName` and `modules.{Name}.description`.
Seeding them is the module's own job — see [localization.md](localization.md).

## Example

```csharp
using Bsft.Localization.DependencyInjection;
using Bsft.Modules;

public class InventoryModule : IModule
{
    public string Name => "Inventory";
    public string Version => "1.0.0";
    public string[] DependsOn => ["Orders"];

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<StockReservationHandler>();

        services.AddBsftTranslations(seed => seed
            .ForLocale("en", en => en
                .Add("modules.Inventory.displayName", "Inventory")
                .Add("modules.Inventory.description", "Stock and inventory management")));
    }

    public Task OnStartAsync(IServiceProvider services, CancellationToken ct = default)
    {
        services.GetRequiredService<StockReservationHandler>().Subscribe();
        return Task.CompletedTask;
    }

    public Task OnStopAsync(CancellationToken ct = default) => Task.CompletedTask;
}
```

`OnStartAsync` is the right place to take out event-aggregator subscriptions:
the container is built, and the subscription lives as long as the module does.

## See also

- [event-aggregator.md](event-aggregator.md) — the usual way modules talk without referencing each other.
- the getting-started guide — where module registration sits in the host.
