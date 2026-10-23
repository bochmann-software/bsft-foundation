using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;
using Bsft.Correlation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Hosting;

/// <summary>
/// Answers a request the rate limiter rejected: 429, a <c>Retry-After</c> header and a ProblemDetails body
/// with <see cref="HostingDefaults.RateLimitedErrorCode"/>, and one count on
/// <see cref="HostingDefaults.RateLimitRejectionsInstrument"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Retry-After</c> is the rejecting limiter's own estimate (<see cref="MetadataName.RetryAfter"/>),
/// rounded up to whole seconds. A limiter without one, a concurrency limiter above all, gets the configured
/// fallback: per limiter for a named policy, which <see cref="ChainedRateLimitLease"/> already carries, and
/// <see cref="BsftRateLimitingOptions.RetryAfterSeconds"/> for any other policy.
/// </para>
/// <para>
/// The body is written through <see cref="IProblemDetailsService"/> when the host registered one, as
/// <c>BsftExceptionMiddleware</c> does, so a host's <c>CustomizeProblemDetails</c> applies here too, and
/// directly otherwise.
/// </para>
/// </remarks>
internal sealed class RateLimitRejectionWriter
{
    /// <summary>The tag value of a rejection no named policy made.</summary>
    internal const string UnnamedPolicyTag = "global";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly Counter<long> _rejections;
    private readonly TimeSpan _retryAfterFallback;

    /// <summary>Initializes a new instance of the <see cref="RateLimitRejectionWriter"/> class.</summary>
    /// <param name="rejections">The counter each rejection is added to.</param>
    /// <param name="retryAfterFallback">The <c>Retry-After</c> of a rejection whose limiter gave none.</param>
    internal RateLimitRejectionWriter(Counter<long> rejections, TimeSpan retryAfterFallback)
    {
        _rejections = rejections;
        _retryAfterFallback = retryAfterFallback;
    }

    /// <summary>Answers one rejected request; the shape of <see cref="RateLimiterOptions.OnRejected"/>.</summary>
    /// <param name="context">The rejected request and the lease that refused it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the response is written.</returns>
    internal async ValueTask WriteAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;
        var policy = httpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName
            ?? UnnamedPolicyTag;

        _rejections.Add(1, new KeyValuePair<string, object?>("policy", policy));

        var response = httpContext.Response;
        if (response.HasStarted)
            return;

        var retryAfter = RetryAfterSeconds(context.Lease);
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);

        var extensions = new Dictionary<string, object?>
        {
            ["errorCode"] = HostingDefaults.RateLimitedErrorCode,
            ["retryAfterSeconds"] = retryAfter
        };

        if (httpContext.RequestServices?.GetService<ICorrelationContext>()?.CorrelationId is { Length: > 0 } correlationId)
            extensions["correlationId"] = correlationId;

        const string title = "Too many requests.";
        var detail = string.Create(
            CultureInfo.InvariantCulture, $"The request was rate limited. Retry after {retryAfter} seconds.");

        if (await TryWriteThroughServiceAsync(httpContext, title, detail, extensions).ConfigureAwait(false))
            return;

        var document = new Dictionary<string, object?>
        {
            ["type"] = ProblemType,
            ["title"] = title,
            ["status"] = StatusCodes.Status429TooManyRequests,
            ["detail"] = detail
        };

        foreach (var extension in extensions)
            document[extension.Key] = extension.Value;

        response.ContentType = "application/problem+json";
        await response.WriteAsync(JsonSerializer.Serialize(document, JsonOptions), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The <c>type</c> of the body, the one <c>BsftExceptionMiddleware</c> uses for a status.</summary>
    private static string ProblemType => "https://httpstatuses.io/429";

    /// <summary>The seconds of the <c>Retry-After</c> header, at least one.</summary>
    /// <param name="lease">The lease that refused the request.</param>
    /// <returns>The seconds.</returns>
    private int RetryAfterSeconds(RateLimitLease lease)
    {
        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var estimate) ? estimate : _retryAfterFallback;

        return (int)Math.Clamp(Math.Ceiling(retryAfter.TotalSeconds), 1, int.MaxValue);
    }

    /// <summary>Writes the body through the host's <see cref="IProblemDetailsService"/>, when it has one.</summary>
    /// <param name="httpContext">The rejected request.</param>
    /// <param name="title">The problem's title.</param>
    /// <param name="detail">The problem's detail.</param>
    /// <param name="extensions">The members beside the RFC 9457 ones.</param>
    /// <returns><see langword="true"/> when the service wrote the body.</returns>
    private static async ValueTask<bool> TryWriteThroughServiceAsync(
        HttpContext httpContext, string title, string detail, Dictionary<string, object?> extensions)
    {
        if (httpContext.RequestServices?.GetService<IProblemDetailsService>() is not { } service)
            return false;

        var problemDetails = new ProblemDetails
        {
            Type = ProblemType,
            Title = title,
            Status = StatusCodes.Status429TooManyRequests,
            Detail = detail
        };

        foreach (var extension in extensions)
            problemDetails.Extensions[extension.Key] = extension.Value;

        return await service.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problemDetails })
            .ConfigureAwait(false);
    }
}
