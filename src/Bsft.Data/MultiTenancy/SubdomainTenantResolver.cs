using Microsoft.AspNetCore.Http;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Resolves the tenant identifier from the first subdomain of the request host.
/// </summary>
public sealed class SubdomainTenantResolver : ITenantResolver
{
    // A host only carries a tenant subdomain once it has at least three labels
    // (tenant.example.com); "example.com" has none to take.
    private const int MinimumHostLabelsWithSubdomain = 3;

    /// <inheritdoc />
    public Task<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        var host = context.Request.Host.Host;
        if (string.IsNullOrEmpty(host))
            return Task.FromResult<string?>(null);

        var parts = host.Split('.');
        if (parts.Length < MinimumHostLabelsWithSubdomain)
            return Task.FromResult<string?>(null);

        return Task.FromResult<string?>(parts[0]);
    }
}
