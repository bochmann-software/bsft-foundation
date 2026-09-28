namespace Bsft.Idempotency;

/// <summary>
/// Endpoint metadata marking an operation as one the idempotency middleware protects.
/// </summary>
/// <remarks>
/// An interface rather than only the attribute, so a minimal-API route can carry the same opt-in
/// through <c>WithMetadata</c> without a controller in sight, and so a host with a convention of its
/// own can supply it from wherever it keeps such things. The middleware asks for this interface, not
/// for the attribute, which is what makes <c>RequireIdempotency()</c> and <c>[Idempotent]</c> the
/// same opt-in rather than two.
/// </remarks>
public interface IIdempotencyMetadata
{
    /// <summary>
    /// Gets a value indicating whether a request to this endpoint without a key is refused,
    /// overriding <see cref="BsftIdempotencyOptions.ShouldRequireKey"/> for this endpoint alone.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> means "whatever the host configured", which is the usual answer. An
    /// endpoint states it only when it differs from the rest of the application — a payment call
    /// that must never run unprotected in an application whose default is permissive.
    /// </remarks>
    bool? ShouldRequireKey { get; }
}
