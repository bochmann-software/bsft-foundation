using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Bsft.Hosting;

/// <summary>
/// Answers one failed request: maps the exception with the registered <see cref="IBsftExceptionMapper"/>s and the
/// framework's own mapping, logs it, tells the <see cref="IBsftExceptionObserver"/>s and writes the ProblemDetails response.
/// </summary>
/// <remarks>
/// <see cref="BsftExceptionMiddleware"/> and <see cref="BsftExceptionHandlerAdapter"/> are two entry points to this one
/// pipeline, so they cannot drift apart. Replacing it replaces both.
/// </remarks>
public interface IBsftExceptionResponder
{
    /// <summary>
    /// Maps one exception to a response, logs it, tells the observers and writes the response, unless it is already on the wire.
    /// </summary>
    /// <param name="context">The request that failed.</param>
    /// <param name="exception">What it failed with.</param>
    /// <param name="correlationId">The id the response and the log line are both stamped with.</param>
    /// <param name="logger">Where the failure is logged, so each entry point keeps its own log category.</param>
    /// <param name="cancellationToken">Cancels the observers and the write.</param>
    /// <returns>A task that completes when the response is written.</returns>
    Task RespondAsync(
        HttpContext context, Exception exception, string correlationId, ILogger logger, CancellationToken cancellationToken = default);
}
