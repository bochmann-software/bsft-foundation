using Bsft.Data.MultiTenancy;

namespace Bsft.Localization.Tests.Fakes;

/// <summary>
/// A tenant context a test can switch between requests, standing in for whatever a host
/// resolves the tenant from.
/// </summary>
internal sealed class MutableTenantContext : ITenantContext
{
    /// <summary>Initializes a new instance of the <see cref="MutableTenantContext"/> class.</summary>
    /// <param name="tenantId">The tenant to start in, or <see langword="null"/> for none.</param>
    public MutableTenantContext(string? tenantId = null) => TenantId = tenantId;

    /// <inheritdoc />
    public string? TenantId { get; private set; }

    /// <inheritdoc />
    public bool IsResolved => TenantId is not null;

    /// <summary>Moves the context to another tenant, as a second request would.</summary>
    /// <param name="tenantId">The tenant the next request is served for.</param>
    public void SwitchTo(string tenantId) => TenantId = tenantId;
}
