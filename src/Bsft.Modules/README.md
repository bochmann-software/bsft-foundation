# Bsft.Modules

The module system: a catalog of `IModule` implementations discovered from types, a directory or a
JSON manifest, loaded and started in dependency order, with a discovery endpoint that reports what an
application is actually running.

## Install

```bash
dotnet add package Bsft.Modules
```

No further Bsft packages. It takes a framework reference on `Microsoft.AspNetCore.App`, because
`UseBsftModules` maps the `GET /api/modules` endpoint. That endpoint requires an authenticated
caller, so the host has to have authentication and authorization in the pipeline before it.

## Register

```csharp
services.AddBsftModules(configuration, modules =>
{
    modules.Add<InventoryModule>();
    modules.FromConfigurationFile("modules.json");
    modules.FromDirectory("./plugins");
});

app.UseBsftModules();
```

`AddBsftModules` builds the catalog, resolves the dependency order, loads the assemblies and calls
`IModule.ConfigureServices` on each module — so it runs while the container is still being built and
a module's own registrations land in it. `UseBsftModules` then calls `IModule.OnStartAsync` in the
same order and maps the discovery endpoint. The `IConfiguration` it takes is handed to each module
rather than bound into settings of this package.

## Configure

None. The package binds no configuration section and exposes no options type. `ModulesConfiguration`
is a builder-configuration value that lives for the duration of the `AddBsftModules` call, listing
where modules come from; it is not an options type in the sense of the conventions and carries no
`SectionName`.

| Option | Default | Effect |
| --- | --- | --- |
| — | — | — |

Validation: the loader rejects a missing dependency, a dependency cycle and an unloadable assembly
with a `ModuleDependencyException` naming the module. The failure happens during `AddBsftModules`,
so a misconfigured module set never reaches a running host.

## Extend

`IModule` is the extension point: implement it to contribute registrations in `ConfigureServices` and
startup work in `OnStartAsync`, and declare `DependsOn` so the order is derived rather than assumed.
`IModuleCatalog` (what is registered) and `IModuleHost` (what is loaded and its status) are resolvable
for a host that wants to report on the module set itself. The JSON manifest models are internal: the
file format is the contract, not the classes that read it.

## Documentation

Topic page: [docs/modules.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/modules.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
