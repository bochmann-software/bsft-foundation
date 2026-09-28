using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Bsft.Correlation;

/// <summary>
/// ASP.NET Core middleware that reads or generates a correlation ID for each request.
/// Reads from the X-Correlation-Id header if present, otherwise generates a new GUID.
/// Stores the value in <see cref="ICorrelationContext"/>, adds it to the response headers,
/// and pushes it into the <see cref="ILogger"/> scope so that all log entries within the
/// request include the correlation ID.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    // Client-supplied correlation IDs are echoed into the response header and the logging scope, so
    // they must be constrained to a safe character set. This rejects CRLF and other control characters
    // (preventing response-header injection and log forging) as well as unbounded values.
    //
    // The end anchor is \z rather than $ on purpose: $ also matches immediately before a single
    // trailing newline, so "accepted\n" would pass a pattern that exists to reject exactly that.
    private static readonly Regex SafeCorrelationId =
        new(@"^[A-Za-z0-9._-]{1,128}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationIdMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    /// <summary>
    /// Invokes the middleware for the current HTTP context.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <param name="correlationContext">The correlation context to populate.</param>
    /// <param name="logger">The logger used to push the correlation ID into the logging scope.</param>
    public async Task InvokeAsync(HttpContext context, CorrelationContext correlationContext, ILogger<CorrelationIdMiddleware> logger)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        if (correlationContext is null)
            throw new ArgumentNullException(nameof(correlationContext));
        if (logger is null)
            throw new ArgumentNullException(nameof(logger));

        var headerValue = context.Request.Headers[CorrelationDefaults.CorrelationId].FirstOrDefault();

        // Only accept the client value when it is well-formed; otherwise generate a fresh ID.
        var correlationId = !string.IsNullOrWhiteSpace(headerValue) && SafeCorrelationId.IsMatch(headerValue)
            ? headerValue
            : Guid.NewGuid().ToString();

        correlationContext.CorrelationId = correlationId;
        context.Response.Headers[CorrelationDefaults.CorrelationId] = correlationId;

        using (CorrelationLogScope.Begin(logger, correlationId))
        {
            await _next(context).ConfigureAwait(false);
        }
    }
}
