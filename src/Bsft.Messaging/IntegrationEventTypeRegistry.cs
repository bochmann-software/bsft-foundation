using Microsoft.Extensions.Options;

namespace Bsft.Messaging;

/// <summary>
/// The <see cref="IIntegrationEventTypeRegistry"/> built from the event types declared in
/// <see cref="BsftMessagingOptions"/>. Only declared types can be written to or read from the outbox.
/// </summary>
public sealed class IntegrationEventTypeRegistry : IIntegrationEventTypeRegistry
{
    private readonly IReadOnlyDictionary<Type, string> _namesByType;
    private readonly IReadOnlyDictionary<string, Type> _typesByName;

    /// <summary>
    /// Initializes a new instance of the <see cref="IntegrationEventTypeRegistry"/> class.
    /// </summary>
    /// <param name="options">The messaging options carrying the declared event types.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public IntegrationEventTypeRegistry(IOptions<BsftMessagingOptions> options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var declared = options.Value.RegisteredEventTypes;

        // Copied, so a later change to the options cannot alter the mapping a running dispatcher uses.
        _namesByType = declared.ToDictionary(e => e.Key, e => e.Value);
        _typesByName = declared.ToDictionary(e => e.Value, e => e.Key, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public string GetName(Type eventType)
    {
        if (eventType is null)
            throw new ArgumentNullException(nameof(eventType));

        if (!_namesByType.TryGetValue(eventType, out var name))
            throw new InvalidOperationException(
                $"The integration event type '{eventType.FullName}' is not registered. Declare it with BsftMessagingOptions.AddEvent.");

        return name;
    }

    /// <inheritdoc />
    public Type? FindType(string name)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));

        return _typesByName.TryGetValue(name, out var eventType) ? eventType : null;
    }
}
