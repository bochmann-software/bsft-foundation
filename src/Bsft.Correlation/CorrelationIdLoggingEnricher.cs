using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Bsft.Correlation;

/// <summary>
/// ASP.NET Core middleware that pushes the correlation ID into the <see cref="ILogger"/> scope
/// so that all log entries within a request include the correlation ID.
/// </summary>
public sealed class CorrelationIdLoggingEnricher
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationIdLoggingEnricher"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    public CorrelationIdLoggingEnricher(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    /// <summary>
    /// Invokes the middleware, wrapping the remaining pipeline in a logger scope that includes the correlation ID.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <param name="correlationContext">The correlation context containing the current correlation ID.</param>
    /// <param name="logger">The logger to enrich with the correlation ID.</param>
    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlationContext, ILogger<CorrelationIdLoggingEnricher> logger)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        if (correlationContext is null)
            throw new ArgumentNullException(nameof(correlationContext));
        if (logger is null)
            throw new ArgumentNullException(nameof(logger));

        using (CorrelationLogScope.Begin(logger, correlationContext.CorrelationId))
        {
            await _next(context).ConfigureAwait(false);
        }
    }
}
