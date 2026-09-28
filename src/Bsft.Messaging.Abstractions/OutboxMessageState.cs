namespace Bsft.Messaging;

/// <summary>
/// Where an outbox row stands, as an operator asks about it.
/// </summary>
/// <remarks>
/// Derived from the row's timestamps rather than stored: the dispatcher decides what to claim by
/// asking whether a row is published and whether it is retired, and a second column holding the same
/// answer is a column that can disagree with them.
/// </remarks>
public enum OutboxMessageState
{
    /// <summary>Not yet published and not retired: the dispatcher will pick it up.</summary>
    Pending,

    /// <summary>Published. Nothing further happens to it.</summary>
    Dispatched,

    /// <summary>
    /// Retired after exhausting its attempts. This is the state that needs a person, and the reason
    /// this reader exists.
    /// </summary>
    DeadLettered
}
