namespace Bsft.EventAggregator;

/// <summary>
/// Provides a reusable base for <see cref="IEventAggregator"/> implementations that centralizes the
/// synchronous <see cref="Action{TEvent}"/> overload bookkeeping in exactly one place.
/// </summary>
/// <remarks>
/// <para>
/// The synchronous <see cref="Subscribe{TEvent}(Action{TEvent})"/> and
/// <see cref="Unsubscribe{TEvent}(Action{TEvent})"/> overloads are required members of
/// <see cref="IEventAggregator"/>. Getting them right is subtle: subscribing an <see cref="Action{TEvent}"/>
/// has to wrap it in a <see cref="Func{TEvent, Task}"/>, and unsubscribing has to remove the very same
/// wrapper instance again. Because a freshly created wrapper is a distinct delegate from the one recorded
/// on subscribe, an implementation that does not track the wrapper per handler silently fails to
/// unsubscribe. Deriving from <see cref="EventAggregatorBase"/> provides that correct bookkeeping for
/// free: the base owns the wrapper store and its lock, so a derived class only has to implement the
/// asynchronous <see cref="Func{TEvent, Task}"/> based members plus its own handler storage.
/// </para>
/// <para>
/// Implementations that do not want to derive from this base still satisfy the contract by implementing
/// the four <see cref="Action{TEvent}"/> and <see cref="Func{TEvent, Task}"/> members directly, but they
/// must then reproduce the wrapper bookkeeping themselves.
/// </para>
/// </remarks>
public abstract class EventAggregatorBase : IEventAggregator
{
    /// <summary>
    /// The single lock that guards both the base wrapper bookkeeping and the derived handler storage.
    /// A derived class must use this same object for every mutation and snapshot of its handler list
    /// (its Func-based <see cref="Subscribe{TEvent}(Func{TEvent, Task})"/>,
    /// <see cref="Unsubscribe{TEvent}(Func{TEvent, Task})"/>,
    /// <see cref="UnsubscribeWrappers{TEvent}(IReadOnlyCollection{Func{TEvent, Task}})"/> and its
    /// <see cref="PublishAsync{TEvent}(TEvent, CancellationToken)"/> read) rather than a private
    /// lock of its own. Sharing one lock makes the base's Action-overload bookkeeping and the forward
    /// call into the derived storage a single atomic step, which closes a subscribe/unsubscribe race:
    /// with two separate locks a concurrent <see cref="Unsubscribe{TEvent}(Action{TEvent})"/> could
    /// slip between recording the wrapper and adding it to the handler list and leave the handler
    /// permanently subscribed. Because <see langword="lock"/> is reentrant the base can hold this lock
    /// across the forward call while the derived member re-acquires it safely. Handlers must never be
    /// invoked while this lock is held; take a snapshot under the lock and invoke outside it.
    /// </summary>
    /// <remarks>
    /// A publish may read without the lock where the storage is replaced rather than mutated, which is
    /// how <see cref="SimpleEventAggregator"/> holds its handlers: a reader then sees the collection
    /// either before or after a subscription, never half way through one, and that is the same answer
    /// a snapshot taken under the lock would have given. Every mutation still takes the lock, because
    /// the read-modify-write that produces the replacement is not atomic by itself and because the
    /// base's bookkeeping has to happen in the same step.
    /// </remarks>
    protected readonly object SyncRoot = new();

    // Maps a synchronous Action handler to every Func wrapper that was actually subscribed for it, so
    // that Unsubscribe can locate and remove the correct wrapper instances. Without this mapping the
    // wrapper created in Subscribe and the one created in Unsubscribe would be distinct delegate
    // instances and removal (which uses reference equality) would silently fail. A list is kept per
    // handler because the same Action instance may be subscribed more than once; Unsubscribe then
    // removes every wrapper it produced instead of leaking the earlier ones.
    private readonly Dictionary<Delegate, List<Delegate>> _actionWrappers = [];

    /// <summary>
    /// Subscribes an asynchronous handler for events of type <typeparamref name="TEvent"/>.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to subscribe to.</typeparam>
    /// <param name="handler">The asynchronous handler to invoke when the event is published.</param>
    public abstract void Subscribe<TEvent>(Func<TEvent, Task> handler);

    /// <summary>
    /// Unsubscribes a previously registered asynchronous handler for events of type <typeparamref name="TEvent"/>.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to unsubscribe from.</typeparam>
    /// <param name="handler">The asynchronous handler to remove.</param>
    public abstract void Unsubscribe<TEvent>(Func<TEvent, Task> handler);

    /// <summary>
    /// Publishes an event to all registered handlers of type <typeparamref name="TEvent"/>.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to publish.</typeparam>
    /// <param name="event">The event instance to publish.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when all handlers have been invoked.</returns>
    public abstract Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes a synchronous handler for events of type <typeparamref name="TEvent"/>. The base wraps
    /// the handler in a <see cref="Func{TEvent, Task}"/>, records the wrapper so it can be removed later,
    /// and forwards it to the asynchronous <see cref="Subscribe{TEvent}(Func{TEvent, Task})"/>.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to subscribe to.</typeparam>
    /// <param name="handler">The synchronous handler to invoke when the event is published.</param>
    public void Subscribe<TEvent>(Action<TEvent> handler)
    {
        if (handler is null)
            throw new ArgumentNullException(nameof(handler));

        Func<TEvent, Task> wrapper = e =>
        {
            handler(e);
            return Task.CompletedTask;
        };

        // Record the wrapper AND forward it to the derived handler storage under one lock acquisition,
        // so a concurrent Unsubscribe(Action) cannot observe the wrapper in the map before it reaches
        // the handler list. The reentrant lock lets the derived Subscribe(Func) re-acquire SyncRoot.
        lock (SyncRoot)
        {
            if (!_actionWrappers.TryGetValue(handler, out var wrappers))
            {
                wrappers = [];
                _actionWrappers[handler] = wrappers;
            }

            wrappers.Add(wrapper);

            Subscribe(wrapper);
        }
    }

    /// <summary>
    /// Unsubscribes a synchronous handler previously registered via
    /// <see cref="Subscribe{TEvent}(Action{TEvent})"/>. Every wrapper recorded for the handler is
    /// collected under a single lock acquisition and removed in one pass through
    /// <see cref="UnsubscribeWrappers{TEvent}(IReadOnlyCollection{Func{TEvent, Task}})"/>. Removing a
    /// handler that was never subscribed is a safe no-op.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to unsubscribe from.</typeparam>
    /// <param name="handler">The synchronous handler to remove.</param>
    public void Unsubscribe<TEvent>(Action<TEvent> handler)
    {
        if (handler is null)
            throw new ArgumentNullException(nameof(handler));

        // Remove the wrapper bookkeeping AND forward the removal to the derived handler storage under
        // one lock acquisition, mirroring Subscribe(Action). This makes a subscribe-then-unsubscribe of
        // the same handler atomic against a racing Subscribe: the wrapper can never be added to the
        // handler list after this method has already decided it was not present.
        lock (SyncRoot)
        {
            if (!_actionWrappers.TryGetValue(handler, out var wrappers))
                return;

            _actionWrappers.Remove(handler);

            var typed = new Func<TEvent, Task>[wrappers.Count];
            for (var i = 0; i < wrappers.Count; i++)
                typed[i] = (Func<TEvent, Task>)wrappers[i];

            UnsubscribeWrappers<TEvent>(typed);
        }
    }

    /// <summary>
    /// Removes the supplied wrappers from the derived handler storage in a single pass. The base calls
    /// this exactly once per <see cref="Unsubscribe{TEvent}(Action{TEvent})"/>, so an implementation
    /// should acquire its lock once and remove every wrapper in one traversal rather than removing them
    /// one by one.
    /// </summary>
    /// <typeparam name="TEvent">The type of event whose handlers are being removed.</typeparam>
    /// <param name="wrappers">The wrapper delegates to remove; never <see langword="null"/> or empty when called by the base.</param>
    protected abstract void UnsubscribeWrappers<TEvent>(IReadOnlyCollection<Func<TEvent, Task>> wrappers);
}
