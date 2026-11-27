using Bsft.Core;
using Bsft.Core.Observers;
using Bsft.Correlation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bsft.Hosting;

/// <summary>
/// ASP.NET Core middleware that catches <see cref="BsftException"/> subtypes and unhandled exceptions,
/// mapping them to RFC 9457 ProblemDetails responses with appropriate HTTP status codes.
/// </summary>
public sealed class BsftExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<BsftExceptionMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BsftExceptionMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public BsftExceptionMiddleware(RequestDelegate next, ILogger<BsftExceptionMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Invokes the middleware, catching exceptions and converting them to ProblemDetails responses.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <param name="correlationContext">The correlation context for the current request.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlationContext)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        if (correlationContext is null)
            throw new ArgumentNullException(nameof(correlationContext));

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client disconnected; do not attempt to write a 500 response to an aborted request.
            throw;
        }
        catch (Exception ex)
        {
            await ResponderOf(context)
                .RespondAsync(context, ex, correlationContext.CorrelationId, _logger, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The responder the host registered, or one holding the built-in mapping alone when it registered none, so a host
    /// that never touches the extension points needs no registration at all.
    /// </summary>
    /// <param name="context">The request that failed.</param>
    /// <returns>The responder.</returns>
    private static IBsftExceptionResponder ResponderOf(HttpContext context)
    {
        if (context.RequestServices?.GetService<IBsftExceptionResponder>() is { } registered)
            return registered;

        var guard = new ObserverGuard(new SilentObserverFailureReporter());

        return new BsftExceptionResponder([], new BuiltInExceptionMapper(), new ObserverComposite<IBsftExceptionObserver>([], guard), guard);
    }
}
