
namespace Bsft.Data.Search;

/// <summary>
/// Supplies the tenant and organisational boundary applied to every search.
/// </summary>
/// <remarks>
/// The seam exists so that <c>Bsft.Data</c> does not have to reference <c>Bsft.Organization</c>.
/// An application that uses organisational scoping implements this against
/// <c>IOrganizationAccessReader</c>; the same pattern as the audit package's correlation seam.
/// </remarks>
public interface ISearchScopeProvider
{
    /// <summary>Resolves the boundary for the current caller.</summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The scope to apply.</returns>
    Task<SearchScopeState> GetScopeAsync(CancellationToken cancellationToken = default);
}
