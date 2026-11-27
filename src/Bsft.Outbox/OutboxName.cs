namespace Bsft.Outbox;

/// <summary>
/// Records under which name an outbox of one payload type was registered, so a second registration of
/// the same payload under another name can be refused where it matters.
/// </summary>
/// <typeparam name="TPayload">What the rows of the outbox carry.</typeparam>
/// <remarks>
/// The drain pass is registered once per payload type and bound to the options of the first name, so
/// the second name's options would never be read. A hosted worker per name would then drain one outbox
/// twice under the settings of the other.
/// </remarks>
internal sealed class OutboxName<TPayload>
{
    /// <summary>Initializes a new instance of the <see cref="OutboxName{TPayload}"/> class.</summary>
    /// <param name="value">The name the outbox was registered under.</param>
    public OutboxName(string value) => Value = value;

    /// <summary>Gets the name the outbox was registered under.</summary>
    public string Value { get; }
}
