using Microsoft.AspNetCore.Http;

namespace Bsft.Idempotency.Replay;

/// <summary>
/// Writes a stored response out again.
/// </summary>
internal static class IdempotencyReplayWriter
{
    /// <summary>
    /// Answers the request with the stored response, marked as a replay.
    /// </summary>
    /// <param name="response">The response to write to.</param>
    /// <param name="snapshot">What the first request answered.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the response has been written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <remarks>
    /// Only the stored headers are written, and the marker header is added on top: a replay is
    /// distinguishable from a first response, which is what lets a client tell "it ran" from "it had
    /// already run" without comparing bodies.
    /// </remarks>
    internal static async Task WriteAsync(
        HttpResponse response, IdempotencyResponseSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(snapshot);

        response.StatusCode = snapshot.StatusCode;

        foreach (var header in snapshot.Headers)
            response.Headers[header.Key] = header.Value;

        response.Headers[IdempotencyDefaults.ReplayedHeaderName] = IdempotencyDefaults.ReplayedHeaderValue;
        response.ContentLength = snapshot.Body.Length;

        await response.Body.WriteAsync(snapshot.Body, cancellationToken).ConfigureAwait(false);
    }
}
