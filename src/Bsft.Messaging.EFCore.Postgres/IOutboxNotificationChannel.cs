namespace Bsft.Messaging.EFCore.Postgres;

/// <summary>
/// One listening session against the database: it subscribes to the outbox notification channel and
/// calls back whenever a commit announced itself.
/// </summary>
/// <remarks>
/// A session ends when the token is cancelled or when the connection behind it fails, and it is the
/// worker above that decides what happens then. Splitting it that way is what keeps the reconnect
/// policy — the part with a decision in it — separate from the driver call that cannot be exercised
/// without a live server.
/// </remarks>
internal interface IOutboxNotificationChannel
{
    /// <summary>
    /// Listens until the session ends.
    /// </summary>
    /// <param name="onNotified">
    /// Called once as soon as the subscription is in place, and again for every notification received
    /// afterwards. The first call is not an optimisation: it is what covers the rows that were written
    /// while nothing was listening, and it happens after the subscription rather than before it so that
    /// a commit landing in between costs a second drain rather than a missed one.
    /// </param>
    /// <param name="cancellationToken">A cancellation token that ends the session.</param>
    /// <returns>A task that completes when the session ended.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="onNotified"/> is null.</exception>
    Task ListenAsync(Func<CancellationToken, Task> onNotified, CancellationToken cancellationToken = default);
}
