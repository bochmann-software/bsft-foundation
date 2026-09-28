namespace Bsft.Core;

/// <summary>
/// Exception thrown when a business rule is violated.
/// </summary>
public sealed class BusinessRuleException : BsftException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BusinessRuleException"/> class.
    /// </summary>
    /// <param name="message">The error message describing the violated business rule.</param>
    /// <param name="errorCode">An optional machine-readable error code.</param>
    public BusinessRuleException(string message, string? errorCode = null)
        : base(message, errorCode ?? "BUSINESS_RULE_VIOLATED")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BusinessRuleException"/> class for a rule that
    /// refuses only until a known instant.
    /// </summary>
    /// <param name="message">The error message describing the violated business rule.</param>
    /// <param name="errorCode">The machine-readable error code.</param>
    /// <param name="retryAfter">The instant the rule stops refusing.</param>
    public BusinessRuleException(string message, string? errorCode, DateTimeOffset retryAfter)
        : this(message, errorCode)
        => RetryAfter = retryAfter;

    /// <summary>
    /// Gets the instant the refusal lifts, or <see langword="null"/> where the rule states no deadline.
    /// </summary>
    /// <remarks>
    /// A temporary refusal — a lockout, a cooling-off window — is the same 422 as a permanent one to a
    /// client that cannot see when it ends, and the caller that renders "try again at" is the one that
    /// caught the exception. It travels here rather than in the message so that it survives without
    /// being parsed out of prose, and <c>BsftExceptionMiddleware</c> surfaces it as the
    /// <c>retryAfter</c> member of the problem document.
    /// </remarks>
    public DateTimeOffset? RetryAfter { get; }
}
