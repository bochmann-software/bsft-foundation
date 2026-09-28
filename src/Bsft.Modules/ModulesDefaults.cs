namespace Bsft.Modules;

/// <summary>
/// What a host needs to know about the module system's own surface.
/// </summary>
public static class ModulesDefaults
{
    /// <summary>The route the discovery endpoint is mapped to.</summary>
    /// <remarks>
    /// A host that puts its own gateway, proxy rule or rate limit in front of the endpoint needs the
    /// path as a value rather than as a string copied out of the documentation.
    /// </remarks>
    public const string DiscoveryRoute = "/api/modules";
}
