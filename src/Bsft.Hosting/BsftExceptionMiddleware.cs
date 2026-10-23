using Bsft.Core;
using Bsft.Correlation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Bsft.Hosting;

/// <summary>
/// ASP.NET Core middleware that catches <see cref="BsftException"/> subtypes and unhandled exceptions,
/// mapping them to RFC 9457 ProblemDetails responses with appropriate HTTP status codes.
/// </summary>
public sealed class BsftExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<BsftExceptionMiddleware> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

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
            await HandleExceptionAsync(context, ex, correlationContext.CorrelationId).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Maps one exception to a response, logs it, and writes it, unless the response is already on the wire.
    /// </summary>
    /// <param name="context">The request that failed.</param>
    /// <param name="exception">What it failed with.</param>
    /// <param name="correlationId">The id the response and the log line are both stamped with.</param>
    /// <returns>A task that completes when the response is written.</returns>
    private async Task HandleExceptionAsync(HttpContext context, Exception exception, string correlationId)
    {
        var (statusCode, title, detail, extensions) = MapException(exception, correlationId);

        LogException(exception, statusCode, correlationId);

        if (context.Response.HasStarted)
        {
            // Headers/body are already on the wire; setting the status now would throw. Log and bail.
            _logger.LogWarning(
                "Response already started; cannot write ProblemDetails for status {StatusCode}.", statusCode);
            return;
        }

        context.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Type = $"https://httpstatuses.io/{statusCode}",
            Title = title,
            Status = statusCode,
            Detail = detail
        };

        foreach (var ext in extensions)
            problemDetails.Extensions[ext.Key] = ext.Value;

        await WriteProblemDocumentAsync(context, exception, problemDetails).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the ProblemDetails document of a response whose status is already set.
    /// </summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="exception">The exception that was mapped, carried through so a host's
    /// <c>CustomizeProblemDetails</c> or a custom <see cref="IProblemDetailsWriter"/> can read it.</param>
    /// <param name="problemDetails">The document the exception mapped to.</param>
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
        HttpContext context, Exception exception, ProblemDetails problemDetails)
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
        await context.Response.WriteAsync(json).ConfigureAwait(false);
    }

    private static (int StatusCode, string Title, string Detail, Dictionary<string, object> Extensions) MapException(
        Exception exception, string correlationId) => exception switch
    {
        // Every mapped arm surfaces the exception's ErrorCode as `errorCode`, so a client has a
        // machine-readable discriminator within a status: two 422s, or two 409s, that a human reads
        // apart by their detail are told apart in code by their code. The property pattern binds the
        // code as non-null; an arm's exception whose code was somehow null would fall through to the
        // generic 500 below rather than emit a null discriminator. The 500 arm carries no errorCode
        // on purpose — an unmapped framework error is opaque to the client but for its correlation id.
        ValidationException { ErrorCode: { } errorCode } validationEx => (
            StatusCodes.Status400BadRequest,
            "Validation Error",
            validationEx.Message,
            new Dictionary<string, object>
            {
                ["errors"] = validationEx.Errors,
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId
            }),
        NotFoundException { ErrorCode: { } errorCode } notFoundEx => (
            StatusCodes.Status404NotFound,
            "Not Found",
            notFoundEx.Message,
            new Dictionary<string, object>
            {
                ["entityType"] = notFoundEx.EntityType,
                ["entityId"] = notFoundEx.EntityId,
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId
            }),
        ForbiddenException { ErrorCode: { } errorCode } => (
            StatusCodes.Status403Forbidden,
            "Forbidden",
            exception.Message,
            new Dictionary<string, object> { ["errorCode"] = errorCode, ["correlationId"] = correlationId }),
        PreconditionRequiredException { ErrorCode: { } errorCode } => (
            StatusCodes.Status428PreconditionRequired,
            "Precondition Required",
            exception.Message,
            new Dictionary<string, object> { ["errorCode"] = errorCode, ["correlationId"] = correlationId }),
        PreconditionFailedException { ErrorCode: { } errorCode } => (
            StatusCodes.Status412PreconditionFailed,
            "Precondition Failed",
            exception.Message,
            new Dictionary<string, object> { ["errorCode"] = errorCode, ["correlationId"] = correlationId }),
        ConflictException { ErrorCode: { } errorCode } conflictEx => (
            StatusCodes.Status409Conflict,
            "Conflict",
            exception.Message,
            Named(errorCode, correlationId, conflictEx.ResourceType, conflictEx.ResourceId)),
        PayloadTooLargeException { ErrorCode: { } errorCode } => (
            StatusCodes.Status413PayloadTooLarge,
            "Payload Too Large",
            exception.Message,
            new Dictionary<string, object> { ["errorCode"] = errorCode, ["correlationId"] = correlationId }),
        PaymentRequiredException { ErrorCode: { } errorCode } => (
            StatusCodes.Status402PaymentRequired,
            "Payment Required",
            exception.Message,
            new Dictionary<string, object> { ["errorCode"] = errorCode, ["correlationId"] = correlationId }),
        BusinessRuleException { ErrorCode: { } errorCode } businessRuleEx => (
            StatusCodes.Status422UnprocessableEntity,
            "Business Rule Violation",
            exception.Message,
            RetryAware(errorCode, correlationId, businessRuleEx.RetryAfter)),
        BsftException => (
            StatusCodes.Status500InternalServerError,
            "Internal Server Error",
            // Do not leak an unmapped framework exception's message to the client (it may carry
            // internal details); surface a generic message and rely on the correlation id instead.
            "An unexpected error occurred. Please use the correlation ID for troubleshooting.",
            new Dictionary<string, object> { ["correlationId"] = correlationId }),
        _ => (
            StatusCodes.Status500InternalServerError,
            "Internal Server Error",
            "An unexpected error occurred. Please use the correlation ID for troubleshooting.",
            new Dictionary<string, object> { ["correlationId"] = correlationId })
    };

    /// <summary>
    /// The extensions of a conflict, carrying the resource the thrower named as the one collided with.
    /// </summary>
    /// <param name="errorCode">The machine-readable discriminator.</param>
    /// <param name="correlationId">The correlation id of the request.</param>
    /// <param name="resourceType">The kind of resource collided with, or <see langword="null"/>.</param>
    /// <param name="resourceId">The identifier of that resource, or <see langword="null"/>.</param>
    /// <returns>The problem document's extension members.</returns>
    /// <remarks>
    /// A 409 whose only machine-readable content is its code tells a client that something collided but
    /// not with what, leaving it to parse the detail prose for an identifier. The pair is mapped the
    /// same way the not-found arm maps its entity, and both members are absent — not null — where the
    /// thrower named no resource.
    /// </remarks>
    private static Dictionary<string, object> Named(
        string errorCode, string correlationId, string? resourceType, object? resourceId)
    {
        var extensions = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["errorCode"] = errorCode,
            ["correlationId"] = correlationId
        };

        if (resourceType is { } type)
            extensions["resourceType"] = type;

        if (resourceId is { } id)
            extensions["resourceId"] = id;

        return extensions;
    }

    /// <summary>
    /// The extensions of a business-rule refusal, carrying the instant it lifts when the rule stated one.
    /// </summary>
    /// <param name="errorCode">The machine-readable discriminator.</param>
    /// <param name="correlationId">The correlation id of the request.</param>
    /// <param name="retryAfter">The instant the refusal lifts, or <see langword="null"/>.</param>
    /// <returns>The problem document's extension members.</returns>
    /// <remarks>
    /// A temporary refusal that does not say when it ends reads to a client exactly like a permanent
    /// one, so the deadline is surfaced where a client can act on it rather than left in the prose of
    /// the detail. The member is absent — not null — where the rule named no deadline.
    /// </remarks>
    private static Dictionary<string, object> RetryAware(
        string errorCode, string correlationId, DateTimeOffset? retryAfter)
    {
        var extensions = new Dictionary<string, object>
        {
            ["errorCode"] = errorCode,
            ["correlationId"] = correlationId
        };

        if (retryAfter is { } instant)
            extensions["retryAfter"] = instant;

        return extensions;
    }

    /// <summary>
    /// Records the failure at the severity its status code earns.
    /// </summary>
    /// <param name="exception">What the request failed with.</param>
    /// <param name="statusCode">The status the exception mapped to.</param>
    /// <param name="correlationId">The id the response carries too.</param>
    /// <remarks>
    /// Only an unmapped failure is an error. Everything else is a client reaching a rule this
    /// service states on purpose, and logging those at error level would bury the ones nobody meant
    /// to happen among them.
    /// </remarks>
    private void LogException(Exception exception, int statusCode, string correlationId)
    {
        if (statusCode == StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "[{CorrelationId}] Unhandled exception", correlationId);
        else
            _logger.LogWarning(exception, "[{CorrelationId}] {ExceptionType}", correlationId, exception.GetType().Name);
    }

}
