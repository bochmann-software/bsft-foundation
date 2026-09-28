using Bsft.Core;
using Microsoft.AspNetCore.Http;
using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;

namespace Bsft.Idempotency.Fingerprinting;

/// <summary>
/// Digests a request body without consuming it, so the endpoint behind the middleware still reads
/// the body it was sent.
/// </summary>
internal static class RequestBodyDigest
{
    /// <summary>The read buffer. Large enough that an ordinary JSON body is one or two reads.</summary>
    private const int BufferSize = 8192;

    /// <summary>
    /// Computes the SHA-256 of the request body, rewinding it afterwards.
    /// </summary>
    /// <param name="request">The request whose body to digest.</param>
    /// <param name="maxBytes">The largest body that will be read.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The digest as 64 lowercase hexadecimal characters.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    /// <exception cref="PayloadTooLargeException">
    /// Thrown when the body is larger than <paramref name="maxBytes"/>. Refusing is deliberate:
    /// a body that is not hashed is an endpoint that is not protected, and letting it through would
    /// look exactly like success until the duplicate arrived.
    /// </exception>
    internal static async ValueTask<string> ComputeAsync(
        HttpRequest request, long maxBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The declared length settles it without reading a byte, which is the cheap half of the
        // bound. It is only a claim, so the read below still counts.
        if (request.ContentLength > maxBytes)
            throw TooLarge(maxBytes);

        // Buffering is what lets the body be read twice; without it the endpoint would find an
        // exhausted stream and bind an empty model.
        request.EnableBuffering();
        request.Body.Position = 0;

        var digest = await ReadAndHashAsync(request.Body, maxBytes, cancellationToken).ConfigureAwait(false);

        request.Body.Position = 0;

        return digest;
    }

    /// <summary>Reads the stream to its end, hashing as it goes and refusing to read past the bound.</summary>
    /// <param name="body">The body stream, positioned at its start.</param>
    /// <param name="maxBytes">The largest body that will be read.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The digest as 64 lowercase hexadecimal characters.</returns>
    private static async ValueTask<string> ReadAndHashAsync(Stream body, long maxBytes, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            long total = 0;
            int read;

            while ((read = await body.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;

                if (total > maxBytes)
                    throw TooLarge(maxBytes);

                hash.AppendData(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>Builds the refusal, so both places that raise it word it identically.</summary>
    /// <param name="maxBytes">The bound that was exceeded.</param>
    /// <returns>The exception to throw.</returns>
    private static PayloadTooLargeException TooLarge(long maxBytes) =>
        new(
            string.Format(
                CultureInfo.InvariantCulture,
                "The request body is larger than the {0} bytes an idempotent request may carry, so it "
                + "cannot be fingerprinted and the endpoint cannot be protected against a duplicate.",
                maxBytes),
            IdempotencyDefaults.PayloadTooLargeErrorCode);
}
