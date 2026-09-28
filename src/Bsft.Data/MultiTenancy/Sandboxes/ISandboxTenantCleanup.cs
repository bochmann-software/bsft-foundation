namespace Bsft.Data.MultiTenancy.Sandboxes;

/// <summary>Removes what one package holds under a sandbox tenant.</summary>
/// <remarks>
/// <para>
/// Every registered implementation runs on teardown, in registration order, and each one speaks only
/// for its own rows. A package that stores something per tenant registers one; a host that writes its
/// own rows under that tenant id through its own <c>DbContext</c> registers one too, or removes them
/// itself.
/// </para>
/// <para>
/// A handler that throws stops the teardown, and the sandbox stays in the registry. That is the useful
/// direction: a half-cleared tenant that is still listed can be torn down again, whereas one that was
/// forgotten while its rows survived is a leak nobody can name.
/// </para>
/// </remarks>
public interface ISandboxTenantCleanup
{
    /// <summary>Removes this package's rows for one sandbox tenant.</summary>
    /// <param name="tenantId">The sandbox being taken away.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when this package holds nothing more under it.</returns>
    Task ClearAsync(string tenantId, CancellationToken cancellationToken = default);
}
