using Bsft.Core.Observers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Bsft.Hosting;

/// <summary>The default <see cref="IBsftExceptionResponder"/>.</summary>
internal sealed class BsftExceptionResponder : IBsftExceptionResponder
{
    internal const string GenericDetail = "An unexpected error occurred. Please use the correlation ID for troubleshooting.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IObserverGuard _guard;
    private readonly BuiltInExceptionMapper _builtIn;
    private readonly IReadOnlyList<IBsftExceptionMapper> _mappers;
    private readonly ObserverComposite<IBsftExceptionObserver> _observers;

    /// <summary>Initializes a new instance of the <see cref="BsftExceptionResponder"/> class.</summary>
    /// <param name="mappers">The registered mappers, in the order they are asked, before the built-in mapping.</param>
    /// <param name="builtIn">The framework's own mapping, asked last.</param>
    /// <param name="observers">The registered observers, in the order they are told.</param>
    /// <param name="guard">Contains a failure of the log sink or of an observer.</param>
    public BsftExceptionResponder(
        IEnumerable<IBsftExceptionMapper> mappers,
        BuiltInExceptionMapper builtIn,
        ObserverComposite<IBsftExceptionObserver> observers,
        IObserverGuard guard)
    {
        _mappers = [.. mappers];
        _builtIn = builtIn;
        _observers = observers;
        _guard = guard;
    }

    /// <inheritdoc />
    public async Task RespondAsync(
        HttpContext context, Exception exception, string correlationId, ILogger logger, CancellationToken cancellationToken = default)
    {
        var result = Map(context, exception, correlationId);

        LogException(logger, exception, result.StatusCode, correlationId);

        await _observers.NotifyAsync(
            (observer, token) => observer.ExceptionMappedAsync(exception, result, context, token), cancellationToken).ConfigureAwait(false);

        if (context.Response.HasStarted)
        {
            // Headers/body are already on the wire; setting the status now would throw. Log and bail.
            Report(logger, log => log.LogWarning(
                "Response already started; cannot write ProblemDetails for status {StatusCode}.", result.StatusCode));
            return;
        }

        context.Response.StatusCode = result.StatusCode;

        if (result.Headers is { } headers)
            foreach (var header in headers)
                context.Response.Headers[header.Key] = header.Value;

        var problemDetails = new ProblemDetails
        {
            Type = result.Type ?? $"https://httpstatuses.io/{result.StatusCode}",
            Title = result.Title,
            Status = result.StatusCode,
            Detail = result.Detail
        };

        if (result.Extensions is { } extensions)
            foreach (var ext in extensions)
                problemDetails.Extensions[ext.Key] = ext.Value;

        await WriteProblemDocumentAsync(context, exception, problemDetails, cancellationToken).ConfigureAwait(false);
    }

    private BsftExceptionMappingResult Map(HttpContext context, Exception exception, string correlationId)
    {
        var result = _mappers
            .Select(mapper => mapper.Map(exception, context, correlationId))
            .FirstOrDefault(mapped => mapped is not null)
            ?? _builtIn.Map(exception, context, correlationId);

        // A 500 is opaque to the client, whoever mapped it: the exception's text may hold internal details.
        return result.StatusCode == StatusCodes.Status500InternalServerError
            ? result with { Detail = GenericDetail }
            : result;
    }

    /// <summary>
    /// Writes the ProblemDetails document of a response whose status is already set.
    /// </summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="exception">The exception that was mapped, carried through so a host's
    /// <c>CustomizeProblemDetails</c> or a custom <see cref="IProblemDetailsWriter"/> can read it.</param>
    /// <param name="problemDetails">The document the exception mapped to.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the response is written.</returns>
    /// <remarks>
    /// When the host registered <see cref="IProblemDetailsService"/> (<c>AddProblemDetails</c>), the
    /// document is written through it, so <c>CustomizeProblemDetails</c> and a custom
    /// <see cref="IProblemDetailsWriter"/> apply to exception-mapped bodies the same way they apply to
    /// every other ProblemDetails response. A host without the service registered, or whose registered
    /// writers all decline the request (for example an <c>Accept</c> header none of them supports), gets
    /// the same direct write this middleware has always done; that fallback is also what keeps the
    /// default body shape byte-identical when nothing customises it.
    /// </remarks>
    private static async Task WriteProblemDocumentAsync(
        HttpContext context, Exception exception, ProblemDetails problemDetails, CancellationToken cancellationToken)
    {
        var problemDetailsService = context.RequestServices?.GetService<IProblemDetailsService>();
        if (problemDetailsService is not null)
        {
            var written = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = problemDetails,
                Exception = exception
            }).ConfigureAwait(false);

            if (written)
                return;
        }

        var fallbackDocument = new Dictionary<string, object?>
        {
            ["type"] = problemDetails.Type,
            ["title"] = problemDetails.Title,
            ["status"] = problemDetails.Status,
            ["detail"] = problemDetails.Detail
        };

        foreach (var ext in problemDetails.Extensions)
            fallbackDocument[ext.Key] = ext.Value;

        context.Response.ContentType = "application/problem+json";

        var json = JsonSerializer.Serialize(fallbackDocument, JsonOptions);
        await context.Response.WriteAsync(json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records the failure at the severity its status code earns.
    /// </summary>
    /// <param name="logger">Where the failure is logged.</param>
    /// <param name="exception">What the request failed with.</param>
    /// <param name="statusCode">The status the exception mapped to.</param>
    /// <param name="correlationId">The id the response carries too.</param>
    /// <remarks>
    /// Only an unmapped failure is an error. Everything else is a client reaching a rule this
    /// service states on purpose, and logging those at error level would bury the ones nobody meant
    /// to happen among them.
    /// </remarks>
    private void LogException(ILogger logger, Exception exception, int statusCode, string correlationId)
    {
        if (statusCode == StatusCodes.Status500InternalServerError)
            Report(logger, log => log.LogError(exception, "[{CorrelationId}] Unhandled exception", correlationId));
        else
            Report(logger, log => log.LogWarning(exception, "[{CorrelationId}] {ExceptionType}", correlationId, exception.GetType().Name));
    }

    // The log sink is whatever the host wired, and a sink can throw: the Windows event log does for an
    // account that may not write to it, and the default host adds that provider (it logs Warning and
    // above, so the failure shows on the first warning or error, not on the first call). Logger.Log
    // calls every provider and rethrows what they threw afterwards, so out of the catch block of the
    // middleware that exception replaces the failure being handled, and the client gets the server's
    // last-resort 500 instead of the 403, 404 or 412 that was mapped. The guard contains the failure:
    // a failing sink loses its own log line (other providers may still have written theirs) and
    // nothing else, so the response is written.
    private void Report(ILogger logger, Action<ILogger> write) => _guard.Invoke(logger, () => write(logger));
}
