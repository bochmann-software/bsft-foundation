namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Provides persistence for saga instances, enabling lookup, save, and removal by saga type and correlation key.
/// </summary>
public interface ISagaStore
{
    /// <summary>
    /// Retrieves a saga instance by its type and correlation key.
    /// </summary>
    /// <typeparam name="TState">The type of the saga state.</typeparam>
    /// <param name="sagaType">The saga type identifier.</param>
    /// <param name="correlationKey">The correlation key of the instance.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The saga instance if found; otherwise, <c>null</c>.</returns>
    Task<SagaInstance<TState>?> FindAsync<TState>(string sagaType, string correlationKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves or updates a saga instance.
    /// </summary>
    /// <typeparam name="TState">The type of the saga state.</typeparam>
    /// <param name="instance">The saga instance to save.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task SaveAsync<TState>(SagaInstance<TState> instance, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a saga instance by its type and correlation key.
    /// </summary>
    /// <param name="sagaType">The saga type identifier.</param>
    /// <param name="correlationKey">The correlation key of the instance to remove.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task DeleteAsync(string sagaType, string correlationKey, CancellationToken cancellationToken = default);
}
