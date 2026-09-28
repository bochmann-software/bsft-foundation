using Bsft.Http;
using Bsft.Mapping;
using Bsft.Sample.Inventory.Core;

namespace Bsft.Sample.Inventory;

/// <summary>
/// Convention-based mapping from <see cref="Product"/> to <see cref="ProductDto"/>.
/// Every property but the entity tag matches by name; the tag is derived from the version here.
/// </summary>
public class ProductMappingProfile : MappingProfileBase
{
    /// <inheritdoc />
    public override void Configure(ProfileBuilder builder)
    {
        // The one place a product's entity tag is derived. Every DTO the module hands out is built
        // through this map — the list projection, the read by id and the body of an update — so the
        // tag on a list row, the tag on a single read and the ETag header of both cannot drift
        // apart: they are one call on one version. The derivation is the shared Bsft.Http primitive
        // rather than a spelling this module owns, so it also agrees with the value a conditional
        // write is checked against.
        //
        // It runs after materialization, which is where a SHA-256 has to run: this map is applied to
        // entities the query executor has already read. A resource that projects to its DTO inside
        // the database instead — ProjectTo over IQueryable — derives the tag in a Select after the
        // rows come back, because no provider can translate the digest.
        builder.CreateMap<Product, ProductDto>()
            .ForMember(dto => dto.ETag, opts => opts.MapFrom(product => product.Version.ToVersionEntityTag()));
    }
}
