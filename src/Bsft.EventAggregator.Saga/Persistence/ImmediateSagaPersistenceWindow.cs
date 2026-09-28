namespace Bsft.EventAggregator.Saga.Persistence;

/// <summary>
/// The persistence window used by stores that already make every write durable on its own, such as
/// the in-memory pair. Opening one costs nothing and committing it has nothing left to do.
/// </summary>
/// <remarks>
/// It exists so that <see cref="Bsft.EventAggregator.Saga.Execution.SagaFlowHost"/> has one persistence path rather than a branch on the
/// configured store: a provider that can commit both effects together replaces this registration.
/// </remarks>
internal sealed class ImmediateSagaPersistenceWindow : ISagaPersistenceWindow
{
    /// <inheritdoc />
    public Task<ISagaPersistenceWindowScope> OpenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<ISagaPersistenceWindowScope>(new Scope());

    /// <summary>A window that stages nothing, because its stores wrote as they went.</summary>
    private sealed class Scope : ISagaPersistenceWindowScope
    {
        /// <inheritdoc />
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <inheritdoc />
        public ValueTask DisposeAsync() => default;
    }
}
