using System.Net;
using Microsoft.Extensions.Options;

namespace Bsft.Http.Egress;

/// <summary>
/// The shipped policy: https unless plain HTTP was allowed, a name that is not purely internal, and no
/// address in a range that is not the public internet — with the operator's allow list on top.
/// </summary>
/// <remarks>
/// It is public and constructible because a caller with a policy of its own to apply to one client —
/// a package whose own options carry the private-network override for its own destinations — builds
/// one over the options it wants rather than reaching for the container-wide registration. Everything
/// else resolves <see cref="IOutboundAddressPolicy"/> and gets the registered instance.
/// </remarks>
public sealed class OutboundAddressPolicy : IOutboundAddressPolicy
{
    private readonly BsftHttpEgressOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboundAddressPolicy"/> class.
    /// </summary>
    /// <param name="options">The options carrying the overrides and the allow list.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public OutboundAddressPolicy(IOptions<BsftHttpEgressOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public OutboundAddressDecision Evaluate(Uri? url)
    {
        // The scheme is part of "absolute" here, not just UriKind.Absolute: a bare path parses as an
        // absolute file URL on a Unix runtime, so a scheme-blind check would call it absolute on Linux
        // and not on Windows. Anything that is not http or https is refused as not an absolute URL, and
        // the http-versus-https decision is made right after.
        if (url is null || !url.IsAbsoluteUri || !IsHttpUrl(url))
            return OutboundAddressDecision.Denied("The URL must be an absolute http or https URL.");

        if (!IsSchemeAllowed(url))
            return OutboundAddressDecision.Denied("The URL must use https.");

        return EvaluateHost(url.DnsSafeHost);
    }

    /// <inheritdoc />
    public OutboundAddressDecision EvaluateHost(string? host)
    {
        if (IsAllowListed(host))
            return OutboundAddressDecision.Allowed;

        if (host.IsSingleLabelHost())
            return OutboundAddressDecision.Denied("The URL must name a fully qualified host.");

        // A URL that names an address outright never reaches a resolver, so the connect-time guard is
        // not what catches it. Judging it here is what keeps http://169.254.169.254/ from being a
        // destination that passes every check an unresolved name is subject to.
        return host is not null && IPAddress.TryParse(host, out var literal)
            ? Judge(literal)
            : OutboundAddressDecision.Allowed;
    }

    /// <inheritdoc />
    public OutboundAddressDecision EvaluateAddress(string? host, IPAddress address)
    {
        if (address is null)
            throw new ArgumentNullException(nameof(address));

        return IsAllowListed(host) ? OutboundAddressDecision.Allowed : Judge(address);
    }

    /// <summary>Applies the range rules and the private-network override to one address.</summary>
    /// <param name="address">The address to judge.</param>
    /// <returns>The decision.</returns>
    private OutboundAddressDecision Judge(IPAddress address) =>
        _options.AllowPrivateNetworks || address.IsPublicAddress()
            ? OutboundAddressDecision.Allowed
            : OutboundAddressDecision.Denied("The endpoint is an address this application may not call.");

    /// <summary>Reports whether the operator named this host as one to let through.</summary>
    /// <param name="host">The host of the destination.</param>
    /// <returns><see langword="true"/> when it is on the allow list.</returns>
    /// <remarks>
    /// Written as a loop rather than as <c>Any</c> with a lambda: this is asked once per request and
    /// once per resolved address, and the closure a lambda over <paramref name="host"/> needs would be
    /// an allocation on both of those paths — including the ordinary case, where the list is empty and
    /// there is nothing to compare at all.
    /// </remarks>
    private bool IsAllowListed(string? host)
    {
        if (string.IsNullOrEmpty(host))
            return false;

        foreach (var allowed in _options.AllowedHosts)
        {
            if (string.Equals(allowed, host, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Reports whether the scheme of an absolute URL is one this application may use.</summary>
    /// <param name="url">The absolute URL to judge.</param>
    /// <returns><see langword="true"/> when the scheme is https, or http with the override on.</returns>
    private bool IsSchemeAllowed(Uri url) =>
        string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
        || (_options.AllowInsecureUrls && string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal));

    /// <summary>
    /// Reports whether an absolute URI carries an http or https scheme, the only two an outbound call
    /// under this policy may use.
    /// </summary>
    /// <param name="url">The absolute URI to judge.</param>
    /// <returns><see langword="true"/> when the scheme is http or https.</returns>
    private static bool IsHttpUrl(Uri url) =>
        string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
        || string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);
}
