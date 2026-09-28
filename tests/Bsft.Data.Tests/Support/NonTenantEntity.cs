namespace Bsft.Data.Tests.Support;

/// <summary>
/// A plain entity that does NOT implement ITenantEntity, used to exercise the branch of
/// BsftDbContextOptionsExtensions' tenant filters, which skip an untenanted entity type.
/// </summary>
public sealed class NonTenantEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
