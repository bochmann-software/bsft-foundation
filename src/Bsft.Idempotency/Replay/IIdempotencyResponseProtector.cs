namespace Bsft.Idempotency.Replay;

/// <summary>
/// Seals the stored response body of an endpoint marked <see cref="SensitiveIdempotentResponseAttribute"/>,
/// and decides what of a stored body may be replayed.
/// </summary>
internal interface IIdempotencyResponseProtector
{
    /// <summary>Seals a response body before it is stored.</summary>
    /// <param name="body">The body as it was sent.</param>
    /// <param name="scope">The caller digest the record is stored under, which the ciphertext is bound to.</param>
    /// <param name="key">The idempotency key of the record, which the ciphertext is bound to.</param>
    /// <returns>
    /// The body encrypted and marked as sealed, or <see langword="null"/> when it cannot be sealed, in
    /// which case nothing is stored and the key may run again.
    /// </returns>
    byte[]? Protect(byte[] body, string scope, string key);

    /// <summary>Turns a stored body into the one to replay, or into nothing.</summary>
    /// <param name="storedBody">The body the store holds.</param>
    /// <param name="isSensitive">Whether the endpoint is marked as returning a secret.</param>
    /// <param name="scope">The caller digest of the record being replayed.</param>
    /// <param name="key">The idempotency key of the record being replayed.</param>
    /// <returns>
    /// The body to write, unsealed when it was sealed; or <see langword="null"/> when it is sealed and
    /// cannot be unsealed (including a body sealed for another record), or when the endpoint is sensitive
    /// and the body was stored unsealed.
    /// </returns>
    byte[]? FindReplayableBody(byte[] storedBody, bool isSensitive, string scope, string key);
}
