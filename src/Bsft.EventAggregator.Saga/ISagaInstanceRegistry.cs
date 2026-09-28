namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Holds the live saga instances and decides which publisher may advance a waiting step.
/// </summary>
/// <remarks>
/// The cache exists so that a reentrant publish — a step that synchronously publishes the event of
/// the next step — sees the latest state without a store round-trip.
/// <para>
/// Claiming a step is guarded by a striped set of gates. Striped rather than one gate per instance so
/// the set cannot grow with the number of correlation keys; a hash collision briefly serializes two
/// unrelated sagas, which is harmless for a critical section this short. The critical section is a
/// dictionary lookup and two field writes: neither a store call nor user code may run inside it,
/// because the gate is not reentrant and a step publishing the next step's event would deadlock
/// against it.
/// </para>
/// </remarks>
internal interface ISagaInstanceRegistry
{
    /// <inheritdoc cref="SagaInstanceRegistry.KeyFor"/>
    string KeyFor(string sagaType, string correlationKey);

    /// <inheritdoc cref="SagaInstanceRegistry.TryStart{TState}"/>
    bool TryStart<TState>(string key, SagaInstance<TState> instance);

    /// <inheritdoc cref="SagaInstanceRegistry.FindCached{TState}"/>
    SagaInstance<TState>? FindCached<TState>(string key);

    /// <inheritdoc cref="SagaInstanceRegistry.Remove"/>
    void Remove(string key);

    /// <inheritdoc cref="SagaInstanceRegistry.ClaimStep{TState}"/>
    SagaStepClaimResult<TState>? ClaimStep<TState>(string key, SagaInstance<TState> instance, int waitingIndex, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="SagaInstanceRegistry.ReleaseClaim{TState}"/>
    void ReleaseClaim<TState>(string key, SagaInstance<TState> instance, int previousStepIndex);
}
