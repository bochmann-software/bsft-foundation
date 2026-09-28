namespace Bsft.Messaging;

/// <summary>
/// Everything the application states about messaging: which integration event types are published and
/// under which stable name they travel, plus the two option groups that steer the producing and the
/// consuming half of the pipeline.
/// </summary>
/// <remarks>
/// The event vocabulary and the two groups live in one options type because they are one decision made
/// in one place. A consumer name that belongs to another application's vocabulary, or a retry budget
/// configured into a section the type registry never sees, are exactly the mistakes a single bound
/// section makes impossible.
/// </remarks>
public sealed class BsftMessagingOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:Messaging";

    private readonly Dictionary<Type, string> _namesByType = [];

    /// <summary>Gets the registered event types and their stable names.</summary>
    public IReadOnlyDictionary<Type, string> RegisteredEventTypes => _namesByType;

    /// <summary>
    /// Gets the options of the consuming side: the consumer identity, the retry budget and the bounds
    /// of the inbox.
    /// </summary>
    public ConsumerOptions Consumer { get; } = new();

    /// <summary>
    /// Gets the options of the outbox dispatcher: how often it polls, how much it claims per poll and
    /// how long a claim holds.
    /// </summary>
    public OutboxOptions Outbox { get; } = new();

    /// <summary>
    /// Declares an integration event type.
    /// </summary>
    /// <typeparam name="TEvent">The integration event type to declare.</typeparam>
    /// <param name="name">
    /// The stable name the event travels under. Defaults to the full type name. Give an explicit name
    /// when the CLR type may be renamed or moved later — outbox rows written under the old name are
    /// still waiting to be delivered when the new code starts.
    /// </param>
    /// <returns>The options instance for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when the name is null, empty or already taken by another type.</exception>
    public BsftMessagingOptions AddEvent<TEvent>(string? name = null)
        where TEvent : IIntegrationEvent
    {
        var eventType = typeof(TEvent);
        var eventName = name ?? eventType.FullName!;

        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("The integration event name must not be empty.", nameof(name));

        // A name collision means two types would deserialize from the same rows. Detecting it at
        // startup beats discovering it when a dispatcher deserializes into the wrong type.
        foreach (var (registeredType, registeredName) in _namesByType)
        {
            if (registeredType != eventType && string.Equals(registeredName, eventName, StringComparison.Ordinal))
                throw new ArgumentException(
                    $"The integration event name '{eventName}' is already registered for '{registeredType.FullName}'.",
                    nameof(name));
        }

        _namesByType[eventType] = eventName;

        return this;
    }
}
