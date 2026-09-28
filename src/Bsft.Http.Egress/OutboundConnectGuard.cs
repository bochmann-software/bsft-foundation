using System.Net.Sockets;
using System.Net;

namespace Bsft.Http.Egress;

/// <summary>
/// The shipped <see cref="IOutboundConnectGuard"/>: it resolves the host through the seam, judges the
/// addresses through the policy, and connects to the one it judged.
/// </summary>
/// <remarks>
/// It is public and constructible for the same reason <see cref="OutboundAddressPolicy"/> is: a caller
/// that guards one client under its own policy builds the pair itself, and everything else resolves
/// <see cref="IOutboundConnectGuard"/> from the container.
/// </remarks>
public sealed class OutboundConnectGuard : IOutboundConnectGuard
{
    private readonly IOutboundAddressPolicy _policy;
    private readonly IEgressAddressResolver _resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboundConnectGuard"/> class.
    /// </summary>
    /// <param name="policy">The policy that judges the addresses the host resolves to.</param>
    /// <param name="resolver">The resolver used to find out where the host points right now.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public OutboundConnectGuard(IOutboundAddressPolicy policy, IEgressAddressResolver resolver)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    /// <inheritdoc />
    public async Task<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var address = await SelectAddressAsync(host, cancellationToken).ConfigureAwait(false);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            await socket.ConnectAsync(address, port, cancellationToken).ConfigureAwait(false);

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            // The socket owns an operating-system handle and nothing else will dispose it: the stream
            // that would have taken ownership was never constructed.
            socket.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The host is judged again for every address rather than once for the whole answer. That repeats
    /// the allow-list check, which is invariant across the loop, and it is left that way on purpose:
    /// hoisting it would mean a policy member whose only job is to say "this host bypasses the address
    /// rules", which is a hole in the contract to save an empty-list check next to a DNS lookup and a
    /// TCP connect. One question, asked of one method, is worth more here than the comparison it saves.
    /// </remarks>
    public async Task<IPAddress> SelectAddressAsync(string host, CancellationToken cancellationToken = default)
    {
        var addresses = await _resolver.ResolveAsync(host, cancellationToken).ConfigureAwait(false);

        foreach (var address in addresses)
        {
            if (_policy.EvaluateAddress(host, address).IsAllowed)
                return address;
        }

        // The message names the rule and not the address. It travels into the host's own logs, and a
        // caller that reports failures onwards replaces it with a fixed sentence first, but there is no
        // reason for the resolved address to be in either.
        throw new OutboundAddressRejectedException(
            "The endpoint does not resolve to an address this application may call.");
    }
}
