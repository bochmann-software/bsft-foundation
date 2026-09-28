namespace Bsft.EventAggregator.Saga.Persistence;

/// <summary>
/// Opens the window in which everything a finished saga step produced — the advanced state and the
/// step's timeout operations — is staged and then committed as one unit.
/// </summary>
/// <remarks>
/// Without a shared window the state store and the timeout store commit independently, so a timer
/// write that fails leaves a saga that has advanced past a step whose reminders were never written.
/// A store that writes immediately, such as the in-memory one, needs no window and uses
/// <see cref="ImmediateSagaPersistenceWindow"/>, whose scope commits nothing.
/// <para>
/// Only the persist window is shared. A step body runs before the window is opened, so no database
/// connection is pinned across business code.
/// </para>
/// </remarks>
public interface ISagaPersistenceWindow
{
    /// <summary>
    /// Opens a persistence window for the current asynchronous flow.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>
    /// The scope that commits the staged effects. Disposing it without committing discards them.
    /// </returns>
    Task<ISagaPersistenceWindowScope> OpenAsync(CancellationToken cancellationToken = default);
}
