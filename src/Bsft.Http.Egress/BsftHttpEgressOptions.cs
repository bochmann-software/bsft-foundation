namespace Bsft.Http.Egress;

/// <summary>
/// Where this application is allowed to send an outbound request that somebody else chose the
/// destination of: whether the internal network is reachable at all, whether plain HTTP is, and the
/// names that are exempt from both.
/// </summary>
/// <remarks>
/// The defaults are the strict ones. An application that never configures this section refuses plain
/// HTTP, refuses single-label names, and refuses every address that is not on the public internet —
/// which is the posture a destination supplied by a subscriber, a tenant or a provider profile has to
/// be treated with.
/// </remarks>
public sealed class BsftHttpEgressOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:Http:Egress";

    /// <summary>
    /// Gets or sets whether an outbound request may reach an address inside the network this process
    /// runs in. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// This is a development override, and it is the most dangerous switch in this package: with it on,
    /// a request to a destination somebody else chose is a request this application makes from inside
    /// the network on their behalf, which is the whole of server-side request forgery. It exists so
    /// that a developer can point at <c>localhost:5000</c>, and it belongs in a development
    /// configuration file and nowhere else.
    /// </remarks>
    public bool AllowPrivateNetworks { get; set; }

    /// <summary>
    /// Gets or sets whether an outbound request may use a plain <c>http</c> URL. Defaults to
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The other development override. What is sent over plain HTTP is readable by anything on the
    /// path, including the credentials an outbound call carries.
    /// </remarks>
    public bool AllowInsecureUrls { get; set; }

    /// <summary>
    /// Gets the host names that are exempt from the address rules, matched case-insensitively against
    /// the host of the destination. Empty by default.
    /// </summary>
    /// <remarks>
    /// This is the deliberate hole: an internal service an application is meant to call — a mail relay,
    /// an on-premises gateway — named by the operator rather than by whoever supplied the destination.
    /// A name on this list bypasses the single-label rule and the address rules together, so it is the
    /// one place where an internal address becomes reachable without switching the whole policy off.
    /// Match is on the name, not on what it resolves to, so a name here is trusted for as long as it is
    /// listed.
    /// </remarks>
    public IList<string> AllowedHosts { get; } = [];
}
