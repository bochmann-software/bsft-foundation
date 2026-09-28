namespace Bsft.Data.MultiTenancy.Sandboxes;

/// <summary>Gives out an isolated tenant to try things in, and takes it away again.</summary>
/// <remarks>
/// <para>
/// Deliberately narrow. This is not a tenant lifecycle: there is no creation of an organization, no
/// seeding, no membership and no billing. <c>TenantId</c> is a plain string this framework has no
/// table for, so what is on offer here is an identifier nothing else uses and a way to give it back.
/// </para>
/// <para>
/// <strong>Two things to be honest about, and they are part of the contract rather than a note
/// afterwards.</strong>
/// </para>
/// <para>
/// A sandbox tenant is isolated in exactly the sense every other tenant is: by the scoping the host's
/// own queries apply. A host with a query that forgets its tenant predicate has a leaky sandbox, and
/// nothing here can fix that. What this seam promises is narrower and checkable - the identifier is
/// one nothing else was given.
/// </para>
/// <para>
/// What teardown removes is what this seam knows about: the registry entry, and whatever every
/// registered <see cref="ISandboxTenantCleanup"/> speaks for. Rows a host wrote under that tenant id
/// through its own <c>DbContext</c> are the host's to remove, and
/// <see cref="SandboxTeardown.ClearedBy"/> says which handlers actually ran so a caller can tell the
/// difference rather than assume.
/// </para>
/// <para>
/// The identifier is entered the same way every other tenant-scoped piece of work enters one, with
/// <see cref="TenantWorkScopeExtensions.EnterTenant"/>. There is no second mechanism for a sandbox,
/// because a sandbox that were entered differently would be exercising a path production never takes.
/// </para>
/// </remarks>
public interface ISandboxTenantProvisioner
{
    /// <summary>Provisions one sandbox tenant.</summary>
    /// <param name="spec">What it is for, who asked, and for how long.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The sandbox, with the identifier to enter.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="spec"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the specification cannot be used, or when no free identifier was found in the
    /// configured number of attempts.
    /// </exception>
    Task<SandboxTenant> ProvisionAsync(SandboxTenantSpecification spec, CancellationToken cancellationToken = default);

    /// <summary>Takes one sandbox tenant away.</summary>
    /// <param name="tenantId">The identifier this seam handed out.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What was actually removed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tenantId"/> is null.</exception>
    /// <remarks>
    /// An identifier the registry does not know is answered rather than refused, with
    /// <see cref="SandboxTeardown.WasKnown"/> false and no handler run. Tearing down twice is therefore
    /// safe, and naming a real tenant reaches nothing.
    /// </remarks>
    Task<SandboxTeardown> TearDownAsync(string tenantId, CancellationToken cancellationToken = default);
}
