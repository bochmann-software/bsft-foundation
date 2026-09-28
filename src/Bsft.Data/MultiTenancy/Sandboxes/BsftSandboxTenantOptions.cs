namespace Bsft.Data.MultiTenancy.Sandboxes;

/// <summary>How sandbox tenant identifiers are shaped and how long they last.</summary>
public sealed class BsftSandboxTenantOptions
{
    /// <summary>The configuration section these bind from.</summary>
    public const string SectionName = "Bsft:SandboxTenants";

    /// <summary>Gets or sets what every sandbox identifier begins with. Default <c>sandbox</c>.</summary>
    /// <remarks>
    /// A readable marker rather than a security boundary: it is what makes a sandbox obvious in a log
    /// line, a support conversation and a database. What actually keeps a sandbox from colliding with a
    /// real tenant is the registry, not this string.
    /// </remarks>
    public string IdPrefix { get; set; } = "sandbox";

    /// <summary>Gets or sets how long a sandbox lasts when the caller named no lifetime. Default 7 days.</summary>
    public TimeSpan DefaultLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Gets or sets how many identifiers to try before giving up. Default 5.</summary>
    /// <remarks>
    /// A collision means a random identifier was already taken, which on a well-behaved registry
    /// happens approximately never. The retry exists so that a registry with a narrower identifier
    /// space than this one assumes still works, and the ceiling exists so a registry that refuses
    /// every claim fails rather than spins.
    /// </remarks>
    public int MaxProvisionAttempts { get; set; } = 5;
}
