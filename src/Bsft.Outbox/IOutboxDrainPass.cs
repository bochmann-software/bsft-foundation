namespace Bsft.Outbox;

/// <summary>
/// A seam around each drain pass of the hosted drain worker of one outbox: work that has to happen
/// before the batch is leased and after it was applied, such as opening a connection the side effect
/// uses so that an unreachable server costs no delivery attempt, or purging what the pass retired.
/// </summary>
/// <typeparam name="TPayload">What the rows of this outbox carry.</typeparam>
/// <remarks>
/// <para>
/// The worker creates a service scope for every pass and resolves the implementation from it, so a
/// scoped registration (<c>AddBsftOutboxDrainPass</c>) lives for exactly one pass. Without an
/// implementation the worker drains as it always did and creates no scope.
/// </para>
/// <para>
/// A pass is one lease-and-apply round, the unit the worker repeats until a round applies nothing. The
/// implementation decides whether and when the round runs by calling the delegate it is given, and it
/// may return without calling it, in which case nothing is leased. An exception leaves the pass the
/// way an exception from the drain does: it is reported, and the worker tries again after the poll
/// interval.
/// </para>
/// </remarks>
public interface IOutboxDrainPass<TPayload>
{
    /// <summary>Runs one drain pass, wrapping the round that leases and applies a batch.</summary>
    /// <param name="serviceProvider">The service provider of the scope this pass runs in.</param>
    /// <param name="drainOnce">
    /// The round itself, with all of the worker's semantics: the start gate of the stopping token, the
    /// lease, the attempts and the retry schedule. It returns the number of rows it applied.
    /// </param>
    /// <param name="cancellationToken">
    /// The token of the pass, which outlives the host's stop by the shutdown grace.
    /// </param>
    /// <returns>
    /// The number of rows applied. The worker drains again while it is positive, so a pass returns what
    /// <paramref name="drainOnce"/> returned, or zero when it did not run it.
    /// </returns>
    Task<int> RunAsync(
        IServiceProvider serviceProvider,
        Func<CancellationToken, Task<int>> drainOnce,
        CancellationToken cancellationToken);
}
