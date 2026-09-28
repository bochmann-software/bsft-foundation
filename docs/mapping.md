# Bsft.Mapping

Convention-based object mapping with explicit profiles, plus expression-tree
projection so a DTO mapping can run **inside** the database query rather than
after it.

## Contract

```csharp
public interface IMapper
{
    TDest Map<TDest>(object source);
    TDest Map<TSource, TDest>(TSource source);
    TDest Map<TSource, TDest>(TSource source, TDest existing);
    Expression<Func<TSource, TDest>> GetProjection<TSource, TDest>();
}
```

Everything is synchronous; there is no `CancellationToken` anywhere on this
interface. The three-argument `Map` maps **into** an existing instance, which is
what you want when updating a tracked entity rather than replacing it.

## Profiles

A profile derives from `MappingProfileBase` and declares its type pairs:

```csharp
public class ProductMappingProfile : MappingProfileBase
{
    public override void Configure(ProfileBuilder builder)
    {
        builder.CreateMap<Product, ProductDto>();
    }
}
```

Properties are matched by name. When they do not line up, configure the member
explicitly, or exclude it:

```csharp
public override void Configure(ProfileBuilder builder)
{
    builder.CreateMap<Order, OrderDto>()
        .ForMember(d => d.CustomerName, o => o.MapFrom(s => s.Customer.Name))
        .Ignore(d => d.InternalNotes);
}
```

`ForMember` takes the destination member and an options callback whose only
method is `MapFrom`. `Ignore` removes a destination member from the map, which
is how you stop the validator complaining about something intentionally unset.

Each type pair is registered once. A profile refuses to declare the same pair
twice, and so does the mapper across profiles: two profiles declaring one pair
used to leave the winner to the order the assembly scan returned them in.

## What the mapper writes

Only a property with a public setter is assigned. A property counts as public as
soon as its getter does, so `{ get; private set; }` would otherwise be writable
through reflection, and mapping a DTO onto a tracked entity would overwrite
exactly the members the entity made unsettable — its identifier, its owner, its
tenant. A member the destination computes or keeps to itself is skipped by
`Map`, left out of the projection, and not something the validator demands a
configuration for.

Properties without a setter are still matched by name, because an immutable
destination takes them through its constructor.

## Setup

```csharp
services.AddBsftMapping(mapping => mapping
    .AddProfilesFromAssembly(typeof(ProductMappingProfile).Assembly));
```

The configure delegate is **required**. `MappingConfiguration` has exactly one
public method, `AddProfilesFromAssembly(Assembly)`, which may be called several
times to compose profiles from several modules. Naming one assembly twice scans
it once.

The registration adds `IMapper` and `IMappingValidationService` as singletons and
a `MappingStartupValidator` as an `IStartupValidator`. Validation reads the model
this call built: a host that registers its own `IMapper` afterwards replaces the
execution, and the discovered profiles are still what its configuration has to be
complete against.

The package's types live in namespaces by role: the contracts `IMapper` and
`IMappingValidationService` in `Bsft.Mapping.Abstractions`, their implementations
in `Bsft.Mapping.Services`, `MappingValidationException` in
`Bsft.Mapping.Exceptions`, and the profile-authoring API (`MappingProfileBase`,
`ProfileBuilder`, `MapBuilder<,>`, `MemberMapOptions<,>`, `MappingConfiguration`,
`ProjectTo`) in `Bsft.Mapping` itself, which is where an application writing a
profile already is.

There is no `IConfiguration` overload and no options type. `MappingConfiguration`
and `MemberMapOptions<,>` are builder-configuration values that live for the
duration of a call: a mapping is code, and an assembly to scan is not a value a
configuration provider can supply.

## Startup validation

Mappings are verified at startup, not on first use. A destination member that is
neither matched by convention, nor configured with `MapFrom`, nor `Ignore`d is a
startup failure rather than a silently null field discovered in production.
`MappingValidationException` carries the list of problems in its `Errors`
property.

This only fires if the host runs the startup validators — see
`AddBsftStartupValidation()` in [hosting.md](hosting.md), which
`AddBsftDefaults()` calls for you.

## Projection

`GetProjection<TSource, TDest>()` returns an expression built from member-init
nodes, which EF Core can translate. The `ProjectTo` extension applies it:

```csharp
using Bsft.Mapping;
using Bsft.Mapping;

var dtos = await db.Products
    .Where(p => p.IsActive)
    .ProjectTo<ProductDto>(mapper)
    .ToListAsync(ct);
```

Note the signature — the mapper is an argument, not an ambient dependency:

```csharp
public static IQueryable<TDest> ProjectTo<TDest>(this IQueryable source, IMapper mapper)
```

The difference from `Map` matters. `ProjectTo` produces a `SELECT` of only the
columns the DTO needs; mapping after materialisation pulls whole entities into
memory first. For a list endpoint that is usually the difference between one
narrow query and a table's worth of rows.

## Example

```csharp
public sealed class GetProductsHandler(IInventoryDbContext db, IMapper mapper)
    : IQueryHandler<GetProducts, IReadOnlyList<ProductDto>>
{
    public async Task<IReadOnlyList<ProductDto>> HandleAsync(GetProducts query, CancellationToken ct = default) =>
        await db.Products
            .AsNoTracking()
            .ProjectTo<ProductDto>(mapper)
            .ToListAsync(ct);
}
```

## See also

- [data.md](data.md) — filtering, sorting and pagination over the same queryable.
- [hosting.md](hosting.md) — where startup validation is executed.
