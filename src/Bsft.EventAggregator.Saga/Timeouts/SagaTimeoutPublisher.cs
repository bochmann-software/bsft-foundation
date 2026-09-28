using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace Bsft.EventAggregator.Saga.Timeouts;

/// <summary>
/// Publishes a due timeout's carried event: it resolves the persisted payload type, deserializes the
/// payload and hands it to the event aggregator under the correlation id of the request that
/// scheduled the timer.
/// </summary>
/// <remarks>
/// Nothing is caught here. A failure has to reach the dispatcher, which records the failed attempt
/// and leaves the timeout pending for a retry.
/// </remarks>
public sealed class SagaTimeoutPublisher : ISagaTimeoutPublisher
{
    private static readonly MethodInfo _publishMethod = typeof(IEventAggregator)
        .GetMethod(nameof(IEventAggregator.PublishAsync))!;

    // Closing the generic method is not free, and a saga fires the same handful of timeout event
    // types over and over.
    private static readonly ConcurrentDictionary<Type, MethodInfo> _closedPublishMethods = new();

    private readonly IEventAggregator _eventAggregator;
    private readonly ISagaCorrelationScope _correlationScope;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaTimeoutPublisher"/> class.
    /// </summary>
    /// <param name="eventAggregator">The aggregator the timeout events are published through.</param>
    /// <param name="correlationScope">Ambient scope used to restore a timeout's correlation id while it is published.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public SagaTimeoutPublisher(IEventAggregator eventAggregator, ISagaCorrelationScope correlationScope)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _correlationScope = correlationScope ?? throw new ArgumentNullException(nameof(correlationScope));
    }

    /// <inheritdoc />
    public async Task PublishAsync(SagaTimeout timeout, CancellationToken cancellationToken = default)
    {
        if (timeout is null)
            throw new ArgumentNullException(nameof(timeout));

        var payloadType = SagaPersistedTypeResolver.Resolve(timeout.PayloadTypeName)
            ?? throw new InvalidOperationException($"Timeout event type '{timeout.PayloadTypeName}' could not be resolved.");

        var payload = JsonSerializer.Deserialize(timeout.PayloadJson, payloadType)
            ?? throw new InvalidOperationException($"Timeout {timeout.Id} deserialized to null.");

        // Restore the correlation id of the request that scheduled the timeout for the duration of
        // the publish, so logs and the audit trail can follow the chain across the wait.
        using (_correlationScope.Use(timeout.CorrelationId))
        {
            var method = _closedPublishMethods.GetOrAdd(payloadType, static type => _publishMethod.MakeGenericMethod(type));
            var publish = (Task)method.Invoke(_eventAggregator, [payload, cancellationToken])!;

            await publish.ConfigureAwait(false);
        }
    }
}
