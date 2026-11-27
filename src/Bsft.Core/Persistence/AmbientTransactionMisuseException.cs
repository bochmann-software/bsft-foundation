namespace Bsft.Core.Persistence;

/// <summary>
/// Thrown when an <see cref="IAmbientTransaction"/> is used in a way that fails the same way however
/// often it is tried: a failure inside the unit was caught and the work completed anyway, a second
/// outermost unit was started beside a running one, or a store was configured to retry on its own.
/// </summary>
/// <remarks>
/// <para>
/// A type of its own so that a retrying component can tell such a failure from a transient one and
/// leave it alone: retrying it runs the same handlers again and ends the same way, or — for a unit
/// refused beside a running one — succeeds in a fresh scope and hides the bug. The mediator's retry
/// behavior never retries it on its own account.
/// </para>
/// <para>
/// When the misuse was a swallowed failure, that failure is the inner exception. A store provider
/// that classifies it as a conflict with a concurrent transaction — a swallowed deadlock — still has
/// the whole unit retried, because the store, not the caller, ended that attempt.
/// </para>
/// <para>
/// It derives from <see cref="InvalidOperationException"/>, which is what these failures were thrown
/// as before the type existed, so a caller catching that keeps working.
/// </para>
/// </remarks>
public sealed class AmbientTransactionMisuseException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="AmbientTransactionMisuseException"/> class.</summary>
    public AmbientTransactionMisuseException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AmbientTransactionMisuseException"/> class with a
    /// message describing the misuse.
    /// </summary>
    /// <param name="message">What was done with the transaction, and what to do instead.</param>
    public AmbientTransactionMisuseException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AmbientTransactionMisuseException"/> class with a
    /// message describing the misuse and the failure that was swallowed.
    /// </summary>
    /// <param name="message">What was done with the transaction, and what to do instead.</param>
    /// <param name="innerException">The failure that was caught inside the unit.</param>
    public AmbientTransactionMisuseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
