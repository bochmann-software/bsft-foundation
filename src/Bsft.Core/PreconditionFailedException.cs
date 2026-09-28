namespace Bsft.Core;

/// <summary>
/// Exception thrown when a caller's precondition no longer holds, such as an <c>If-Match</c> that
/// names a version of the resource another writer has already replaced.
/// </summary>
/// <remarks>
/// It is the framework's own translation of an optimistic-concurrency conflict, thrown by a handler
/// in place of the provider-specific <c>DbUpdateConcurrencyException</c> so that <c>Bsft.Hosting</c>
/// can map a stale write to <c>412 Precondition Failed</c> without referencing EF Core.
/// </remarks>
public sealed class PreconditionFailedException : BsftException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PreconditionFailedException"/> class.
    /// </summary>
    /// <param name="message">The error message describing the precondition that no longer holds.</param>
    public PreconditionFailedException(string message)
        : base(message, "PRECONDITION_FAILED")
    {
    }
}
