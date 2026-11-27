using Microsoft.AspNetCore.Http;

namespace Bsft.Hosting;

/// <summary>
/// Told about every exception that was mapped to a response, for audit and metrics.
/// </summary>
/// <remarks>
/// Observers are told after the mapping and before the response is written, in registration order, and
/// also when the response had already started and could not be written. An observer that throws is
/// contained and reported by the <c>IObserverGuard</c>; the response is unchanged. Observers are singletons.
/// </remarks>
public interface IBsftExceptionObserver
{
    /// <summary>Tells the observer about one mapped exception.</summary>
    /// <param name="exception">What the request failed with.</param>
    /// <param name="result">What it mapped to, as it is written (a 500 already carries the generic detail).</param>
    /// <param name="context">The request that failed.</param>
    /// <param name="cancellationToken">Cancels the observation.</param>
    /// <returns>A task that completes when the observer is done.</returns>
    Task ExceptionMappedAsync(
        Exception exception, BsftExceptionMappingResult result, HttpContext context, CancellationToken cancellationToken);
}
