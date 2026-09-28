# Bsft.Mapping

Convention-based object and expression mapping: profiles declare type pairs, the mapper maps
instances, and the same profile produces a LINQ projection EF Core can translate into SQL.

## Install

```bash
dotnet add package Bsft.Mapping
```

Requires `Bsft.Core` (for `IStartupValidator`); no third-party mapping library.

## Register

```csharp
services.AddBsftMapping(mapping => mapping.AddProfilesFromAssembly(typeof(Program).Assembly));
```

Nothing else is required. There is no `IConfiguration` overload: what this package needs is a set of
assemblies to scan and the profiles inside them, and neither is a value a configuration provider can
supply. Every non-abstract `MappingProfileBase` in the scanned assemblies is instantiated by
reflection and folded into one mapper, registered as a singleton alongside `IMappingValidationService`
and an `IStartupValidator` that fails the application at startup if a mapping is incomplete.

## Configure

None. The package binds no configuration section and exposes no options type.
`MappingConfiguration` and `MemberMapOptions<TSource, TMember>` are builder-configuration values that
live for the duration of a call, not options in the sense of the conventions, so neither carries a
`SectionName`.

| Option | Default | Effect |
| --- | --- | --- |
| — | — | — |

Validation: `MappingStartupValidator` runs `IMappingValidationService` at startup and reports every
destination member no source member and no explicit rule accounts for. A mapping that cannot be
completed throws `MappingValidationException` naming the type pair and the members. Only members the
mapper may assign are required to be accounted for.

What the mapper writes: a destination property is assigned only when it has a public setter. A
property is public as soon as its getter is, so a `{ get; private set; }` member would otherwise be
writable through reflection and mapping a DTO onto a tracked entity would overwrite exactly what the
entity made unsettable. Members without a public setter are still matched by name, because an
immutable destination takes them through its constructor.

Each type pair is registered once, across profiles as well as within one, and naming an assembly
twice scans it once.

## Extend

`MappingProfileBase` is the extension point and the one type here designed for inheritance: derive a
profile, override `Configure`, and describe type pairs with `ProfileBuilder` and
`MapBuilder<TSource, TDest>` (`MapFrom`, `Ignore`). It needs a public parameterless constructor,
because it is instantiated by reflection. `IMapper` and `IMappingValidationService` are registered as
plain singletons, so a host that registers its own implementation before calling `AddBsftMapping`
gets both registrations and the last one wins.

## Documentation

Topic page: [docs/mapping.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/mapping.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
