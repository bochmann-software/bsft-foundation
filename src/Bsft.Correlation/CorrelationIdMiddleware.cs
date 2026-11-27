using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

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
    // The fixed ASCII alphabet needs no regex engine or wall-clock timeout. Checking the length
    // first bounds the work even when an untrusted caller supplies an oversized header.
    private const int MaximumCorrelationIdLength = 128;

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
        var correlationId = IsSafeCorrelationId(headerValue)
            ? headerValue
            : Guid.NewGuid().ToString();

        correlationContext.CorrelationId = correlationId;
        context.Response.Headers[CorrelationDefaults.CorrelationId] = correlationId;

        using (CorrelationLogScope.Begin(logger, correlationId))
        {
            await _next(context).ConfigureAwait(false);
        }
    }

    /// <summary>Checks the complete client value against the bounded ASCII header alphabet.</summary>
    /// <param name="value">The untrusted header value.</param>
    /// <returns>Whether every character is safe to echo into headers and logging scopes.</returns>
    private static bool IsSafeCorrelationId([NotNullWhen(true)] string? value)
    {
        if (value is null || value.Length is < 1 or > MaximumCorrelationIdLength)
            return false;

        foreach (var character in value)
        {
            if (character is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-'))
                return false;
        }

        return true;
    }

}
