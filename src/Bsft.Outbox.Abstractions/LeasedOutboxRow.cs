namespace Bsft.Outbox;

/// <summary>
/// An outbox row a drainer has claimed, together with the lease token that proves the claim and the
/// payload the side effect is applied to.
/// </summary>
/// <typeparam name="TPayload">What the row carries. The lease protocol never reads it.</typeparam>
/// <param name="Id">The store-assigned row identity.</param>
/// <param name="Payload">What the row carries, as the store projected it.</param>
/// <param name="AttemptCount">The number of attempts made before this one.</param>
/// <param name="LeaseToken">The token that proves this drainer holds the lease on the row.</param>
/// <remarks>
/// Only the holder of the token may complete or abandon the row, which is what keeps two application
/// instances from both applying the same side effect.
/// </remarks>
public sealed record LeasedOutboxRow<TPayload>(
    long Id, TPayload Payload, int AttemptCount, Guid LeaseToken);
