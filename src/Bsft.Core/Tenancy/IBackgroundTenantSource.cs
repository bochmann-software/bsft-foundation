namespace Bsft.Core.Tenancy;

/// <summary>
/// Names the tenants a background service works for, so that a host whose tenants live in separate
/// databases (or separate partitions of one) has each pass of an outbox dispatcher, an inbox sweeper or a job
/// dispatcher run once per tenant instead of once for whatever the unscoped container happens to reach.
/// </summary>
/// <remarks>
/// <para>
/// Optional. A host that registers none runs every background pass once, as before, outside any tenant. A
/// host that registers one has the services that support it (see <see cref="BackgroundTenantPasses"/>) run
/// each pass once per listed tenant, with <see cref="BackgroundTenant.Current"/> set for the duration, so that
/// every scope the pass opens resolves that tenant's context, connection and data.
/// </para>
/// <para>
/// The list is read once per pass, so a tenant added to the host's directory is picked up by the next pass
/// without a restart. The order of the list is the order the tenants are served in.
/// </para>
/// </remarks>
public interface IBackgroundTenantSource
{
    /// <summary>Lists the tenants a background pass is to run for.</summary>
    /// <param name="cancellationToken">A token to observe while the list is read.</param>
    /// <returns>The tenant ids, each non-empty; an empty list means there is nothing to do.</returns>
    ValueTask<IReadOnlyList<string>> ListTenantsAsync(CancellationToken cancellationToken = default);
}
