namespace Bsft.EventAggregator.Saga;

/// <summary>
/// A granted claim on a waiting saga step: the permission to run it, together with the instance the
/// claim was granted on.
/// </summary>
/// <remarks>
/// The instance is part of the claim because it is not necessarily the one the caller resolved. The
/// registry re-reads the cache behind its gate, so a caller that read the saga from the store while
/// the cache was empty can have its claim granted on a newer cached object. Running the step on the
/// caller's object would then persist a state that never left the waiting index, leaving the cache
/// ahead of the store with nothing to reconcile them.
/// </remarks>
/// <typeparam name="TState">The saga state type.</typeparam>
/// <param name="Instance">The instance whose step index the claim advanced.</param>
/// <param name="PreviousStepIndex">The index the saga was waiting at before the claim.</param>
internal sealed record SagaStepClaimResult<TState>(SagaInstance<TState> Instance, int PreviousStepIndex);
