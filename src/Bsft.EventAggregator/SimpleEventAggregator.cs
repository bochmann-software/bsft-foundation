using System.Collections.Concurrent;

namespace Bsft.EventAggregator;

/// <summary>
/// A thread-safe, in-process implementation of <see cref="IEventAggregator"/>
/// that stores handlers in a dictionary and invokes them sequentially on publish.
/// The synchronous <see cref="Action{TEvent}"/> overload bookkeeping is inherited from
/// <see cref="EventAggregatorBase"/>; this type only provides the asynchronous members and its
/// own handler storage.
/// </summary>
/// <remarks>
/// Copy-on-write: a subscription replaces the handler array of its event type rather than mutating
/// one, so a publish reads a reference and iterates it without a lock and without allocating. That
/// trade is the right way round here, because subscribing happens at wiring time and publishing
/// happens for every event the process raises.
/// </remarks>
public sealed class SimpleEventAggregator : EventAggregatorBase
{
    private readonly ConcurrentDictionary<Type, Delegate[]> _handlers = new();

    /// <inheritdoc />
    public override void Subscribe<TEvent>(Func<TEvent, Task> handler)
    {
        if (handler is null)
            throw new ArgumentNullException(nameof(handler));

        // SyncRoot is the base-owned lock shared with the Action-overload bookkeeping, so a
        // Subscribe(Action) forward call and this handler-list mutation form one atomic step. The
        // dictionary is concurrent so that a publish can read it while this runs, not so that two
        // writers can: the read-modify-write below still needs the lock.
        lock (SyncRoot)
        {
            _handlers[typeof(TEvent)] = _handlers.TryGetValue(typeof(TEvent), out var existing)
                ? [.. existing, handler]
                : [handler];
        }
    }

    /// <inheritdoc />
    public override void Unsubscribe<TEvent>(Func<TEvent, Task> handler)
    {
        if (handler is null)
            throw new ArgumentNullException(nameof(handler));

        lock (SyncRoot)
        {
            Remove<TEvent>(existing => Array.IndexOf(existing, (Delegate)handler) is var at && at >= 0
                ? [.. existing[..at], .. existing[(at + 1)..]]
                : existing);
        }
    }

    /// <inheritdoc />
    protected override void UnsubscribeWrappers<TEvent>(IReadOnlyCollection<Func<TEvent, Task>> wrappers)
    {
        // The base guarantees a non-null, non-empty collection here (see EventAggregatorBase). Build a
        // set so the single filtering pass tests membership in O(1) rather than scanning the wrappers
        // once per handler.
        var set = new HashSet<Func<TEvent, Task>>(wrappers);

        lock (SyncRoot)
        {
            Remove<TEvent>(existing =>
                [.. existing.Where(h => !set.Contains((Func<TEvent, Task>)h))]);
        }
    }

    /// <inheritdoc />
    public override async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event is null)
            throw new ArgumentNullException(nameof(@event));

        // No lock and no copy: the array this reads is never mutated, only replaced. A subscription
        // that lands during the loop below therefore does not see this publish, which is the same
        // answer a snapshot taken under a lock would have given.
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
            return;

        List<Exception>? failures = null;

        foreach (var handler in handlers)
        {
            // Cancellation joins the failures rather than replacing them. A handler that already threw
            // threw whether or not the publish was cancelled a moment later, and a caller that only
            // ever saw the OperationCanceledException would never learn its event was half delivered.
            if (cancellationToken.IsCancellationRequested)
            {
                if (failures is null)
                    throw new OperationCanceledException(cancellationToken);

                failures.Add(new OperationCanceledException(cancellationToken));

                break;
            }

            try
            {
                await ((Func<TEvent, Task>)handler)(@event).ConfigureAwait(false);
            }
            // Subscriber isolation requires catching every exception type: a handler is arbitrary
            // user code, and one failure must not stop the remaining handlers. Nothing is swallowed —
            // all failures are rethrown together as an AggregateException after the loop. The
            // generic-exception-catch finding on this catch is accepted, not suppressed.
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is not null)
            throw new AggregateException("One or more event handlers threw an exception.", failures);
    }

    /// <summary>
    /// Replaces the handler array of <typeparamref name="TEvent"/> with what <paramref name="without"/>
    /// makes of it, dropping the entry entirely once nothing is left so that an event type nobody
    /// listens to costs no dictionary slot.
    /// </summary>
    /// <typeparam name="TEvent">The event type whose handlers are being removed.</typeparam>
    /// <param name="without">Produces the remaining handlers from the current ones.</param>
    private void Remove<TEvent>(Func<Delegate[], Delegate[]> without)
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var existing))
            return;

        var remaining = without(existing);

        if (remaining.Length == 0)
            _handlers.TryRemove(typeof(TEvent), out _);
        else
            _handlers[typeof(TEvent)] = remaining;
    }
}
