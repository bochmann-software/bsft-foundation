using Bsft.Data.MultiTenancy;

namespace Bsft.Data.Tests.MultiTenancy;

/// <summary>Entity for tenant key tests with a long Id property.</summary>
internal sealed class TenantKeyedTestEntity : TenantEntity
{
    /// <summary>The time-ordered id for tenant-keyed tables.</summary>
    public long Id { get; set; }

    /// <summary>Entity name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Entity age.</summary>
    public int Age { get; set; }
}
