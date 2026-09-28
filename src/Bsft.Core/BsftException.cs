namespace Bsft.Core;

/// <summary>
/// Base exception for all BSFT framework exceptions.
/// Carries an optional error code for machine-readable error identification.
/// </summary>
/// <remarks>
/// This type is designed for inheritance: an application derives its own exception from it so that
/// <c>BsftExceptionMiddleware</c> recognises the failure and the error code travels to the client.
/// The framework's own exceptions below it are sealed; only this base is open.
/// </remarks>
public class BsftException : Exception
{
    /// <summary>
    /// Gets the machine-readable error code associated with this exception.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BsftException"/> class with no message.
    /// </summary>
    /// <remarks>
    /// Present because the framework's exception contract expects the standard set, and because a
    /// serializer or a test double may need to construct one without a message. Application code
    /// should prefer an overload that says what went wrong.
    /// </remarks>
    public BsftException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BsftException"/> class with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public BsftException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BsftException"/> class with a message and the
    /// exception that caused it.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception this one was raised from.</param>
    public BsftException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BsftException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">An optional machine-readable error code.</param>
    public BsftException(string message, string? errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BsftException"/> class with an inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    /// <param name="errorCode">An optional machine-readable error code.</param>
    public BsftException(string message, Exception innerException, string? errorCode)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
