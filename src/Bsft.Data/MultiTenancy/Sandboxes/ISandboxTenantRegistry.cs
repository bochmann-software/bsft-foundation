namespace Bsft.Data.MultiTenancy.Sandboxes;

/// <summary>Where the identifiers this seam handed out are remembered.</summary>
/// <remarks>
/// <para>
/// Nothing registers an implementation, and the startup check refuses a host that brought none. It has
/// to be the host's, because the promise is that the identifier is one <em>nothing else uses</em>, and
/// only something durable can keep that promise across a restart. An in-memory default would look like
/// the seam working and would hand out a colliding identifier the first time the process recycled.
/// </para>
/// <para>
/// It is also what makes teardown safe. A teardown only runs over an identifier this registry knows,
/// so naming a real tenant reaches no handler.
/// </para>
/// </remarks>
public interface ISandboxTenantRegistry
{
    /// <summary>Claims one identifier, if it is free.</summary>
    /// <param name="tenant">The sandbox being provisioned.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// <see langword="true"/> when this call claimed it, <see langword="false"/> when something already
    /// had it.
    /// </returns>
    /// <remarks>
    /// Answering rather than throwing on a collision, because the provisioner's response is to try
    /// another identifier rather than to fail. An implementation over a database gets the answer from a
    /// unique constraint, which is the only place two racing callers can be told apart.
    /// </remarks>
    Task<bool> TryReserveAsync(SandboxTenant tenant, CancellationToken cancellationToken = default);

    /// <summary>Reads one sandbox back.</summary>
    /// <param name="tenantId">The identifier.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The sandbox, or <see langword="null"/> when this is not one.</returns>
    Task<SandboxTenant?> FindAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Forgets one sandbox.</summary>
    /// <param name="tenantId">The identifier.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when it is forgotten.</returns>
    Task RemoveAsync(string tenantId, CancellationToken cancellationToken = default);
}
