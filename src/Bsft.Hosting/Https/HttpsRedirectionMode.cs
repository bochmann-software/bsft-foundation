namespace Bsft.Hosting.Https;

/// <summary>
/// Controls whether the BSFT HTTPS middleware is added to the pipeline.
/// </summary>
public enum HttpsRedirectionMode
{
    /// <summary>
    /// HSTS and HTTPS redirection are added to the pipeline. This is the default.
    /// </summary>
    Enabled,

    /// <summary>
    /// HSTS and HTTPS redirection are skipped, for hosts that terminate TLS upstream.
    /// </summary>
    Disabled
}
