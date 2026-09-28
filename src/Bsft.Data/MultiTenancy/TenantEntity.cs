namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Base class for tenant-scoped entities, carrying the tenant identifier the save-changes
/// interceptor stamps and checks.
/// </summary>
/// <remarks>
/// Designed for inheritance and therefore deliberately not sealed: an application derives its own
/// entities from it. Implementing <see cref="ITenantEntity"/> directly does the same job for an
/// entity that already has a base class of its own.
/// </remarks>
public class TenantEntity : ITenantEntity
{
    /// <inheritdoc />
    public string? TenantId { get; set; }
}
