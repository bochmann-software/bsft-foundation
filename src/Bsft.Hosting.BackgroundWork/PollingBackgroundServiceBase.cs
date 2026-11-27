using Bsft.Core.Polling;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bsft.Hosting.BackgroundWork;

/// <summary>
/// Base class for a hosted service that does one pass of work over and over: the derived service writes
/// the pass, and this class owns the loop, the wait between passes, the logging of a failed pass and the
/// clean stop.
/// </summary>
/// <remarks>
/// <para>
/// The loop is the <see cref="PollLoop"/> of the rest of the framework, so the contract is its contract: a
/// pass may fail in any way it likes without ending the service, a failed pass is logged and tried again
/// after <see cref="PollInterval"/>, and only the host stopping ends the loop, without an error in the log.
/// A pass that ends cancelled while the host is not stopping (a command timeout, typically) counts as a
/// failed pass rather than as a shutdown.
/// </para>
/// <para>
/// A pass returns a <see cref="bool"/>, which is the <c>Try</c> shape: <see langword="true"/> when it did work
/// and polls again at once, <see langword="false"/> when it found nothing and waits <see cref="PollInterval"/>.
/// The base maps it to the <see cref="PollPassOutcome"/> that <see cref="PollLoop"/> takes
/// (<see cref="PollPassOutcome.PollAgain"/> and <see cref="PollPassOutcome.WaitForInterval"/>).
/// </para>
/// <para>
/// <see cref="PollInterval"/> and <see cref="Enabled"/> are read once, when the service starts executing.
/// A <see cref="PollInterval"/> that is not greater than zero, which includes <see cref="Timeout.InfiniteTimeSpan"/>,
/// faults the service before <see cref="OnStartingAsync"/> runs. Execution yields to the host before the start work,
/// so work that completes without awaiting never holds up the start of the other services.
/// A failure of <see cref="OnStartingAsync"/> is not retried: it faults the service, which is what the host's
/// <c>BackgroundServiceExceptionBehavior</c> decides about. Registered with
/// <c>AddBsftGatedHostedService</c> the service starts, and so reads and runs all of the above, only after
/// the start gates have opened.
/// </para>
/// </remarks>
public abstract class PollingBackgroundServiceBase : BackgroundService
{
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="PollingBackgroundServiceBase"/> class.
    /// </summary>
    /// <param name="logger">Receives what the loop has to say; the category of the derived service is kept.</param>
    /// <param name="timeProvider">
    /// The clock the wait between passes runs on, so a test drives the interval instead of waiting it out;
    /// the system clock when <see langword="null"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is null.</exception>
    protected PollingBackgroundServiceBase(ILogger logger, TimeProvider? timeProvider = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Gets how long the loop waits after a pass that asked to wait, and after a failed pass.
    /// </summary>
    protected abstract TimeSpan PollInterval { get; }

    /// <summary>
    /// Gets a value indicating whether the service runs at all. When <see langword="false"/>, execution
    /// ends at once, with one line in the log, without calling <see cref="OnStartingAsync"/>. The default
    /// is <see langword="true"/>.
    /// </summary>
    protected virtual bool Enabled => true;

    /// <summary>
    /// One-time work that runs before the first pass. The default does nothing.
    /// </summary>
    /// <param name="cancellationToken">A token that is cancelled when the host stops.</param>
    /// <returns>A task that completes when the work is done.</returns>
    protected virtual Task OnStartingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// One pass of work.
    /// </summary>
    /// <param name="cancellationToken">A token that is cancelled when the host stops.</param>
    /// <returns>
    /// <see langword="true"/> when the pass did work and the next one starts at once;
    /// <see langword="false"/> to wait <see cref="PollInterval"/> first.
    /// </returns>
    protected abstract Task<bool> TryTickAsync(CancellationToken cancellationToken);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var service = GetType().Name;

        if (!Enabled)
        {
            PollingBackgroundServiceLog.Disabled(_logger, service);

            return;
        }

        var interval = PollInterval;

        if (interval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{service} has a poll interval of {interval}, which would not wait between passes or never end the wait. A poll interval must be greater than zero and finite.");
        }

        // The host starts its services one after the other: whatever the start work and the first pass do before
        // their first await would hold the start of every service behind this one.
        await Task.Yield();

        await OnStartingAsync(stoppingToken).ConfigureAwait(false);

        var reporter = new LogReporter(_logger, service);
        await new PollLoop(PassAsync, reporter, interval, _timeProvider).RunAsync(stoppingToken).ConfigureAwait(false);
    }

    /// <summary>Runs one pass and maps what it says to what the loop does next.</summary>
    private async Task<PollPassOutcome> PassAsync(CancellationToken cancellationToken) =>
        await TryTickAsync(cancellationToken).ConfigureAwait(false) ? PollPassOutcome.PollAgain : PollPassOutcome.WaitForInterval;

    /// <summary>Phrases what the loop reports as log events of the derived service's logger.</summary>
    private sealed class LogReporter(ILogger logger, string service) : IPollReporter
    {
        /// <inheritdoc />
        public void ReportPassFailed(Exception failure) => PollingBackgroundServiceLog.PassFailed(logger, failure, service);

        /// <inheritdoc />
        public void ReportPassCanceledWithoutStop() => PollingBackgroundServiceLog.PassCanceledWithoutStop(logger, service);

        /// <inheritdoc />
        public void ReportStopping() => PollingBackgroundServiceLog.Stopping(logger, service);
    }
}
