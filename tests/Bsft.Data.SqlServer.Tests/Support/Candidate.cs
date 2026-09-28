using Bsft.Data.Search;
using Bsft.Data.MultiTenancy;

namespace Bsft.Data.SqlServer.Tests.Support;

/// <summary>The entity the search assertions run against.</summary>
public sealed class Candidate : ITenantEntity, IOrganizationScopedEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public string? TenantId { get; set; }

    public Guid OrganizationUnitId { get; set; }
}
