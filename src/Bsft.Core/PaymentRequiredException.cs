namespace Bsft.Core;

/// <summary>
/// Exception thrown when a request is well formed and permitted, and is refused only because the
/// caller's plan does not carry the entitlement it needs or has exhausted the quota it draws on.
/// </summary>
/// <remarks>
/// <para>
/// It is deliberately not a <see cref="BusinessRuleException"/>. A 422 says the resource's own state
/// forbids the request, and a quota refusal is not about the resource at all: the same request, from
/// the same caller, against the same resource, succeeds on a larger plan or after the period rolls
/// over. <c>402 Payment Required</c> says exactly that, and it is the status a client can branch on
/// to offer an upgrade rather than to report a defect.
/// </para>
/// <para>
/// The status is also the one the estate already emits. It is therefore an alignment rather than a
/// new vocabulary: a client that already treats 402 as "offer the upgrade path" keeps working.
/// </para>
/// <para>
/// The error code is always caller-supplied, because a refusal at this status is never
/// self-explanatory — "which entitlement" and "which meter" are the whole content of the answer, and
/// a single fixed code would force every client to parse the message to find out.
/// </para>
/// </remarks>
public sealed class PaymentRequiredException : BsftException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PaymentRequiredException"/> class.
    /// </summary>
    /// <param name="message">
    /// The error message, which should name the entitlement or meter, its limit and when it resets.
    /// </param>
    /// <param name="errorCode">The machine-readable discriminator the client branches on.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="errorCode"/> is blank.</exception>
    public PaymentRequiredException(string message, string errorCode)
        : base(message, errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
    }
}
