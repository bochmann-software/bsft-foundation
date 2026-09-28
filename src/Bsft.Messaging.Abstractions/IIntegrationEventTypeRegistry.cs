namespace Bsft.Messaging;

/// <summary>
/// Maps integration event types to the stable type names that are written to the outbox and back.
/// </summary>
/// <remarks>
/// The mapping is explicit rather than derived from whatever type name a stored row happens to carry.
/// Resolving an arbitrary name from a database row into a CLR type would let anything able to write a
/// row decide which type the dispatcher instantiates, and it would silently break the moment a type
/// is renamed or moved to another assembly.
/// </remarks>
public interface IIntegrationEventTypeRegistry
{
    /// <summary>
    /// Gets the stable name registered for the specified event type.
    /// </summary>
    /// <param name="eventType">The integration event type.</param>
    /// <returns>The registered name.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the type is not registered.</exception>
    string GetName(Type eventType);

    /// <summary>
    /// Gets the event type registered under the specified name.
    /// </summary>
    /// <param name="name">The stable name read from an outbox row or a transport message.</param>
    /// <returns>The registered type, or <see langword="null"/> when the name is unknown.</returns>
    Type? FindType(string name);
}
