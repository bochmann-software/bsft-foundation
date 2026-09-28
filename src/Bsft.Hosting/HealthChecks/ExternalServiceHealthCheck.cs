using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bsft.Hosting.HealthChecks;

/// <summary>
/// Health check that verifies an external service is reachable by performing an HTTP GET request.
/// </summary>
public sealed class ExternalServiceHealthCheck : IHealthCheck
{
    /// <summary>
    /// Upper bound for a single probe. A health check must answer quickly; a slow external service is
    /// reported as unhealthy rather than blocking the health endpoint.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly string _serviceName;
    private readonly string _healthUrl;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalServiceHealthCheck"/> class.
    /// </summary>
    /// <param name="serviceName">The name of the external service being checked.</param>
    /// <param name="healthUrl">The health check URL to probe.</param>
    /// <param name="httpClientFactory">The HTTP client factory for creating clients.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="healthUrl"/> is not an absolute HTTP or HTTPS URL. A health check is a request
    /// this service makes to an address someone configured, so anything else would turn a
    /// mis-configuration into a probe of the local file system or the cloud metadata endpoint.
    /// </exception>
    public ExternalServiceHealthCheck(string serviceName, string healthUrl, IHttpClientFactory httpClientFactory)
    {
        _serviceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
        _healthUrl = healthUrl ?? throw new ArgumentNullException(nameof(healthUrl));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

        // Reject non-HTTP(S) targets (e.g. file://, ftp://, or the cloud metadata endpoint) so a
        // mis-configured health URL cannot be turned into an SSRF/local-file probe.
        if (!Uri.TryCreate(healthUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "Health URL must be an absolute http or https URL.", nameof(healthUrl));
        }
    }

    /// <summary>
    /// Performs the health check by sending an HTTP GET request to the configured URL.
    /// </summary>
    /// <param name="context">The health check context.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A <see cref="HealthCheckResult"/> indicating the health status.</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = ProbeTimeout;

            var response = await client.GetAsync(_healthUrl, cancellationToken).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy($"Service '{_serviceName}' is reachable.")
                : HealthCheckResult.Unhealthy($"Service '{_serviceName}' returned status code {(int)response.StatusCode}.");
        }
        // A health probe is a sanitising boundary, so the catch stays broad on purpose. Whatever a
        // probe fails on - the service was unreachable, it did not answer within the timeout, or the
        // client was misconfigured - the caller is told the same fixed sentence and the exception
        // travels in the result object rather than in the description.
        //
        // Narrowing this to HttpRequestException and TaskCanceledException was tried and reverted:
        // an exception that escapes here is caught by ASP.NET Core's HealthCheckService instead,
        // which builds the entry from the raw exception message. Any consumer that writes a custom
        // health response echoing HealthReportEntry.Description would then publish internal detail
        // over an endpoint that is usually unauthenticated.
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Service '{_serviceName}' is unreachable.", ex);
        }
    }
}
