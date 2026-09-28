using Bsft.Data.MultiTenancy;

namespace Bsft.Caching.Data.Tests.Fakes;

/// <summary>A tenant context answering with whatever the test put into it.</summary>
public sealed class FakeTenantContext : ITenantContext
{
    private string? _tenantId;

    public FakeTenantContext(string? tenantId)
    {
        _tenantId = tenantId;
        IsResolved = tenantId is not null;
    }

    public FakeTenantContext(string? tenantId, bool isResolved)
    {
        _tenantId = tenantId;
        IsResolved = isResolved;
    }

    /// <summary>Gets how often the tenant identifier was read, so memoization is observable.</summary>
    public int Reads { get; private set; }

    public string? TenantId
    {
        get
        {
            Reads++;
            return _tenantId;
        }
    }

    public bool IsResolved { get; private set; }

    /// <summary>Resolves the tenant mid-scope, the way a late resolver would.</summary>
    public void Resolve(string tenantId)
    {
        _tenantId = tenantId;
        IsResolved = true;
    }
}
