using System.Net;

namespace Bsft.Http.Egress;

/// <summary>
/// Judges where this application may open an outbound connection to.
/// </summary>
/// <remarks>
/// <para>
/// It answers the same question at the two moments an application asks it: when a destination is
/// accepted from somebody else and stored, and when a connection to that destination is actually
/// opened. Both go through one implementation so the two cannot drift into disagreeing about what
/// "internal" means, which is the failure that makes a create-time check look like protection while
/// the connection goes somewhere else.
/// </para>
/// <para>
/// A host replaces this when its own network has ranges the shipped rules do not know about. Replacing
/// it changes what every consumer of the guard may reach, which is the point: it is one decision, in
/// one place.
/// </para>
/// </remarks>
public interface IOutboundAddressPolicy
{
    /// <summary>
    /// Judges a destination URL, without resolving it.
    /// </summary>
    /// <param name="url">The URL a request is about to be sent to.</param>
    /// <returns>The decision, carrying the rule that refused when it refused.</returns>
    /// <remarks>
    /// This is the cheap half: the scheme, the shape of the host, and the address itself when the URL
    /// names one literally. A name is not resolved here, because a decision taken about a resolution
    /// that a later connection repeats is a decision about something else — see
    /// <see cref="IOutboundConnectGuard"/> for the half that closes that gap.
    /// </remarks>
    OutboundAddressDecision Evaluate(Uri? url);

    /// <summary>
    /// Judges the host part of a destination, without resolving it.
    /// </summary>
    /// <param name="host">The host part of the URL, which may itself be a literal address.</param>
    /// <returns>The decision, carrying the rule that refused when it refused.</returns>
    OutboundAddressDecision EvaluateHost(string? host);

    /// <summary>
    /// Judges an address a host resolved to.
    /// </summary>
    /// <param name="host">The host that resolved to it, which an allow list is matched against.</param>
    /// <param name="address">The address itself.</param>
    /// <returns>The decision, carrying the rule that refused when it refused.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="address"/> is null.</exception>
    OutboundAddressDecision EvaluateAddress(string? host, IPAddress address);
}
