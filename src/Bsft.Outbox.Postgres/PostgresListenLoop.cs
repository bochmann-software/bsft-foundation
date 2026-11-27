using Bsft.Core.Polling;
using Microsoft.Extensions.Logging;

namespace Bsft.Outbox.Postgres;

/// <summary>
/// The reconnecting loop of <see cref="IPostgresListenLoop"/>, run on the <see cref="PollLoop"/> of the
/// rest of the framework with one listening session as its pass.
/// </summary>
/// <remarks>
/// What a wake-up does when it is woken is the caller's: the loop hands every callback of the session
/// to <c>onNotified</c> and reports what happened to the connection in the caller's own logger. The
/// start gates are awaited before the first session.
/// </remarks>
public sealed class PostgresListenLoop : IPostgresListenLoop
{
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="PostgresListenLoop"/> class.</summary>
    /// <param name="timeProvider">The clock the reconnect delay runs on.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is null.</exception>
    public PostgresListenLoop(TimeProvider timeProvider) =>
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc />
    public Task RunAsync(
        Func<Func<CancellationToken, Task>, CancellationToken, Task> listen,
        Func<CancellationToken, Task> onNotified,
        TimeSpan reconnectDelay,
        ILogger logger,
        CancellationToken stoppingToken = default)
    {
        ArgumentNullException.ThrowIfNull(listen);
        ArgumentNullException.ThrowIfNull(onNotified);
        ArgumentNullException.ThrowIfNull(logger);

        // One session is one pass. It ends only when the connection does, so the loop always waits the
        // reconnect delay rather than reconnecting straight into a server that just refused it.
        var session = new ListenSessionPass(listen, onNotified);

        return new PollLoop(session.RunAsync, new Reporter(logger, reconnectDelay), reconnectDelay, _timeProvider)
            .RunAsync(stoppingToken);
    }

    /// <summary>One listening session, as the pass the poll loop repeats.</summary>
    private sealed class ListenSessionPass
    {
        private readonly Func<Func<CancellationToken, Task>, CancellationToken, Task> _listen;
        private readonly Func<CancellationToken, Task> _onNotified;

        public ListenSessionPass(
            Func<Func<CancellationToken, Task>, CancellationToken, Task> listen, Func<CancellationToken, Task> onNotified)
        {
            _listen = listen;
            _onNotified = onNotified;
        }

        public async Task<PollPassOutcome> RunAsync(CancellationToken cancellationToken)
        {
            await _listen(_onNotified, cancellationToken).ConfigureAwait(false);

            return PollPassOutcome.WaitForInterval;
        }
    }

    /// <summary>Says in the caller's logger what happened to the session.</summary>
    private sealed class Reporter : IPollReporter
    {
        private readonly ILogger _logger;
        private readonly TimeSpan _reconnectDelay;

        public Reporter(ILogger logger, TimeSpan reconnectDelay)
        {
            _logger = logger;
            _reconnectDelay = reconnectDelay;
        }

        /// <inheritdoc />
        public void ReportPassFailed(Exception failure) =>
            _logger.LogError(
                failure,
                "The outbox notification listener lost its session; it reconnects in {ReconnectDelay}. Rows keep being drained by the poll in the meantime.",
                _reconnectDelay);

        /// <inheritdoc />
        public void ReportPassCanceledWithoutStop() =>
            _logger.LogError(
                "The outbox notification listener was canceled although the worker was not asked to stop; it reconnects in {ReconnectDelay}.",
                _reconnectDelay);

        /// <inheritdoc />
        public void ReportStopping() => _logger.LogDebug("Outbox notification listener stopping.");
    }
}
