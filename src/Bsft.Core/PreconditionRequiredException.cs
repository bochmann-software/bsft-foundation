namespace Bsft.Core;

/// <summary>
/// Exception thrown when a write requires a precondition the caller did not state, such as a
/// conditional update or delete made without an <c>If-Match</c> header.
/// </summary>
/// <remarks>
/// It exists so a handler that enforces optimistic concurrency can refuse an unconditional write
/// without <c>Bsft.Hosting</c> having to know why: the middleware maps it to
/// <c>428 Precondition Required</c>, and the framework never has to reference EF Core to turn a
/// missing validator into a status code.
/// </remarks>
public sealed class PreconditionRequiredException : BsftException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PreconditionRequiredException"/> class.
    /// </summary>
    /// <param name="message">The error message describing the precondition the caller must state.</param>
    public PreconditionRequiredException(string message)
        : base(message, "PRECONDITION_REQUIRED")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PreconditionRequiredException"/> class with a
    /// caller-supplied error code.
    /// </summary>
    /// <param name="message">The error message describing the precondition the caller must state.</param>
    /// <param name="errorCode">
    /// The machine-readable discriminator the client branches on. A missing <c>If-Match</c> and a
    /// missing <c>Idempotency-Key</c> are both 428s and are not the same refusal, so the one that is
    /// not the concurrency case names itself.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="errorCode"/> is blank.</exception>
    public PreconditionRequiredException(string message, string errorCode)
        : base(message, errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
    }
}
