using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Bsft.Hosting.SecurityHeaders;

/// <summary>Adds the configured baseline security headers to every response unless the endpoint set them itself.</summary>
/// <remarks>
/// The headers are written when the response starts, so they reach error responses produced further down and see
/// what an endpoint already set.
/// </remarks>
internal sealed class SecurityHeadersMiddleware
{
    private const string PermissionsPolicyHeader = "Permissions-Policy";
    private const string ReferrerPolicyHeader = "Referrer-Policy";

    private readonly RequestDelegate _next;
    private readonly (string Name, string Value)[] _headers;
    private readonly string? _contentSecurityPolicy;
    private readonly PathString[] _exemptPaths;

    /// <summary>Initializes a new instance of the <see cref="SecurityHeadersMiddleware"/> class.</summary>
    /// <param name="next">The next middleware.</param>
    /// <param name="options">The configured headers.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public SecurityHeadersMiddleware(RequestDelegate next, IOptions<BsftSecurityHeadersOptions> options)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(options);

        _next = next;
        var configured = options.Value;
        _headers = new (string Name, string? Value)[]
        {
            (HeaderNames.XContentTypeOptions, configured.ContentTypeOptions),
            (HeaderNames.XFrameOptions, configured.FrameOptions),
            (ReferrerPolicyHeader, configured.ReferrerPolicy),
            (PermissionsPolicyHeader, configured.PermissionsPolicy),
        }.Where(header => !string.IsNullOrEmpty(header.Value)).Select(header => (header.Name, header.Value!)).ToArray();
        _contentSecurityPolicy = string.IsNullOrEmpty(configured.ContentSecurityPolicy) ? null : configured.ContentSecurityPolicy;
        _exemptPaths = [.. configured.ContentSecurityPolicyExemptPaths.Select(path => new PathString(path))];
    }

    /// <summary>Registers the header-writing callback and invokes the rest of the pipeline.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task that completes when the downstream pipeline has run.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Read before the pipeline runs: a Map branch rewrites Request.Path and an exception handler re-executes
        // the request on another path, and neither may change whether the exemption matches.
        var requestPath = context.Request.Path;
        context.Response.OnStarting(ApplyAsync, (context, requestPath));
        return _next(context);
    }

    private Task ApplyAsync(object state)
    {
        var (context, requestPath) = ((HttpContext, PathString))state;
        var headers = context.Response.Headers;

        foreach (var (name, value) in _headers)
        {
            SetIfAbsent(headers, name, value);
        }

        if (_contentSecurityPolicy is not null && !_exemptPaths.Any(path => requestPath.StartsWithSegments(path)))
        {
            SetIfAbsent(headers, HeaderNames.ContentSecurityPolicy, _contentSecurityPolicy);
        }

        return Task.CompletedTask;
    }

    private static void SetIfAbsent(IHeaderDictionary headers, string name, string value)
    {
        if (!headers.ContainsKey(name))
        {
            headers[name] = value;
        }
    }
}
