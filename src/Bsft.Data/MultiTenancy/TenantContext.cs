using Bsft.Core.Tenancy;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Scoped implementation of <see cref="ITenantContext"/> that caches the resolved tenant value.
/// </summary>
/// <remarks>
/// <para>
/// A context nobody set reports the tenant of the background pass it was created in
/// (<see cref="BackgroundTenant.Current"/>), so that a scope a store or a dispatcher opens while a pass works
/// for one tenant sees that tenant without being told. Outside such a pass, and once
/// <see cref="SetTenantId"/> ran, it reports what was set, exactly as before.
/// </para>
/// <para>
/// The pass's tenant is read once, when the context is created, and kept: a context created outside a pass
/// stays without a tenant when it is later used inside one, and a context created in one tenant's pass keeps
/// that tenant when the flow moves on. A scope therefore never changes its tenant halfway, which matters where
/// the tenant also chose the scope's database connection.
/// </para>
/// </remarks>
public sealed class TenantContext : ITenantContext
{
    private readonly string? _backgroundTenant = BackgroundTenant.Current;
    private string? _tenantId;
    private bool _isResolved;

    /// <inheritdoc />
    public string? TenantId => _isResolved ? _tenantId : _backgroundTenant;

    /// <inheritdoc />
    public bool IsResolved => _isResolved || _backgroundTenant is not null;

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
