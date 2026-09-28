using Bsft.Data.MultiTenancy;

namespace Bsft.Data.Sqlite.Tests.Support;

/// <summary>A searchable entity whose key is a GUID, which no FTS5 rowid can carry.</summary>
public class Referral : ITenantEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public string? TenantId { get; set; }
}
