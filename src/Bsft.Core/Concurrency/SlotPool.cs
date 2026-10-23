namespace Bsft.Core.Concurrency;

/// <summary>
/// The default <see cref="ISlotPool"/>: a counting semaphore with a documented order.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fairness.</b> Callers are admitted strictly in the order they called
/// <see cref="EnterAsync"/>. A caller that arrives while others wait queues behind them even when a
/// slot has just been freed; the slot goes to the head of the queue at the moment it is released.
/// <see cref="SemaphoreSlim"/> documents no order for its waiters, which is why the pool keeps its
/// own queue.
/// </para>
/// <para>
/// <b>Cancellation.</b> A token that is already cancelled yields a cancelled task before the pool is
/// touched, even when a slot is free. A caller cancelled while it waits leaves the queue and holds
/// nothing, so there is nothing for it to give back. A token cancelled after the slot was granted
/// does not revoke the grant: the call returns the slot and the caller disposes it. The queue is
/// guarded by one lock, a waiter leaves it under that lock before it is cancelled and a slot is
/// assigned to a waiter inside the same lock, so a waiter is either granted or cancelled, never both.
/// </para>
/// <para>
/// <b>Release.</b> Disposing the returned handle frees the slot once; disposing it again does
/// nothing, so a handle disposed by two code paths cannot free a slot that another caller holds.
/// </para>
/// </remarks>
public sealed class SlotPool : ISlotPool
{
    private readonly object _lock = new();
    private readonly LinkedList<Waiter> _waiters = new();
    private int _available;

    /// <summary>Initializes a new instance of the <see cref="SlotPool"/> class.</summary>
    /// <param name="capacity">How many slots the pool has; at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacity"/> is below 1.</exception>
    public SlotPool(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
        _available = capacity;
    }

    /// <inheritdoc />
    public int Capacity { get; }

    /// <inheritdoc />
    public int Available
    {
        get
        {
            lock (_lock)
                return _available;
        }
    }

    /// <inheritdoc />
    public Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<IDisposable>(cancellationToken);

        Waiter waiter;
        lock (_lock)
        {
            // A free slot is taken only when nobody waits: a caller never overtakes the queue.
            if (_available > 0 && _waiters.Count == 0)
            {
                _available--;
                return Task.FromResult<IDisposable>(new Slot(this));
            }

            waiter = new Waiter();
            waiter.Node = _waiters.AddLast(waiter);
        }

        return WaitAsync(waiter, cancellationToken);
    }

    private async Task<IDisposable> WaitAsync(Waiter waiter, CancellationToken cancellationToken)
    {
        var registration = cancellationToken.Register(
            static state =>
            {
                var (pool, waiting, token) = ((SlotPool, Waiter, CancellationToken))state!;
                pool.Cancel(waiting, token);
            },
            (this, waiter, cancellationToken));

        try
        {
            await waiter.Completion.Task.ConfigureAwait(false);
        }
        finally
        {
            // Granted or cancelled, the wait is over. A token cancelled after the grant would find a
            // waiter that is no longer queued and leave it alone, so the registration is not needed
            // for the slot's lifetime: the grant cannot be taken back.
            registration.Dispose();
        }

        return new Slot(this);
    }

    private void Cancel(Waiter waiter, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (waiter.Node is { } node)
            {
                _waiters.Remove(node);
                waiter.Node = null;
            }
        }

        // A waiter that was granted the slot has a completed task, because the grant is made under
        // the same lock: cancelling it changes nothing, and the grant stands.
        waiter.Completion.TrySetCanceled(cancellationToken);
    }

    private void Release()
    {
        lock (_lock)
        {
            if (_waiters.First is { } first)
            {
                var granted = first.Value;
                _waiters.RemoveFirst();
                granted.Node = null;

                // Under the lock, so that a cancellation can neither miss the grant nor win against
                // it; the waiter continues on the thread pool, never inside the lock.
                granted.Completion.TrySetResult();
            }
            else
            {
                _available++;
            }
        }
    }

    private sealed class Waiter
    {
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal LinkedListNode<Waiter>? Node { get; set; }
    }

    private sealed class Slot(SlotPool pool) : IDisposable
    {
        private int _released;

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
                return;

            pool.Release();
        }
    }
}
