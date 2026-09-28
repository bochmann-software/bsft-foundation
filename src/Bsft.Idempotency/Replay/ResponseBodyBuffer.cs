using Microsoft.AspNetCore.Http;

namespace Bsft.Idempotency.Replay;

/// <summary>
/// Holds a response in memory while the endpoint produces it, so it can be stored before it goes out.
/// </summary>
/// <remarks>
/// <para>
/// A response that has already started cannot be stored — the bytes are gone and the status can no
/// longer be read reliably — so the only way to record what a request answered is to stand between
/// the endpoint and the socket. It is also what keeps <c>BsftExceptionMiddleware</c>'s
/// "response already started" guard meaningful for a protected endpoint.
/// </para>
/// <para>
/// The honest cost: an endpoint that streams its response is buffered whole, so a download or a
/// server-sent-event stream must not be marked idempotent. That is stated on the topic page rather
/// than guessed at from a heuristic here.
/// </para>
/// </remarks>
internal sealed class ResponseBodyBuffer : IDisposable
{
    private readonly HttpResponse _response;
    private readonly Stream _originalBody;
    private readonly MemoryStream _buffer = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ResponseBodyBuffer"/> class and diverts the
    /// response into memory.
    /// </summary>
    /// <param name="response">The response to divert.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response"/> is null.</exception>
    public ResponseBodyBuffer(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        _response = response;
        _originalBody = response.Body;
        response.Body = _buffer;
    }

    /// <summary>
    /// Gets how many bytes the endpoint has written so far.
    /// </summary>
    /// <remarks>
    /// Readable without taking the snapshot, so the decision whether a response is worth storing can
    /// be taken before its body is copied out of the buffer.
    /// </remarks>
    public long Length => _buffer.Length;

    /// <summary>
    /// Takes what the endpoint produced: the status, the allowlisted headers and the bytes.
    /// </summary>
    /// <param name="allowedHeaders">The header names that may be stored and replayed.</param>
    /// <returns>The snapshot to store.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="allowedHeaders"/> is null.</exception>
    public IdempotencyResponseSnapshot Capture(IEnumerable<string> allowedHeaders)
    {
        ArgumentNullException.ThrowIfNull(allowedHeaders);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in allowedHeaders)
        {
            if (_response.Headers.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value))
                headers[name] = value.ToString();
        }

        return new IdempotencyResponseSnapshot(_response.StatusCode, headers, _buffer.ToArray());
    }

    /// <summary>
    /// Puts the real response body back and writes what was buffered to it.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the bytes are on their way out.</returns>
    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        _response.Body = _originalBody;
        _buffer.Position = 0;

        await _buffer.CopyToAsync(_originalBody, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Restoring here as well as in ReleaseAsync, so a request that threw leaves the pipeline the
        // way it found it and the exception middleware writes to the real response.
        _response.Body = _originalBody;
        _buffer.Dispose();
    }
}
