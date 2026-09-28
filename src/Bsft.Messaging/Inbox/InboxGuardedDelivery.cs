using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bsft.Messaging.Inbox;

/// <summary>
/// The default <see cref="IInboxGuardedDelivery"/>: one service scope per delivery, the correlation id
/// of the producing request restored around it, the inbox consulted before the handlers and committed
/// after them.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="IInboxStore"/> is resolved from the delivery's own scope rather than injected, which
/// is what allows the EF Core store to reach the same <c>DbContext</c> the handlers write through. That
/// is the whole mechanism behind the atomic case: the mark is staged on that context and the commit
/// that follows the handlers writes it in the same transaction as their work, so a handler's changes
/// and the record that they happened cannot survive each other.
/// </para>
/// <para>
/// Where no context is shared — the in-memory store, or handlers writing to something that is not the
/// inbox's database — the commit is a second write after the handlers succeeded, and a crash in
/// between leaves the work done and unrecorded. The redelivery then runs the handlers again. That
/// ordering is chosen deliberately over marking first: marking first would turn the same crash into an
/// event nobody ever handled, and a duplicate an idempotent handler absorbs is recoverable in a way a
/// silently dropped event is not.
/// </para>
/// </remarks>
internal sealed class InboxGuardedDelivery : IInboxGuardedDelivery
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessageCorrelationScope _correlationScope;
    private readonly IIntegrationEventHandlerInvoker _invoker;
    private readonly ConsumerOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxGuardedDelivery"/> class.
    /// </summary>
    /// <param name="scopeFactory">The factory used to create a scope per delivery.</param>
    /// <param name="correlationScope">The ambient scope the message's correlation id is restored into.</param>
    /// <param name="invoker">The invoker running the handlers registered for the event.</param>
    /// <param name="options">The messaging options carrying the consumer identity.</param>
    /// <param name="timeProvider">Time provider used to stamp the received instant.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public InboxGuardedDelivery(
        IServiceScopeFactory scopeFactory,
        IMessageCorrelationScope correlationScope,
        IIntegrationEventHandlerInvoker invoker,
        IOptions<BsftMessagingOptions> options,
        TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _correlationScope = correlationScope ?? throw new ArgumentNullException(nameof(correlationScope));
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value.Consumer;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async Task<EventDeliveryOutcome> DeliverAsync(
        IntegrationEventMessage message, IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (message is null)
            throw new ArgumentNullException(nameof(message));
        if (integrationEvent is null)
            throw new ArgumentNullException(nameof(integrationEvent));

        // Restored around the whole delivery rather than around the handler call alone, so that an
        // event a handler stages inherits the correlation of the message that caused it and a chain of
        // integration events stays one traceable story.
        using var correlation = _correlationScope.Use(message.CorrelationId);

        // Async scope, not a synchronous one: a handler resolved through it can walk into
        // infrastructure that is only IAsyncDisposable, and this method is already fully async.
        await using var scope = _scopeFactory.CreateAsyncScope();

        var inbox = scope.ServiceProvider.GetRequiredService<IInboxStore>();

        var claimed = await inbox
            .TryBeginAsync(message, _options.ConsumerName, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        if (!claimed)
            return EventDeliveryOutcome.Duplicate;

        await CommitOrAbandonAsync(inbox, scope.ServiceProvider, integrationEvent, cancellationToken).ConfigureAwait(false);

        return EventDeliveryOutcome.Handled;
    }

    /// <summary>
    /// Runs the handlers and commits the inbox mark, releasing the claim again if either step fails.
    /// </summary>
    /// <remarks>
    /// The release is in a <c>finally</c> rather than a <c>catch</c> so that it also covers a delivery
    /// that ends in cancellation, and so that the original failure reaches the caller untouched. A
    /// claim that were left behind by a failure would make the redelivery look like a duplicate, which
    /// would turn a retryable handler failure into an event nobody ever handles.
    /// </remarks>
    private async Task CommitOrAbandonAsync(
        IInboxStore inbox, IServiceProvider handlerServices, IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var completed = false;
        try
        {
            await _invoker.InvokeAllAsync(handlerServices, integrationEvent, cancellationToken).ConfigureAwait(false);
            await inbox.CommitAsync(cancellationToken).ConfigureAwait(false);
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                // Deliberately not the caller's token: the claim has to be released even when the
                // delivery failed because the host is shutting down, and a release that observed the
                // cancelled token would be the one step that does not run when it matters most.
                await inbox.AbandonAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
