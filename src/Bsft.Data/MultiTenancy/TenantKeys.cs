namespace Bsft.Data.MultiTenancy;

/// <summary>
/// The values the tenant-keyed mapping of the framework's own tables shares: the annotation that
/// marks it, the column length and the tenant a row is written for.
/// </summary>
/// <remarks>
/// A table of a tenant database that follows <see cref="TenantKeyModelBuilderExtensions.UseTenantKeys"/>
/// carries a non-null <c>TenantId</c>, and its key is either a Guid or <c>(TenantId, Id)</c> with an
/// <c>Id</c> the client creates; none has an identity column. See <c>docs/data.md</c>, "Tenant keys".
/// </remarks>
public static class TenantKeys
{
    /// <summary>
    /// The model annotation <see cref="TenantKeyModelBuilderExtensions.UseTenantKeys"/> sets, and the
    /// entity-type annotation every entity type mapped tenant-keyed carries.
    /// </summary>
    public const string TenantKeyedAnnotation = "Bsft:TenantKeyed";

    /// <summary>
    /// The entity-type annotation every framework mapping that follows the switch carries, keyed or
    /// not. It is what lets a late <see cref="TenantKeyModelBuilderExtensions.UseTenantKeys"/> fail
    /// instead of being ignored.
    /// </summary>
    public const string TenantKeyableAnnotation = "Bsft:TenantKeyable";

    /// <summary>The column name of the tenant on every tenant-keyed table.</summary>
    public const string TenantIdColumnName = "TenantId";

    /// <summary>
    /// The length of the <c>TenantId</c> column on a tenant-keyed table, the same as the workflow
    /// tables have always used. A bounded length is what lets SQL Server put the column in a key.
    /// </summary>
    public const int TenantIdMaxLength = 128;
}
