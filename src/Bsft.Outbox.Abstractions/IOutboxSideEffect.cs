namespace Bsft.Outbox;

/// <summary>
/// What a drain pass does with a row before it marks it: publish it, send it, apply it. This is the
/// one thing that differs between two outboxes of the same host.
/// </summary>
/// <typeparam name="TPayload">What the rows of this outbox carry.</typeparam>
/// <remarks>
/// <para>
/// It has to be idempotent. The row is marked processed only after this returns, so a crash in
/// between leaves the row pending and this is called again with the same payload — the price
/// at-least-once pays for never losing a row.
/// </para>
/// <para>
/// It must not swallow its own failure. A drain pass decides between another attempt and retirement
/// from the exception it observes here, so an effect that reports success after failing turns a
/// retryable problem into a silently dropped row.
/// </para>
/// </remarks>
public interface IOutboxSideEffect<TPayload>
{
    /// <summary>Applies the effect the row stands for.</summary>
    /// <param name="payload">What the row carries.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task that completes once the effect has been applied.</returns>
    Task ApplyAsync(TPayload payload, CancellationToken cancellationToken = default);

    /// <summary>Names one payload for a log line, without quoting what it carries.</summary>
    /// <param name="payload">What the row carries.</param>
    /// <returns>A short identification of the payload, safe to log.</returns>
    /// <remarks>
    /// The drainer logs every failure and every retirement, and a row identity alone rarely tells an
    /// operator which of them matters. What may be said about a payload is the owning package's
    /// decision, not the kernel's, so the kernel asks rather than guesses — and a payload that has
    /// nothing safe to say answers with its own kind.
    /// </remarks>
    string Describe(TPayload payload);
}
