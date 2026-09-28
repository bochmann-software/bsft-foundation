using Bsft.Outbox;

namespace Bsft.Messaging.Publishing;

/// <summary>
/// What an outbox row means in this package: publish the message it carries on the bus.
/// </summary>
/// <remarks>
/// This is the one integration-event-specific step of the drain pass. Everything around it — the
/// lease, the order of publish and mark, the retry schedule, the retirement — is the kernel's and is
/// shared with every other outbox in the host.
/// </remarks>
internal sealed class IntegrationEventPublication : IOutboxSideEffect<IntegrationEventMessage>
{
    private readonly IMessageBus _messageBus;

    /// <summary>Initializes a new instance of the <see cref="IntegrationEventPublication"/> class.</summary>
    /// <param name="messageBus">The bus the messages are published through.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messageBus"/> is null.</exception>
    public IntegrationEventPublication(IMessageBus messageBus)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
    }

    /// <inheritdoc />
    public Task ApplyAsync(IntegrationEventMessage payload, CancellationToken cancellationToken = default)
        => _messageBus.PublishAsync(payload, cancellationToken);

    /// <inheritdoc />
    public string Describe(IntegrationEventMessage payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // The id and the registered name, and nothing of the payload: the event's own body is the
        // application's data, and a log line is the wrong place for it.
        return $"event {payload.EventId} of type {payload.EventName}";
    }
}
