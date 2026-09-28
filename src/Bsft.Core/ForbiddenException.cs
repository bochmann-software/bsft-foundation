namespace Bsft.Core;

/// <summary>
/// Exception thrown when access to a resource is forbidden.
/// </summary>
public sealed class ForbiddenException : BsftException
{
    /// <summary>The code every instance carries unless a caller names its own.</summary>
    private const string DefaultErrorCode = "FORBIDDEN";

    /// <summary>
    /// Initializes a new instance of the <see cref="ForbiddenException"/> class.
    /// </summary>
    /// <param name="message">The error message describing why access was denied.</param>
    public ForbiddenException(string message)
        : base(message, DefaultErrorCode)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ForbiddenException"/> class, wrapping the
    /// exception that caused access to be refused.
    /// </summary>
    /// <param name="message">The error message describing why access was denied.</param>
    /// <param name="innerException">The exception the refusal was raised from.</param>
    public ForbiddenException(string message, Exception innerException)
        : base(message, innerException, DefaultErrorCode)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ForbiddenException"/> class with a caller-supplied
    /// error code.
    /// </summary>
    /// <param name="message">The error message describing why access was denied.</param>
    /// <param name="errorCode">
    /// The machine-readable discriminator the client branches on. A family with more than one way of
    /// refusing — a separation-of-duties rule that names which of three rules refused the decision,
    /// for instance — states it rather than sharing the generic <c>FORBIDDEN</c>, because a client
    /// that has to tell "you raised this yourself" from "you already decided an earlier step" cannot
    /// do it by reading prose.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="errorCode"/> is blank.</exception>
    public ForbiddenException(string message, string errorCode)
        : base(message, errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
    }
}
