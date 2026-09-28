
namespace Bsft.Data.Search;

/// <summary>
/// The visibility boundary a search must not cross.
/// </summary>
/// <param name="TenantId">The tenant whose rows may be returned, or <c>null</c> for no tenant restriction.</param>
/// <param name="AllowedOrganizationUnitIds">
/// The organisational units whose rows may be returned, or <c>null</c> for no organisational
/// restriction. An empty list means the caller may see nothing.
/// </param>
public sealed record SearchScopeState(string? TenantId, IReadOnlyList<Guid>? AllowedOrganizationUnitIds)
{
    /// <summary>Gets a scope that restricts nothing. Only appropriate for single-tenant data.</summary>
    public static SearchScopeState Unrestricted { get; } = new(null, null);
}
