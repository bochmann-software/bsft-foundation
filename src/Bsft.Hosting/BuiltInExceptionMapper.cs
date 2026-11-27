using Bsft.Core;
using Microsoft.AspNetCore.Http;

namespace Bsft.Hosting;

/// <summary>
/// The framework's own mapping of its exception types, and of every other exception as a generic 500.
/// It is asked last, so a registered <see cref="IBsftExceptionMapper"/> overrides it.
/// </summary>
internal sealed class BuiltInExceptionMapper : IBsftExceptionMapper
{
    /// <inheritdoc />
    public BsftExceptionMappingResult Map(Exception exception, HttpContext context, string correlationId)
    {
        var (statusCode, title, detail, extensions) = MapException(exception, correlationId);
        return new BsftExceptionMappingResult(statusCode, title, detail, extensions);
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
            BsftExceptionResponder.GenericDetail,
            new Dictionary<string, object> { ["correlationId"] = correlationId }),
        _ => (
            StatusCodes.Status500InternalServerError,
            "Internal Server Error",
            BsftExceptionResponder.GenericDetail,
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
}
