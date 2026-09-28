namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Marks an entity as tenant-scoped with a tenant identifier property.
/// </summary>
public interface ITenantEntity
{
    /// <summary>
    /// Gets or sets the tenant identifier that owns this entity.
    /// </summary>
    string? TenantId { get; set; }
}
