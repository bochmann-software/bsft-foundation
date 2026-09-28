using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Resolves the tenant identifier from a configured HTTP request header.
/// </summary>
public sealed class HeaderTenantResolver : ITenantResolver
{
    // Upper bound on an accepted tenant identifier. The header is attacker-controlled, so an
    // unbounded value is rejected rather than flowing into the tenant context and query filters.
    private const int MaxTenantIdLength = 128;

    private readonly string _headerName;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeaderTenantResolver"/> class.
    /// </summary>
    /// <param name="options">The data options carrying the header name to read.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <remarks>
    /// The name is read once, at construction: the resolver is a singleton and the header a
    /// deployment reads its tenant from is not something that changes while it runs.
    /// </remarks>
    public HeaderTenantResolver(IOptions<BsftDataOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _headerName = options.Value.MultiTenancy.HeaderName;
    }

    /// <inheritdoc />
    public Task<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        var value = context.Request.Headers[_headerName].FirstOrDefault();

        // Treat whitespace-only or oversized header values as "no tenant" instead of trusting them.
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxTenantIdLength)
            return Task.FromResult<string?>(null);

        return Task.FromResult<string?>(value.Trim());
    }
}
