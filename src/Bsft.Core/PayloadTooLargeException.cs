namespace Bsft.Core;

/// <summary>
/// Exception thrown when a request body is larger than the bound a feature is willing to read it
/// under, so the feature refuses the request instead of proceeding without the guarantee the body
/// would have bought.
/// </summary>
/// <remarks>
/// It exists for the case where reading the body is itself the protection: an idempotency
/// fingerprint has to hash the body, and a body it will not buffer is a request it cannot recognise
/// as a duplicate. Refusing loudly with <c>413 Payload Too Large</c> is the honest answer, because
/// the alternative — letting the request through unprotected — looks identical to success and fails
/// only when the duplicate arrives.
/// </remarks>
public sealed class PayloadTooLargeException : BsftException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadTooLargeException"/> class.
    /// </summary>
    /// <param name="message">The error message describing the bound that was exceeded.</param>
    public PayloadTooLargeException(string message)
        : base(message, "PAYLOAD_TOO_LARGE")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadTooLargeException"/> class with a
    /// caller-supplied error code.
    /// </summary>
    /// <param name="message">The error message describing the bound that was exceeded.</param>
    /// <param name="errorCode">The machine-readable discriminator the client branches on.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="errorCode"/> is blank.</exception>
    public PayloadTooLargeException(string message, string errorCode)
        : base(message, errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
    }
}
