using System.Collections.Concurrent;

namespace Bsft.EventAggregator.Saga;

/// <inheritdoc cref="ISagaInstanceRegistry"/>
internal sealed class SagaInstanceRegistry : ISagaInstanceRegistry
{
    private const int GateCount = 64;

    private readonly ConcurrentDictionary<string, object> _instances = new();

    private readonly SemaphoreSlim[] _gates =
        [.. Enumerable.Range(0, GateCount).Select(_ => new SemaphoreSlim(1, 1))];

    /// <summary>
    /// Builds the cache key of a saga instance. It lives here because the cache owns the format:
    /// every caller that has to name an instance must produce the exact same string.
    /// </summary>
    /// <param name="sagaType">The saga type.</param>
    /// <param name="correlationKey">The correlation key of the instance.</param>
    /// <returns>The cache key under which the instance is held.</returns>
    public string KeyFor(string sagaType, string correlationKey) => $"{sagaType}|{correlationKey}";

    /// <summary>
    /// Publishes a newly started instance. Returns <c>false</c> when one is already active for the
    /// key, so a duplicate trigger cannot clobber a running saga.
    /// </summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <param name="key">The cache key of the instance.</param>
    /// <param name="instance">The newly started instance to publish.</param>
    /// <returns><c>true</c> when the instance was published, <c>false</c> when one was already active.</returns>
    public bool TryStart<TState>(string key, SagaInstance<TState> instance) => _instances.TryAdd(key, instance);

    /// <summary>Gets the cached instance for a key, or <c>null</c> when it is not cached.</summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <param name="key">The cache key of the instance.</param>
    /// <returns>The cached instance, or <c>null</c> when none is cached under the key.</returns>
    public SagaInstance<TState>? FindCached<TState>(string key) =>
        _instances.TryGetValue(key, out var cached) && cached is SagaInstance<TState> hit ? hit : null;

    /// <summary>Drops an instance from the cache.</summary>
    /// <param name="key">The cache key of the instance to drop.</param>
    public void Remove(string key) => _instances.TryRemove(key, out _);

    private SemaphoreSlim GateFor(string key) =>
        _gates[(uint)key.GetHashCode(StringComparison.Ordinal) % GateCount];

    /// <summary>
    /// Gives a claim back after the step failed: the index returns to where it was and the instance
    /// leaves the cache, so the next event re-reads the authoritative state from the store.
    /// </summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <param name="key">The cache key of the instance.</param>
    /// <param name="instance">The instance whose claim is released.</param>
    /// <param name="previousStepIndex">The index the saga was waiting at before the claim.</param>
    /// <remarks>
    /// Behind the same gate as <see cref="ClaimStep{TState}"/>: every write to
    /// <see cref="SagaInstance{TState}.CurrentStepIndex"/> goes through one synchronization
    /// discipline, otherwise a rollback could race a concurrently claimed later step.
    /// </remarks>
    public void ReleaseClaim<TState>(string key, SagaInstance<TState> instance, int previousStepIndex)
    {
        var gate = GateFor(key);
        gate.Wait();

        try
        {
            instance.CurrentStepIndex = previousStepIndex;
            _instances.TryRemove(key, out _);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Claims a waiting step for exactly one publisher and advances the instance past it.
    /// </summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <param name="key">The cache key of the instance.</param>
    /// <param name="instance">The instance as resolved by the caller.</param>
    /// <param name="waitingIndex">The logical step index the saga must currently be waiting at.</param>
    /// <param name="cancellationToken">A cancellation token observed while queueing for the gate.</param>
    /// <returns>
    /// The granted claim — the instance that was advanced and the index it was waiting at — or
    /// <c>null</c> when somebody else claimed the step first.
    /// </returns>
    /// <remarks>
    /// The claim carries the advanced instance rather than the index alone: the cache may hold a
    /// different object than the caller resolved, and the step has to run on the one that actually
    /// moved. See <see cref="SagaStepClaimResult{TState}"/>.
    /// </remarks>
    public SagaStepClaimResult<TState>? ClaimStep<TState>(string key, SagaInstance<TState> instance, int waitingIndex, CancellationToken cancellationToken = default)
    {
        var gate = GateFor(key);
        gate.Wait(cancellationToken);

        try
        {
            // Re-read from the cache: the instance may have advanced since the caller resolved it.
            var current = FindCached<TState>(key) ?? instance;

            if (current.IsComplete || current.CurrentStepIndex != waitingIndex)
                return null;

            current.CurrentStepIndex = waitingIndex + 1;
            _instances[key] = current;
            return new SagaStepClaimResult<TState>(current, waitingIndex);
        }
        finally
        {
            gate.Release();
        }
    }
}
