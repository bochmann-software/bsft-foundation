namespace Bsft.Core.Tenancy;

/// <summary>
/// Creates a dependency-injection scope for one tenant, with the tenant set before anything in the scope is
/// resolved.
/// </summary>
/// <remarks>
/// <para>
/// The seam a background service uses when it opens the scope a unit of work runs in — a job, a leased work
/// item, a swept message — while it works for a tenant. <c>Bsft.Data</c> registers an implementation that sets
/// its tenant context; a host replaces it when a tenant's scope needs more than the tenant, such as the system
/// caller identity the work runs under or the tenant's database binding.
/// </para>
/// <para>
/// A background service that finds no implementation opens a plain scope, which still sees the tenant through
/// <see cref="BackgroundTenant.Current"/> when the pass entered one.
/// </para>
/// </remarks>
public interface ITenantScopeFactory
{
    /// <summary>Creates a scope whose services see <paramref name="tenantId"/> as their tenant.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <returns>The scope, which the caller disposes.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="tenantId"/> is null, empty or white space.</exception>
    ITenantScope CreateScope(string tenantId);

    /// <summary>
    /// Creates a scope whose services see <paramref name="tenantId"/> as their tenant, for a host whose tenant
    /// scope needs I/O to bind, such as resolving the tenant's directory entry or leasing its data source.
    /// </summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="cancellationToken">A token to observe while the scope is being bound.</param>
    /// <returns>The scope, which the caller disposes.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="tenantId"/> is null, empty or white space.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    /// <remarks>
    /// <para>
    /// The framework's own callers on a hot or background path use this member, so a host that can only bind
    /// a scope asynchronously implements it and is never called synchronously by them. The default calls
    /// <see cref="CreateScope"/> once the token is checked, so an implementation written before this member
    /// existed keeps working unchanged, in source and in binary.
    /// </para>
    /// <para>
    /// An implementation of this member still implements <see cref="CreateScope"/>: a caller outside the
    /// framework may need a scope where it cannot await.
    /// </para>
    /// </remarks>
    ValueTask<ITenantScope> CreateScopeAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new ValueTask<ITenantScope>(CreateScope(tenantId));
    }
}
