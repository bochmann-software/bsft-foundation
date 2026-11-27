using Bsft.Correlation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bsft.Hosting;

/// <summary>
/// The <see cref="IExceptionHandler"/> entry point to the mapping <see cref="BsftExceptionMiddleware"/>
/// applies: the same mappers, the same observers and the same response, for a host that prefers
/// <c>AddExceptionHandler</c> and <c>UseExceptionHandler</c> to a middleware of its own.
/// </summary>
/// <remarks>
/// Register it with <c>AddBsftExceptionHandlerAdapter</c> and add <c>app.UseExceptionHandler()</c>. A request
/// the client aborted is not handled, so ASP.NET Core treats it as it always did.
/// </remarks>
public sealed class BsftExceptionHandlerAdapter : IExceptionHandler
{
    private readonly IBsftExceptionResponder _responder;
    private readonly ILogger<BsftExceptionHandlerAdapter> _logger;

    /// <summary>Initializes a new instance of the <see cref="BsftExceptionHandlerAdapter"/> class.</summary>
    /// <param name="responder">The pipeline that maps and writes.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public BsftExceptionHandlerAdapter(IBsftExceptionResponder responder, ILogger<BsftExceptionHandlerAdapter> logger)
    {
        _responder = responder ?? throw new ArgumentNullException(nameof(responder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
            return false;

        var correlationContext = httpContext.RequestServices.GetRequiredService<ICorrelationContext>();
        await _responder.RespondAsync(httpContext, exception, correlationContext.CorrelationId, _logger, cancellationToken).ConfigureAwait(false);

        return true;
    }
}
