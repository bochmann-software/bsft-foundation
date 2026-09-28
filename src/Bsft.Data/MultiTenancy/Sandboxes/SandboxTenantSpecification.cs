namespace Bsft.Data.MultiTenancy.Sandboxes;

/// <summary>What a caller says when it asks for a sandbox.</summary>
/// <remarks>
/// Not the identifier. A caller that named its own tenant id could name one already in use, and the
/// isolation this seam promises is exactly that nothing else uses it.
/// </remarks>
public sealed class SandboxTenantSpecification
{
    /// <summary>Gets what the sandbox is for, in a few words.</summary>
    public required string Label { get; init; }

    /// <summary>Gets who is asking, as the host names people.</summary>
    public required string RequestedBy { get; init; }

    /// <summary>Gets how long it should last, or null for the configured default.</summary>
    /// <remarks>
    /// <see cref="TimeSpan.Zero"/> and below are refused rather than read as "forever": a sandbox that
    /// expired before it was handed out is a request nobody meant to make.
    /// </remarks>
    public TimeSpan? Lifetime { get; init; }
}
