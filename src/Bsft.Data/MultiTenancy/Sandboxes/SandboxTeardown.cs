namespace Bsft.Data.MultiTenancy.Sandboxes;

/// <summary>What taking a sandbox away actually removed.</summary>
/// <remarks>
/// Reported rather than assumed, because the honest answer is narrower than "the tenant is gone".
/// What ran is what the host registered, and rows written under that tenant id through the host's own
/// <c>DbContext</c> are the host's to remove.
/// </remarks>
public sealed class SandboxTeardown
{
    /// <summary>Gets the tenant that was torn down.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets a value indicating whether this was a sandbox at all.</summary>
    /// <remarks>
    /// False means the registry had never heard of the identifier, and nothing was removed. A teardown
    /// that ran its handlers over an unknown identifier would be a way to delete a real tenant's data
    /// by naming it.
    /// </remarks>
    public required bool WasKnown { get; init; }

    /// <summary>Gets the type names of the handlers that ran, in the order they ran.</summary>
    public required IReadOnlyList<string> ClearedBy { get; init; }
}
