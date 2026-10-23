namespace Bsft.Core.Observers;

/// <summary>
/// Tells every registered observer about an event, in registration order, and contains the failure
/// of each one so the ones after it still hear of it.
/// </summary>
/// <remarks>
/// <para>
/// One composite serves any observer contract, so a contract is not given a composite class of its
/// own. A contract that wants to be called as a single observer implements it by delegating, one
/// line per member:
/// </para>
/// <code>
/// public void Completed(CallInfo call) =&gt; _composite.Notify(observer =&gt; observer.Completed(call));
/// </code>
/// <para>
/// The composite takes a snapshot of the observers when it is created, so an observer registered
/// later is not told. A contract implemented by the delegating class must not be registered as one
/// of the observers it injects, or the class would be told about its own events.
/// </para>
/// </remarks>
/// <typeparam name="TObserver">The observer contract.</typeparam>
public sealed class ObserverComposite<TObserver>
    where TObserver : class
{
    private readonly IObserverGuard _guard;

    /// <summary>Initializes a new instance of the <see cref="ObserverComposite{TObserver}"/> class.</summary>
    /// <param name="observers">Every registered observer, in the order they are told.</param>
    /// <param name="guard">Contains an observer failure.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public ObserverComposite(IEnumerable<TObserver> observers, IObserverGuard guard)
    {
        Observers = [.. observers ?? throw new ArgumentNullException(nameof(observers))];
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    /// <summary>Gets the observers, in the order they are told.</summary>
    public IReadOnlyList<TObserver> Observers { get; }

    /// <summary>Tells every observer.</summary>
    /// <param name="call">The call to make on each observer.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="call"/> is null.</exception>
    public void Notify(Action<TObserver> call)
    {
        ArgumentNullException.ThrowIfNull(call);

        foreach (var observer in Observers)
            _guard.Invoke(observer, () => call(observer));
    }

    /// <summary>Tells every observer, one after the other.</summary>
    /// <param name="call">The call to make on each observer.</param>
    /// <param name="cancellationToken">Cancels the observed operation; nobody is told after it.</param>
    /// <returns>A task that completes when every observer was told.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="call"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public async Task NotifyAsync(Func<TObserver, CancellationToken, Task> call, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);

        foreach (var observer in Observers)
        {
            // An observer that ignores its token must not let the next one be told.
            cancellationToken.ThrowIfCancellationRequested();
            await _guard.InvokeAsync(observer, token => call(observer, token), cancellationToken).ConfigureAwait(false);
        }
    }
}
