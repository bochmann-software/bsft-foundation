namespace Bsft.EventAggregator.Saga.EFCore;

/// <summary>
/// Exposes the <see cref="ISagaTimeoutDbContext"/> of the saga persistence window that is open on the
/// current asynchronous flow, so the saga stores can stage their changes into it instead of resolving
/// a context of their own and committing separately.
/// </summary>
public interface ISagaDbContextAccessor
{
    /// <summary>
    /// Gets the context of the open window, or <c>null</c> when no window is open and each store
    /// commits on its own.
    /// </summary>
    ISagaTimeoutDbContext? Current { get; }

    /// <summary>
    /// Registers a callback to run once the open window's commit has succeeded. A store stages a write
    /// into the window's context but must not stamp the caller's instance with the new
    /// concurrency token until that write is durable, so it defers the stamp through this hook: a
    /// refused commit then leaves the caller's instance carrying exactly the token it was loaded with.
    /// When no window is open — <see cref="Current"/> is <c>null</c> — the callback is silently dropped,
    /// mirroring <see cref="Current"/>: a store that commits on its own stamps its instance inline
    /// instead of deferring, so there is nothing to run and nothing to throw over.
    /// </summary>
    /// <param name="onCommitted">The action to run after the window commits successfully.</param>
    void OnCommitted(Action onCommitted);
}
