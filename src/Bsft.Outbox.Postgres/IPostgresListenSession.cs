namespace Bsft.Outbox.Postgres;

/// <summary>
/// One listening session against a PostgreSQL database: it subscribes to a notification channel on a
/// connection of its own and calls back whenever something was announced on it.
/// </summary>
/// <remarks>
/// A session ends when the token is cancelled or when the connection behind it fails, and it is the
/// caller that decides what happens then. Splitting it that way keeps the reconnect policy, the part
/// with a decision in it, apart from the driver calls.
/// </remarks>
public interface IPostgresListenSession
{
    /// <summary>Listens until the session ends.</summary>
    /// <param name="connectionString">The connection the session opens for itself, taken from the pool of this connection string and held for the whole session (one pool slot; use another connection string or <c>Pooling=false</c> to keep it out of the request pool).</param>
    /// <param name="channel">The channel to listen on; already validated by its type.</param>
    /// <param name="onNotified">
    /// Called once as soon as the subscription is in place, and again for every notification received
    /// afterwards. The first call is not an optimisation: it is what covers the rows that were written
    /// while nothing was listening, and it happens after the subscription rather than before it so that
    /// a commit landing in between costs a second drain rather than a missed one.
    /// </param>
    /// <param name="cancellationToken">A cancellation token that ends the session.</param>
    /// <returns>A task that completes when the session ended.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="connectionString"/> or <paramref name="onNotified"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the channel is the default value, which no session accepts.</exception>
    Task ListenAsync(
        string connectionString,
        PostgresNotificationChannel channel,
        Func<CancellationToken, Task> onNotified,
        CancellationToken cancellationToken = default);
}
