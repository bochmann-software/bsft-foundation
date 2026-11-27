using Bsft.Core.Tenancy;
using Bsft.Data.MultiTenancy;

namespace Bsft.Data.Tests.Support.Fakes;

/// <summary>Fake tenant context for testing.</summary>
internal sealed class FakeTenantContext : ITenantContext
{
    /// <summary>Initializes a new instance of the <see cref="FakeTenantContext"/> class.</summary>
    /// <param name="tenantId">The tenant id, or <see langword="null"/>.</param>
    public FakeTenantContext(string? tenantId = null)
    {
        TenantId = tenantId;
        IsResolved = !string.IsNullOrEmpty(tenantId);
    }

    /// <inheritdoc />
    public string? TenantId { get; }

    /// <inheritdoc />
    public bool IsResolved { get; }
}
