using System.Net.Sockets;
using System.Net;

namespace Bsft.Http.Egress;

/// <summary>
/// Which IP addresses an application may open an outbound connection to.
/// </summary>
/// <remarks>
/// <para>
/// This is the rule the two SSRF layers share. A create-time check on a URL and the connect-time
/// re-check ask exactly this question, of exactly this code, so the two cannot drift into disagreeing
/// about what "internal" means — which is the failure that makes a create-time check look like
/// protection while the connection goes somewhere else.
/// </para>
/// <para>
/// It is written as a deny list over address ranges rather than an allow list of customer addresses,
/// because an allow list is not something an application can have where the destination is chosen by
/// somebody else: the whole point of a webhook or a provider callback is that the other side picks the
/// address. The ranges denied are the ones that are not the public internet — loopback, link-local
/// (which includes the cloud metadata address), the private ranges, unspecified, multicast, and the
/// IPv6 equivalents including addresses that embed an IPv4 one.
/// </para>
/// <para>
/// It is public because the rule is the thing that must not be duplicated: a package with a
/// create-time check of its own — one that reports its refusals in its own vocabulary rather than in
/// this package's — asks these two predicates rather than writing the ranges out again.
/// </para>
/// </remarks>
public static class OutboundAddressRules
{
    /// <summary>0.0.0.0/8: the unspecified address and the "this network" range.</summary>
    private const byte Unspecified = 0;

    /// <summary>10.0.0.0/8, private.</summary>
    private const byte PrivateClassA = 10;

    /// <summary>127.0.0.0/8, loopback.</summary>
    private const byte Loopback = 127;

    /// <summary>100.64.0.0/10, carrier-grade NAT: shared address space rather than the public internet.</summary>
    private const byte SharedAddressSpace = 100;

    /// <summary>The first second octet of the carrier-grade NAT range.</summary>
    private const byte SharedAddressSpaceFirst = 64;

    /// <summary>The last second octet of the carrier-grade NAT range.</summary>
    private const byte SharedAddressSpaceLast = 127;

    /// <summary>169.254.0.0/16, link-local, where every cloud provider keeps its metadata service.</summary>
    private const byte LinkLocalFirstOctet = 169;

    /// <summary>The second octet of the link-local range.</summary>
    private const byte LinkLocalSecondOctet = 254;

    /// <summary>172.16.0.0/12, private.</summary>
    private const byte PrivateClassB = 172;

    /// <summary>The first second octet of the 172.16/12 range.</summary>
    private const byte PrivateClassBFirst = 16;

    /// <summary>The last second octet of the 172.16/12 range.</summary>
    private const byte PrivateClassBLast = 31;

    /// <summary>192.x: protocol assignments, documentation, and the private 192.168/16 range.</summary>
    private const byte ReservedOneNineTwo = 192;

    /// <summary>The zero octet, which several reserved ranges have in second or third position.</summary>
    private const byte ZeroOctet = 0;

    /// <summary>The third octet of 192.0.2/24, reserved for documentation.</summary>
    private const byte FirstDocumentationThirdOctet = 2;

    /// <summary>The second octet of the private 192.168/16 range.</summary>
    private const byte PrivateClassCSecondOctet = 168;

    /// <summary>The second octet of 192.88.99/24, the deprecated six-to-four relay anycast range.</summary>
    private const byte SixToFourRelaySecondOctet = 88;

    /// <summary>The third octet of the 192.88.99/24 relay anycast range.</summary>
    private const byte SixToFourRelayThirdOctet = 99;

    /// <summary>198.x: the benchmarking and documentation ranges.</summary>
    private const byte ReservedOneNineEight = 198;

    /// <summary>The first second octet of the 198.18/15 benchmarking range.</summary>
    private const byte BenchmarkingFirst = 18;

    /// <summary>The last second octet of the 198.18/15 benchmarking range.</summary>
    private const byte BenchmarkingLast = 19;

    /// <summary>The second octet of the 198.51.100/24 documentation range.</summary>
    private const byte SecondDocumentationSecondOctet = 51;

    /// <summary>The third octet of the 198.51.100/24 documentation range.</summary>
    private const byte SecondDocumentationThirdOctet = 100;

    /// <summary>203.x: the third documentation range.</summary>
    private const byte ReservedTwoZeroThree = 203;

    /// <summary>The third octet of the 203.0.113/24 documentation range.</summary>
    private const byte ThirdDocumentationThirdOctet = 113;

    /// <summary>224.0.0.0/4 upwards: multicast, reserved and broadcast.</summary>
    private const byte FirstMulticastOctet = 224;

    /// <summary>The mask that isolates the fc00::/7 prefix of a unique local address.</summary>
    private const byte UniqueLocalMask = 0xFE;

    /// <summary>The fc00::/7 prefix itself.</summary>
    private const byte UniqueLocalPrefix = 0xFC;

    /// <summary>The first byte of the 2002::/16 six-to-four prefix.</summary>
    private const byte SixToFourFirstByte = 0x20;

    /// <summary>The second byte of the 2002::/16 six-to-four prefix.</summary>
    private const byte SixToFourSecondByte = 0x02;

    /// <summary>Where the embedded IPv4 address starts inside a six-to-four address.</summary>
    private const int SixToFourAddressStart = 2;

    /// <summary>Where the embedded IPv4 address ends inside a six-to-four address.</summary>
    private const int SixToFourAddressEnd = 6;

    /// <summary>Where the embedded IPv4 address starts inside a NAT64 address.</summary>
    private const int Nat64AddressStart = 12;

    /// <summary>The four bytes of the 64:ff9b::/96 well-known NAT64 prefix.</summary>
    private static readonly byte[] Nat64Prefix = [0x00, 0x64, 0xFF, 0x9B];

    /// <summary>How many bytes an IPv4 address is written into.</summary>
    private const int V4Length = 4;

    /// <summary>How many bytes an IPv6 address is written into.</summary>
    private const int V6Length = 16;

    /// <summary>The separator between the labels of a hostname.</summary>
    private const char LabelSeparator = '.';

    /// <summary>
    /// Reports whether an application may open a connection to an address.
    /// </summary>
    /// <param name="address">The address the endpoint resolved to.</param>
    /// <returns><see langword="true"/> when it is a public address this application may call.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="address"/> is null.</exception>
    public static bool IsPublicAddress(this IPAddress address)
    {
        if (address is null)
            throw new ArgumentNullException(nameof(address));

        // An IPv4 address written as an IPv6 one is the same address, and treating it as a separate
        // family is how ::ffff:127.0.0.1 gets past a check that only knows about 127.0.0.1.
        if (address.IsIPv4MappedToIPv6)
            return address.MapToIPv4().IsPublicAddress();

        // An IPAddress is one of exactly two families — the type cannot be constructed with anything
        // else — so this is a two-way decision rather than a switch with an unreachable default.
        return address.AddressFamily == AddressFamily.InterNetworkV6
            ? IsPublicV6(address)
            : IsPublicV4(address);
    }

    /// <summary>Judges an IPv4 address against the ranges that are not the public internet.</summary>
    /// <param name="address">The address to judge.</param>
    /// <returns><see langword="true"/> when it is an ordinary public address.</returns>
    private static bool IsPublicV4(IPAddress address)
    {
        // Written into the stack rather than through GetAddressBytes, which allocates an array for
        // every address judged, and one address is judged for every connection opened.
        Span<byte> octets = stackalloc byte[V4Length];
        address.TryWriteBytes(octets, out _);

        return octets[0] switch
        {
            Unspecified or PrivateClassA or Loopback => false,

            SharedAddressSpace when octets[1] is >= SharedAddressSpaceFirst and <= SharedAddressSpaceLast => false,

            LinkLocalFirstOctet when octets[1] == LinkLocalSecondOctet => false,

            PrivateClassB when octets[1] is >= PrivateClassBFirst and <= PrivateClassBLast => false,

            ReservedOneNineTwo when octets[1] == ZeroOctet
                && octets[2] is ZeroOctet or FirstDocumentationThirdOctet => false,
            ReservedOneNineTwo when octets[1] == PrivateClassCSecondOctet => false,
            ReservedOneNineTwo when octets[1] == SixToFourRelaySecondOctet
                && octets[2] == SixToFourRelayThirdOctet => false,

            ReservedOneNineEight when octets[1] is BenchmarkingFirst or BenchmarkingLast => false,
            ReservedOneNineEight when octets[1] == SecondDocumentationSecondOctet
                && octets[2] == SecondDocumentationThirdOctet => false,

            ReservedTwoZeroThree when octets[1] == ZeroOctet
                && octets[2] == ThirdDocumentationThirdOctet => false,

            >= FirstMulticastOctet => false,

            _ => true,
        };
    }

    /// <summary>Judges an IPv6 address, reading out an embedded IPv4 one where it carries it.</summary>
    /// <param name="address">The address to judge.</param>
    /// <returns><see langword="true"/> when it is an ordinary public address.</returns>
    private static bool IsPublicV6(IPAddress address)
    {
        if (IPAddress.IPv6Loopback.Equals(address) || IPAddress.IPv6Any.Equals(address))
            return false;

        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6Teredo)
            return false;

        Span<byte> bytes = stackalloc byte[V6Length];
        address.TryWriteBytes(bytes, out _);

        // fc00::/7 unique local addresses, the IPv6 equivalent of the private ranges.
        if ((bytes[0] & UniqueLocalMask) == UniqueLocalPrefix)
            return false;

        // ::a.b.c.d, the deprecated IPv4-compatible form of RFC 4291. The BCL does not report it as
        // mapped, so without this arm ::169.254.169.254 is judged as an unlisted IPv6 address and
        // allowed, which is the same smuggling the mapped form is already stopped from doing. The
        // form is deprecated rather than gone: a resolver still hands it back when a name has an
        // AAAA record written that way, and the check costs twelve bytes of comparison.
        if (!bytes[..Nat64AddressStart].ContainsAnyExcept((byte)0))
            return IsPublicV4(new IPAddress(bytes[Nat64AddressStart..]));

        // 64:ff9b::/96 and 2002::/16 carry an IPv4 address inside an IPv6 one. Reading that address out
        // and judging it on its own terms is the only way a private destination cannot be smuggled
        // through a translation prefix.
        if (bytes[0] == SixToFourFirstByte && bytes[1] == SixToFourSecondByte)
            return IsPublicV4(new IPAddress(bytes[SixToFourAddressStart..SixToFourAddressEnd]));

        if (bytes[..Nat64Prefix.Length].SequenceEqual(Nat64Prefix))
            return IsPublicV4(new IPAddress(bytes[Nat64AddressStart..]));

        return true;
    }

    /// <summary>
    /// Reports whether a hostname is a single label — <c>intranet</c>, <c>db</c>, <c>metadata</c> —
    /// rather than a name with a public suffix.
    /// </summary>
    /// <param name="host">The host part of the endpoint URL.</param>
    /// <returns><see langword="true"/> when the name is one label, with or without a trailing dot.</returns>
    /// <remarks>
    /// <para>
    /// A single-label name is resolved through the host's own search domains, so it names something on
    /// the network this process runs in almost by definition. It is rejected separately from the
    /// address check because it is worth rejecting even when it happens to resolve to something public
    /// today: the name means something different from inside the network than it does anywhere else,
    /// which is exactly the ambiguity to keep out of a URL somebody else chose.
    /// </para>
    /// <para>
    /// A trailing dot is stripped first. DNS reads <c>metadata.</c> as the absolute form of the same
    /// one label, so a check that counted it as a dotted name would be answered differently by one
    /// character that changes nothing about where the name points.
    /// </para>
    /// <para>
    /// A host carrying a colon is an IPv6 literal rather than a name, and the address rules judge it.
    /// </para>
    /// </remarks>
    public static bool IsSingleLabelHost(this string? host)
    {
        if (string.IsNullOrEmpty(host) || host.Contains(':', StringComparison.Ordinal))
            return false;

        return !host.TrimEnd(LabelSeparator).Contains(LabelSeparator, StringComparison.Ordinal);
    }
}
