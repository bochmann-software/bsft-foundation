using System.Net;

namespace Bsft.Http.Egress;

/// <summary>
/// The connect-time half of the SSRF defence: it resolves the host itself, judges the addresses, and
/// then connects to one it has judged.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes a create-time check hold. Between a destination being accepted and a request
/// being sent, whoever supplied the destination controls what their hostname resolves to; a name that
/// answered with a public address while the form was being validated can answer with
/// <c>169.254.169.254</c> an hour later. That is DNS rebinding, and no amount of validating the URL
/// prevents it.
/// </para>
/// <para>
/// The part that actually closes the hole is that an implementation connects to the
/// <see cref="IPAddress"/> it checked, rather than checking a name and then handing the name to the
/// socket to resolve again. A re-resolution between the check and the connect is the same race one
/// layer down, and it is the mistake this shape exists to make impossible.
/// </para>
/// </remarks>
public interface IOutboundConnectGuard
{
    /// <summary>
    /// Opens a connection to an endpoint, refusing to open one to an address that is not allowed.
    /// </summary>
    /// <param name="host">The host the request is for.</param>
    /// <param name="port">The port the request is for.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The connected stream.</returns>
    /// <exception cref="OutboundAddressRejectedException">
    /// Thrown when the host does not resolve, or resolves only to addresses this application may not
    /// call.
    /// </exception>
    Task<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the host and picks the first address that may be called.
    /// </summary>
    /// <param name="host">The host the request is for.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>An address that passed the rules.</returns>
    /// <exception cref="OutboundAddressRejectedException">Thrown when there is no such address.</exception>
    /// <remarks>
    /// The first acceptable address is taken rather than all of them being required to be acceptable,
    /// which is the opposite of what a create-time check does. The difference is deliberate: when a
    /// destination is accepted the question is "is this a sensible thing to store", where a mixed answer
    /// is a misconfiguration worth reporting, and here it is "may this connection be opened", where the
    /// address being connected to is known exactly.
    /// </remarks>
    Task<IPAddress> SelectAddressAsync(string host, CancellationToken cancellationToken = default);
}
