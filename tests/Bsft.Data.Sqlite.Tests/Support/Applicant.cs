using Bsft.Data.MultiTenancy;

namespace Bsft.Data.Sqlite.Tests.Support;

/// <summary>A searchable entity whose key is the integer an FTS5 rowid can carry.</summary>
public class Applicant : ITenantEntity
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public string? TenantId { get; set; }
}
