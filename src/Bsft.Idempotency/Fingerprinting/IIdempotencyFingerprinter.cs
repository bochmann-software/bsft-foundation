using Microsoft.AspNetCore.Http;

namespace Bsft.Idempotency.Fingerprinting;

/// <summary>
/// Reduces a request to the two digests the store is keyed and checked on.
/// </summary>
internal interface IIdempotencyFingerprinter
{
    /// <summary>
    /// Computes the scope and the fingerprint of a request, buffering its body so the endpoint can
    /// still read it.
    /// </summary>
    /// <param name="context">The request being fingerprinted.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The caller digest and the request digest.</returns>
    ValueTask<IdempotencyFingerprintResult> ComputeAsync(HttpContext context, CancellationToken cancellationToken = default);
}
