namespace Bsft.Hosting.Https;

/// <summary>
/// Whether the host redirects HTTP to HTTPS and advertises HSTS.
/// </summary>
/// <remarks>
/// This is the one hosting setting that the pipeline reads back at <c>UseBsftDefaults</c> time rather
/// than acting on at registration, which is why it has an options type of its own: the decision is made
/// where the services are registered and needed where the middleware is added, and
/// <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/> is the seam between the two. A host
/// that terminates TLS upstream sets <see cref="RedirectionMode"/> to
/// <see cref="HttpsRedirectionMode.Disabled"/>, in configuration or through
/// <c>BsftDefaultsConfiguration.DisableHttpsRedirection()</c>.
/// </remarks>
public sealed class BsftHttpsOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:Hosting:Https";

    /// <summary>
    /// Gets or sets whether HSTS and HTTPS redirection are added to the pipeline. Defaults to
    /// <see cref="HttpsRedirectionMode.Enabled"/>.
    /// </summary>
    public HttpsRedirectionMode RedirectionMode { get; set; } = HttpsRedirectionMode.Enabled;
}
