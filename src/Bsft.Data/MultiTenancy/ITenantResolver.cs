using Microsoft.AspNetCore.Http;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Defines a strategy for resolving the current tenant from the HTTP request.
/// </summary>
public interface ITenantResolver
{
    /// <summary>
    /// Resolves the tenant identifier from the given HTTP context.
    /// </summary>
    /// <param name="context">The HTTP context of the current request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The resolved tenant identifier, or null if not resolvable.</returns>
    Task<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default);
}
