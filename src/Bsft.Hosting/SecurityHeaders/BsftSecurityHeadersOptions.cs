namespace Bsft.Hosting.SecurityHeaders;

/// <summary>Configuration options for the baseline security response headers.</summary>
/// <remarks>
/// Each header member is the value the middleware sends; <see langword="null"/> or an empty string sends no such
/// header. A header an endpoint sets itself is never overwritten, so an endpoint that needs a stricter or a
/// looser value (a download served with a CSP sandbox, say) writes its own. <c>Strict-Transport-Security</c> is
/// not part of this type: it is ASP.NET Core's <c>UseHsts</c>, which already answers only on HTTPS requests and
/// leaves loopback hosts alone, and <c>UseBsftDefaults</c> turns it on.
/// </remarks>
public sealed class BsftSecurityHeadersOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:Hosting:SecurityHeaders";

    /// <summary>
    /// Gets or sets the <c>X-Content-Type-Options</c> value. <c>nosniff</c> by default, the only value the header has.
    /// </summary>
    public string? ContentTypeOptions { get; set; } = "nosniff";

    /// <summary>
    /// Gets or sets the <c>X-Frame-Options</c> value, <c>DENY</c> or <c>SAMEORIGIN</c>. <c>DENY</c> by default.
    /// </summary>
    public string? FrameOptions { get; set; } = "DENY";

    /// <summary>Gets or sets the <c>Referrer-Policy</c> value. <c>no-referrer</c> by default.</summary>
    public string? ReferrerPolicy { get; set; } = "no-referrer";

    /// <summary>
    /// Gets or sets the <c>Content-Security-Policy</c> value. <c>frame-ancestors 'none'</c> by default, which stops
    /// framing and leaves everything else a page loads alone; an API host that serves no documents may narrow it to
    /// <c>default-src 'none'; frame-ancestors 'none'</c>.
    /// </summary>
    public string? ContentSecurityPolicy { get; set; } = "frame-ancestors 'none'";

    /// <summary>
    /// Gets or sets the <c>Permissions-Policy</c> value. <see langword="null"/> by default: which features to switch
    /// off depends on what the host serves, so the framework does not guess.
    /// </summary>
    public string? PermissionsPolicy { get; set; }

    /// <summary>
    /// Gets the path prefixes (each starting with <c>/</c>, matched by whole segments) whose responses get no
    /// <c>Content-Security-Policy</c>, for a page that needs inline scripts such as Swagger UI. The other headers
    /// still apply there. Empty by default.
    /// </summary>
    public IList<string> ContentSecurityPolicyExemptPaths { get; } = [];
}
