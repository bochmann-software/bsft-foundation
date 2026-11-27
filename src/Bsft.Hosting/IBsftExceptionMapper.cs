using Microsoft.AspNetCore.Http;

namespace Bsft.Hosting;

/// <summary>
/// Maps an exception to the response a request that failed with it receives.
/// </summary>
/// <remarks>
/// Mappers registered with <c>AddBsftExceptionMapper</c> are asked in registration order, before the
/// framework's own mappings, and the first one that returns a result wins. A mapper must not throw: it
/// runs while another failure is being answered. Mappers are singletons.
/// </remarks>
public interface IBsftExceptionMapper
{
    /// <summary>Maps one exception.</summary>
    /// <param name="exception">What the request failed with.</param>
    /// <param name="context">The request that failed.</param>
    /// <param name="correlationId">The correlation id of the request, for the extensions that carry it.</param>
    /// <returns>The mapping, or <see langword="null"/> when this mapper has no opinion on the exception.</returns>
    BsftExceptionMappingResult? Map(Exception exception, HttpContext context, string correlationId);
}
