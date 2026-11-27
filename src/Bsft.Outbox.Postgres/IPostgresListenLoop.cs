using Microsoft.Extensions.Logging;

namespace Bsft.Outbox.Postgres;

/// <summary>
/// The listen-and-reconnect loop every PostgreSQL wake-up runs: one listening session as the pass of a
/// poll loop, restarted after a pause whenever the session ends.
/// </summary>
/// <remarks>
/// A session that ends, because the server went away, the connection was cut or the first connect never
/// succeeded, is a pass that finished: the loop waits the reconnect delay and starts another. It is the
/// contract of the rest of the framework's background services: a pass may fail in any way it likes
/// without ending the service, and only the stopping token ends the loop.
/// </remarks>
public interface IPostgresListenLoop
{
    /// <summary>Runs listening sessions until the stopping token is cancelled.</summary>
    /// <param name="listen">Runs one session: given the callback and the token, it returns when the session ended.</param>
    /// <param name="onNotified">Called as the session calls back: once after the subscription and for every notification.</param>
    /// <param name="reconnectDelay">How long the loop waits before it starts another session.</param>
    /// <param name="logger">Logger used to report sessions that ended.</param>
    /// <param name="stoppingToken">The token that stops the loop when cancelled.</param>
    /// <returns>A task that completes when the loop has unwound.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    Task RunAsync(
        Func<Func<CancellationToken, Task>, CancellationToken, Task> listen,
        Func<CancellationToken, Task> onNotified,
        TimeSpan reconnectDelay,
        ILogger logger,
        CancellationToken stoppingToken = default);
}
