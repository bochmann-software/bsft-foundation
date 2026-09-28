namespace Bsft.Idempotency;

/// <summary>
/// The constants a host, a client library or a test needs to talk about idempotent requests without
/// referencing anything that runs.
/// </summary>
/// <remarks>
/// A client assembly needs nothing from this package but a header name, which is why these are
/// <see langword="const"/> and why the family has no <c>.Abstractions</c> sibling: there is no
/// consumer that needs the contract without the implementation.
/// </remarks>
public static class IdempotencyDefaults
{
    /// <summary>
    /// The request header carrying the client's key. Spelled as Stripe, the IETF draft and every
    /// generic client already spell it, because a header nobody recognises is a header nobody sends.
    /// </summary>
    public const string HeaderName = "Idempotency-Key";

    /// <summary>
    /// The response header set on a replay, so a client can tell "your request ran" from "your
    /// request had already run" without comparing bodies.
    /// </summary>
    public const string ReplayedHeaderName = "Idempotency-Replayed";

    /// <summary>The value <see cref="ReplayedHeaderName"/> carries. It is only ever set on a replay.</summary>
    public const string ReplayedHeaderValue = "true";

    /// <summary>
    /// The <c>errorCode</c> of the 428 an opted-in endpoint answers when the key is required and
    /// absent. It is not <c>PRECONDITION_REQUIRED</c>, because a missing <c>If-Match</c> is a 428 too
    /// and the two are different things for the client to fix.
    /// </summary>
    public const string KeyRequiredErrorCode = "IDEMPOTENCY_KEY_REQUIRED";

    /// <summary>
    /// The <c>errorCode</c> of the 409 answered when a key is presented again with a different
    /// request. Serving the first response would answer a question the client did not ask.
    /// </summary>
    public const string KeyReusedErrorCode = "IDEMPOTENCY_KEY_REUSED";

    /// <summary>
    /// The <c>errorCode</c> of the 409 answered while the first request under this key is still
    /// running. It carries <c>Retry-After</c>; blocking instead would hold a connection and a row for
    /// the duration of somebody else's request.
    /// </summary>
    public const string InProgressErrorCode = "IDEMPOTENCY_IN_PROGRESS";

    /// <summary>
    /// The <c>errorCode</c> of the 413 answered when the body is larger than the fingerprint is
    /// willing to buffer. Refusing is deliberate: letting it through would look like success and
    /// leave the endpoint unprotected.
    /// </summary>
    public const string PayloadTooLargeErrorCode = "IDEMPOTENCY_PAYLOAD_TOO_LARGE";

    /// <summary>The key of the <c>errors</c> entry a malformed key is reported under.</summary>
    public const string KeyValidationField = "idempotencyKey";

    /// <summary>
    /// The largest <see cref="BsftIdempotencyOptions.MaxKeyLength"/> the framework's own stores
    /// accept, and the width of the shared store's key column.
    /// </summary>
    /// <remarks>
    /// One number rather than two, so a host cannot configure a key length the shared table refuses.
    /// A key longer than the column is a failed write somebody sees, never a truncation that would
    /// silently merge two keys into one and replay the wrong response.
    /// </remarks>
    public const int MaxSupportedKeyLength = 512;
}
