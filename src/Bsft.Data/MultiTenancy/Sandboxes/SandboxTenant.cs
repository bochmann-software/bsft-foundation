namespace Bsft.Data.MultiTenancy.Sandboxes;

/// <summary>An isolated tenant to try things in, and when it stops being one.</summary>
/// <remarks>
/// <para>
/// The identifier is an ordinary <c>TenantId</c>: it is entered the way every other tenant-scoped
/// piece of work enters one, with <see cref="TenantWorkScopeExtensions.EnterTenant"/>, and everything
/// downstream reads it as the tenant it is. Nothing here makes it special, which is the point.
/// </para>
/// <para>
/// <see cref="ExpiresAt"/> is a statement, not a mechanism. Nothing in this package sweeps an expired
/// sandbox: a host that wants them collected reads them out of its own registry and calls the
/// provisioner, which is a job, not a seam.
/// </para>
/// </remarks>
public sealed class SandboxTenant
{
    /// <summary>Gets the tenant identifier nothing else uses.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets what it was asked for, in a few words, for a listing a person reads.</summary>
    public required string Label { get; init; }

    /// <summary>Gets who asked for it, as the host names people.</summary>
    public required string RequestedBy { get; init; }

    /// <summary>Gets when it was provisioned.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets when it stops being useful, or null when it was asked for without a lifetime.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}
