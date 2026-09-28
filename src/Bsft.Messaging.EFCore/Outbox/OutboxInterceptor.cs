using Bsft.Messaging.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bsft.Messaging.EFCore.Outbox;

/// <summary>
/// EF Core interceptor that turns the integration events staged for the current unit of work into
/// <see cref="OutboxMessageEntity"/> rows during the same <c>SaveChanges</c> as the state change.
/// This is what makes the outbox transactional: either the state change and its events are both
/// committed, or neither is. The hosting <see cref="DbContext"/> must implement
/// <see cref="IOutboxDbContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// Add it to the context with <c>DbContextOptionsBuilder.AddInterceptors</c>. It covers the
/// synchronous and the asynchronous save alike, so an application that saves synchronously does not
/// silently lose its events.
/// </para>
/// <para>
/// This is also where the correlation id is taken, because this is the last moment it is still
/// available: the row is written from the request that caused it, while the dispatcher that publishes
/// it runs later and knows nothing about that request.
/// </para>
/// </remarks>
public sealed class OutboxInterceptor : SaveChangesInterceptor
{
    private readonly IIntegrationEventBuffer _buffer;
    private readonly IIntegrationEventTypeRegistry _registry;
    private readonly IIntegrationEventSerializer _serializer;
    private readonly IMessageCorrelationAccessor _correlationAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxInterceptor"/> class.
    /// </summary>
    /// <param name="buffer">The staging area holding the events of the current unit of work.</param>
    /// <param name="registry">The registry supplying the stable name of each event type.</param>
    /// <param name="serializer">The serializer encoding each event into the stored payload.</param>
    /// <param name="correlationAccessor">The accessor supplying the correlation id to stage under.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public OutboxInterceptor(
        IIntegrationEventBuffer buffer,
        IIntegrationEventTypeRegistry registry,
        IIntegrationEventSerializer serializer,
        IMessageCorrelationAccessor correlationAccessor)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _correlationAccessor = correlationAccessor ?? throw new ArgumentNullException(nameof(correlationAccessor));
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        // Context is always present for save interceptor callbacks.
        WriteOutbox(eventData.Context!);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        WriteOutbox(eventData.Context!);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void WriteOutbox(DbContext context)
    {
        // The context is checked before the buffer is drained: draining first would throw the staged
        // events away on a misconfigured context instead of leaving them for a corrected save.
        if (context is not IOutboxDbContext outboxContext)
            throw new InvalidOperationException(
                $"The DbContext '{context.GetType().Name}' must implement IOutboxDbContext to use the integration event outbox.");

        var pending = _buffer.TakePending();
        if (pending.Count == 0)
            return;

        // Read once for the whole unit of work: every event staged in it belongs to the same request,
        // and the accessor may reach into a scoped context to answer.
        var correlationId = _correlationAccessor.CorrelationId;

        foreach (var integrationEvent in pending)
        {
            outboxContext.OutboxMessages.Add(new OutboxMessageEntity
            {
                EventId = integrationEvent.EventId,
                EventTypeName = _registry.GetName(integrationEvent.GetType()),
                PayloadJson = _serializer.Serialize(integrationEvent),
                OccurredAt = integrationEvent.OccurredAt.UtcDateTime,
                CorrelationId = correlationId,
            });
        }
    }
}
