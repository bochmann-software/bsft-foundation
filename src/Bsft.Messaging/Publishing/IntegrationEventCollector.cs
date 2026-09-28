namespace Bsft.Messaging.Publishing;

/// <summary>
/// Scoped staging area for the integration events of one unit of work. It implements both sides of
/// the staging contract: application code stages through <see cref="IIntegrationEventCollector"/> and
/// the persistence layer drains through <see cref="IIntegrationEventBuffer"/>.
/// </summary>
/// <remarks>
/// Registered per scope, so the events of one request or one saga step never mix with another's. The
/// instance is not thread-safe for concurrent staging, matching the scoped unit of work it belongs to.
/// </remarks>
public sealed class IntegrationEventCollector : IIntegrationEventCollector, IIntegrationEventBuffer
{
    private readonly List<IIntegrationEvent> _staged = [];

    /// <inheritdoc />
    public void Enqueue(IIntegrationEvent integrationEvent)
    {
        if (integrationEvent is null)
            throw new ArgumentNullException(nameof(integrationEvent));

        _staged.Add(integrationEvent);
    }

    /// <inheritdoc />
    public IReadOnlyList<IIntegrationEvent> TakePending()
    {
        if (_staged.Count == 0)
            return [];

        var pending = _staged.ToArray();
        _staged.Clear();

        return pending;
    }
}
