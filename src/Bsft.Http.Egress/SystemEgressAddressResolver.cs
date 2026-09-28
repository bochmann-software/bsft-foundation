using System.Net.Sockets;
using System.Net;

namespace Bsft.Http.Egress;

/// <summary>
/// Resolves through the host's own DNS.
/// </summary>
internal sealed class SystemEgressAddressResolver : IEgressAddressResolver
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            return [];

        // A literal address is not resolved at all. Dns.GetHostAddresses accepts one and hands it
        // straight back, but going through the resolver for a value that is already an address means a
        // name resolution failure can be reported for something that never needed resolving.
        if (IPAddress.TryParse(host, out var literal))
            return [literal];

        try
        {
            return await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            // A name that does not resolve is an ordinary answer to "where does this point", not a
            // failure of this method: the caller's next step is to refuse the destination either way,
            // and it says so far better than a socket error does.
            return [];
        }
    }
}
