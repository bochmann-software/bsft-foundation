namespace Bsft.Idempotency;

/// <summary>
/// The answer a store gives when a request tries to claim its key.
/// </summary>
/// <param name="Outcome">Which of the four things happened.</param>
/// <param name="Response">
/// The stored response, set only when <paramref name="Outcome"/> is
/// <see cref="IdempotencyBeginOutcome.Replayed"/>.
/// </param>
/// <param name="LeaseExpiresAt">
/// When the standing reservation lapses, set only when <paramref name="Outcome"/> is
/// <see cref="IdempotencyBeginOutcome.InProgress"/>. It is what bounds the <c>Retry-After</c>, so a
/// caller is never told to wait past the point at which the key becomes retryable anyway.
/// </param>
public sealed record IdempotencyBeginResult(
    IdempotencyBeginOutcome Outcome,
    IdempotencyResponseSnapshot? Response = null,
    DateTimeOffset? LeaseExpiresAt = null);
