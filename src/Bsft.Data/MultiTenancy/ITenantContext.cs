namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Provides access to the current tenant context.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// Gets the resolved tenant identifier, or null if not resolved.
    /// </summary>
    string? TenantId { get; }

    /// <summary>
    /// Gets a value indicating whether the tenant has been resolved.
    /// </summary>
    bool IsResolved { get; }
}
