namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Scoped implementation of <see cref="ITenantContext"/> that caches the resolved tenant value.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private string? _tenantId;
    private bool _isResolved;

    /// <inheritdoc />
    public string? TenantId => _tenantId;

    /// <inheritdoc />
    public bool IsResolved => _isResolved;

    /// <summary>
    /// Sets the tenant identifier and marks the context as resolved.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    public void SetTenantId(string? tenantId)
    {
        _tenantId = tenantId;
        _isResolved = true;
    }
}
