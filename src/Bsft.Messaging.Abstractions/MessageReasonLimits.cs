namespace Bsft.Messaging;

/// <summary>
/// The bounds every part of the pipeline cuts a failure or park reason at.
/// </summary>
/// <remarks>
/// <para>
/// A reason is derived from an exception and therefore unbounded: it can quote the event that failed.
/// Both places a reason ends up are bounded, though — the outbox writes it into a column of fixed
/// width, and a park writes it into an AMQP header inside a frame of finite size — so it is cut before
/// it is stored either way.
/// </para>
/// <para>
/// The number lives here, in the abstractions, because the two sides of the pipeline that cut it never
/// see each other: the outbox store is an EF Core package and the park header is written by a transport
/// adapter. Two constants that merely happened to agree would drift apart the first time one of them was
/// tuned, and the symptom — the same exception message truncated at two different points depending on
/// which way the message failed — is not one anybody would trace back to a constant.
/// </para>
/// </remarks>
public static class MessageReasonLimits
{
    /// <summary>
    /// The greatest number of characters of a failure or park reason that is stored.
    /// </summary>
    /// <remarks>
    /// A reason longer than this is cut rather than rejected: losing the tail of a diagnostic message
    /// is preferable to losing the record that the failure happened at all. The cut never splits a
    /// surrogate pair, so what is stored is always well-formed text.
    /// </remarks>
    public const int MaxReasonLength = 1000;
}
