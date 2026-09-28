
namespace Bsft.Data.Search;

/// <summary>
/// Marks an entity that belongs to an organisational unit, so a search can be restricted to the
/// units the caller may see.
/// </summary>
public interface IOrganizationScopedEntity
{
    /// <summary>Gets the organisational unit owning this row.</summary>
    Guid OrganizationUnitId { get; }
}
