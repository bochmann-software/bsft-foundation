using Bsft.Data.MultiTenancy;

namespace Bsft.Idempotency.Tests.Fakes;

/// <summary>A tenant context that answers exactly what a test handed it.</summary>
internal sealed class FixedTenantContext : ITenantContext
{
    public FixedTenantContext(string? tenantId, bool isResolved)
    {
        TenantId = tenantId;
        IsResolved = isResolved;
    }

    public string? TenantId { get; }

    public bool IsResolved { get; }
}
