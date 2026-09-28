namespace Bsft.Http.Egress;

/// <summary>
/// What the egress policy decided about a destination, and why it decided it.
/// </summary>
/// <remarks>
/// <para>
/// The reason exists so a caller can put the rule that refused into its own diagnostics without
/// inventing a second vocabulary for it. It names the rule and never the address: the text travels
/// into logs that a caller may hand on, and the address a name resolved to is not something to
/// disclose to whoever supplied the name.
/// </para>
/// <para>
/// A record rather than a class: it carries a verdict and the words for it and nothing else, so two
/// decisions that say the same thing are the same decision, and a test comparing one against another
/// says what it means without reaching into the members one at a time.
/// </para>
/// </remarks>
public sealed record OutboundAddressDecision
{
    /// <summary>Initializes a new instance of the <see cref="OutboundAddressDecision"/> record.</summary>
    /// <param name="isAllowed">Whether the destination may be called.</param>
    /// <param name="reason">Why it may not, when it may not.</param>
    private OutboundAddressDecision(bool isAllowed, string reason)
    {
        IsAllowed = isAllowed;
        Reason = reason;
    }

    /// <summary>Gets the decision every destination that passed every rule carries.</summary>
    public static OutboundAddressDecision Allowed { get; } = new(isAllowed: true, string.Empty);

    /// <summary>Gets a value indicating whether the destination may be called.</summary>
    public bool IsAllowed { get; }

    /// <summary>Gets why the destination was refused, and the empty string when it was not.</summary>
    /// <remarks>
    /// It is never null on a refusal, and the constructor being private is what makes that hold: a
    /// refusal can only come from <see cref="Denied"/>, which rejects a blank reason. A caller that
    /// reports a refusal therefore always has something to report.
    /// </remarks>
    public string Reason { get; }

    /// <summary>Builds a refusal.</summary>
    /// <param name="reason">Why the destination was refused. It names the rule, never the address.</param>
    /// <returns>The refusal.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason"/> is null or blank.</exception>
    public static OutboundAddressDecision Denied(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new OutboundAddressDecision(isAllowed: false, reason);
    }
}
