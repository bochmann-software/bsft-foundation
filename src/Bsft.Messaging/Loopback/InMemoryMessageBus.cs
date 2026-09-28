using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bsft.Messaging.Loopback;

/// <summary>
/// The default <see cref="IMessageBus"/>: it decodes the message and delivers the event to the
/// <see cref="IIntegrationEventHandler{TEvent}"/> instances registered in this process. It is the
/// transport for a single-service deployment and the stand-in for a broker in tests; a broker adapter
/// replaces it by registering its own <see cref="IMessageBus"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each publish runs in its own service scope, so a handler can take a scoped dependency (a DbContext,
/// for instance) even though the bus itself is a singleton driven by a background dispatcher.
/// </para>
/// <para>
/// It is also both ends of the wire at once: it consumes the message it was handed, so it is where
/// the correlation id of the producing request is restored for the handler. A broker adapter does the
/// same on its consuming side, which is what makes the two deployments behave alike.
/// </para>
/// <para>
/// Handling a message inside the publish is what makes it the simplest option and also what it gives
/// up: there is no inbox in this path and no bounded retry, because a failure simply fails the publish
/// and the outbox retries the whole message. Where a single-process deployment wants the consuming
/// pipeline it will have in production — inbox, bounded attempts, parking — the loopback transport of
/// <c>AddBsftMessagingLoopback</c> replaces this bus and the consumer host does the handling.
/// </para>
/// </remarks>
public sealed class InMemoryMessageBus : IMessageBus
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IIntegrationEventSerializer _serializer;
    private readonly IMessageCorrelationScope _correlationScope;
    private readonly IIntegrationEventHandlerInvoker _invoker;
    private readonly ILogger<InMemoryMessageBus> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryMessageBus"/> class.
    /// </summary>
    /// <param name="scopeFactory">The factory used to create a scope per publish.</param>
    /// <param name="serializer">The serializer decoding the message payload into its event.</param>
    /// <param name="correlationScope">The ambient scope the message's correlation id is restored into.</param>
    /// <param name="invoker">The invoker running the handlers registered for the decoded event.</param>
    /// <param name="logger">The logger used to report deliveries without a subscriber.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public InMemoryMessageBus(
        IServiceScopeFactory scopeFactory,
        IIntegrationEventSerializer serializer,
        IMessageCorrelationScope correlationScope,
        IIntegrationEventHandlerInvoker invoker,
        ILogger<InMemoryMessageBus> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _correlationScope = correlationScope ?? throw new ArgumentNullException(nameof(correlationScope));
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task PublishAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default)
    {
        if (message is null)
            throw new ArgumentNullException(nameof(message));

        var integrationEvent = _serializer.Deserialize(message);

        // Restored around the whole delivery rather than around the handler call alone, so that an
        // event a handler stages inherits the correlation of the message that caused it and a chain of
        // integration events stays one traceable story.
        using var correlation = _correlationScope.Use(message.CorrelationId);

        // Async scope, not a synchronous one: a handler resolved through it can walk into
        // infrastructure that is only IAsyncDisposable, and this method is already fully async.
        await using var scope = _scopeFactory.CreateAsyncScope();

        var handlerCount = await _invoker
            .InvokeAllAsync(scope.ServiceProvider, integrationEvent, cancellationToken)
            .ConfigureAwait(false);

        if (handlerCount == 0)
        {
            // Not an error: an event may exist for consumers in other services only.
            _logger.LogDebug(
                "No in-process handler is registered for integration event {EventName}.", message.EventName);
        }
    }
}
