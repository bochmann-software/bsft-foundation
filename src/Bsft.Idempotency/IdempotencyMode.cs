namespace Bsft.Idempotency;

/// <summary>
/// How the reservation of an idempotency key relates to the transaction the endpoint itself commits.
/// The choice decides exactly one thing — what a crash between the two leaves behind — and the two
/// answers are stated rather than blurred.
/// </summary>
public enum IdempotencyMode
{
    /// <summary>
    /// The reservation is written and completed on its own connection, independently of whatever the
    /// endpoint does. Works with any store, needs no <c>DbContext</c>, and is the default.
    /// </summary>
    /// <remarks>
    /// The honest limit: a process torn down between the reservation and the endpoint's own commit
    /// leaves a reserved key with no business row behind it. The key becomes retryable again after
    /// <see cref="BsftIdempotencyOptions.InProgressTimeout"/>, so nothing is lost permanently, but for
    /// that window the client is told to wait for work that will never finish.
    /// </remarks>
    Detached = 0,

    /// <summary>
    /// The reservation is staged in the host's own unit of work and commits with the endpoint's
    /// <c>SaveChanges</c>, so the record and the business row are one atomic outcome.
    /// </summary>
    /// <remarks>
    /// Requires a store that can enlist — <c>Bsft.Idempotency.EFCore</c> over the application's own
    /// context. The cost is the dependency: a host that only wants the double-click fixed does not
    /// need it, which is why it is not the default.
    /// </remarks>
    Enlisted = 1,
}
