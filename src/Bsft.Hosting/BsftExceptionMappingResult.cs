namespace Bsft.Hosting;

/// <summary>
/// What an exception maps to: the status, the ProblemDetails members and the response headers of the
/// answer a request that failed with it receives.
/// </summary>
/// <param name="StatusCode">The HTTP status code of the response.</param>
/// <param name="Title">The <c>title</c> member.</param>
/// <param name="Detail">The <c>detail</c> member, or <see langword="null"/> to leave it out.</param>
/// <param name="Extensions">Extension members written beside the standard ones, or <see langword="null"/> for none.</param>
/// <param name="Headers">Response headers set before the body is written (<c>Retry-After</c>, for one), or <see langword="null"/> for none.</param>
/// <param name="Type">The <c>type</c> member, or <see langword="null"/> for <c>https://httpstatuses.io/{StatusCode}</c>.</param>
/// <remarks>
/// A mapping with status 500 never carries the text of the exception: the responder replaces its
/// <paramref name="Detail"/> with the generic message, so a mapper that names the 500 cannot publish
/// what the exception said by accident. Use another 5xx status where a client should read the detail.
/// </remarks>
public sealed record BsftExceptionMappingResult(
    int StatusCode,
    string Title,
    string? Detail = null,
    IReadOnlyDictionary<string, object>? Extensions = null,
    IReadOnlyDictionary<string, string>? Headers = null,
    string? Type = null);
