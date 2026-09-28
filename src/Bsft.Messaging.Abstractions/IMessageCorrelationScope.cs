namespace Bsft.Messaging;

/// <summary>
/// Carries the correlation id of the message currently being handled through the asynchronous flow
/// that handles it, so components resolved from the delivery's own service scope pick it up instead of
/// minting a fresh one.
/// </summary>
/// <remarks>
/// <para>
/// The ambient value flows with the asynchronous control flow, so a single registration has to be
/// shared by the writer and every reader; the messaging registration therefore adds it as a singleton.
/// </para>
/// <para>
/// Writing the ambient id is a transport concern: a bus restores it around the handler invocation so
/// that whatever the handler stages inherits it. Application code reads it, through
/// <see cref="IMessageCorrelationAccessor"/> rather than from here.
/// </para>
/// </remarks>
public interface IMessageCorrelationScope
{
    /// <summary>Gets the correlation id of the message currently being handled, if any.</summary>
    string? Current { get; }

    /// <summary>
    /// Sets the ambient correlation id for the current asynchronous flow.
    /// </summary>
    /// <param name="correlationId">The correlation id to handle under, or <c>null</c> for none.</param>
    /// <returns>A handle that restores the previous value when disposed.</returns>
    IDisposable Use(string? correlationId);
}
