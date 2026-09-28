using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Resolves the tenant identifier from a configured user claim.
/// </summary>
public sealed class ClaimTenantResolver : ITenantResolver
{
    private readonly string _claimType;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClaimTenantResolver"/> class.
    /// </summary>
    /// <param name="options">The data options carrying the claim type to read.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <remarks>
    /// The claim type is read once, at construction: the resolver is a singleton and the claim a
    /// deployment reads its tenant from is not something that changes while it runs.
    /// </remarks>
    public ClaimTenantResolver(IOptions<BsftDataOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _claimType = options.Value.MultiTenancy.ClaimType;
    }

    /// <inheritdoc />
    public Task<string?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        var value = context.User?.FindFirst(_claimType)?.Value;
        return Task.FromResult(value);
    }
}
