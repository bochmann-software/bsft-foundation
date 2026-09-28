using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.BackgroundWork;

/// <summary>
/// Base class for a startup operation that has to happen exactly once and has to keep trying until it
/// does: it attempts the operation, backs off after a failure and comes back, and stops for one of
/// two reasons only — the operation succeeded, or the host is going away.
/// </summary>
/// <remarks>
/// <para>
/// The shape a host reaches for over and over: apply a migration, register the instance somewhere,
/// prime a cache, seed a lookup table. Written by hand it is a <c>while</c> loop with a
/// <c>try</c>/<c>catch</c> in it, and the two things it always gets wrong are the same two: a
/// cancellation from inside the operation read as a shutdown, so the host runs on with the work never
/// done and nothing in the log, and a synchronous throw out of the operation that escapes
/// <c>ExecuteAsync</c> and takes the process down under the default
/// <c>BackgroundServiceExceptionBehavior</c>.
/// </para>
/// <para>
/// Both are handled here by inspecting the attempt through its task rather than catching it, which is
/// the same discipline <see cref="Bsft.Core.Polling.PollLoop"/> applies to a loop that never ends.
/// That loop is not reused: it polls forever on a fixed interval, and this one exits on success and
/// widens its delay after every failure, so the two share their reasoning rather than their code.
/// </para>
/// <para>
/// Waiting goes through the injected <see cref="TimeProvider"/>, so a test drives the whole backoff
/// schedule on virtual time instead of sleeping through it.
/// </para>
/// </remarks>
public abstract class RunOnceWithRetryBackgroundServiceBase : BackgroundService
{
    private readonly BsftRunOnceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly string _operationName;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunOnceWithRetryBackgroundServiceBase"/> class.
    /// </summary>
    /// <param name="options">Supplies this service's named options.</param>
    /// <param name="timeProvider">Drives the startup delay and the backoff.</param>
    /// <param name="logger">Records the attempts and their outcome.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <remarks>
    /// The options are read from the runtime type of the instance, so a derived service finds the
    /// options its own registration configured without restating its name.
    /// </remarks>
    protected RunOnceWithRetryBackgroundServiceBase(
        IOptionsMonitor<BsftRunOnceOptions> options,
        TimeProvider timeProvider,
        ILogger logger)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _operationName = BsftRunOnceOptions.NameFor(GetType());
        _options = options.Get(_operationName);
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunUntilSucceededAsync(stoppingToken);

    /// <summary>
    /// The operation itself. It succeeds by returning and fails by throwing; every failure is another
    /// attempt after the backoff.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that is cancelled when the host stops.</param>
    /// <returns>A task that completes when the operation has succeeded.</returns>
    /// <remarks>
    /// It runs outside any scope: a derived service that needs scoped dependencies resolves them per
    /// attempt with <see cref="ServiceScopeFactoryExtensions.RunScopedAsync{TService}"/>, so a failed
    /// attempt cannot leave a poisoned <c>DbContext</c> behind for the next one.
    /// </remarks>
    protected abstract Task RunOnceAsync(CancellationToken cancellationToken);

    /// <summary>Attempts the operation until it succeeds, gives up or the host stops.</summary>
    /// <param name="stoppingToken">The token that is cancelled when the host stops.</param>
    /// <returns>A task that completes when there is nothing left to attempt.</returns>
    internal async Task RunUntilSucceededAsync(CancellationToken stoppingToken = default)
    {
        if (!await TryDelayAsync(_options.StartupDelay, stoppingToken).ConfigureAwait(false))
        {
            LogStopping();
            return;
        }

        var delay = _options.InitialRetryDelay;

        for (var attempt = 1; !stoppingToken.IsCancellationRequested; attempt++)
        {
            var operation = AttemptAsync(stoppingToken);
            await Task.WhenAny(operation).ConfigureAwait(false);

            if (operation.IsCompletedSuccessfully)
            {
                _logger.LogInformation(
                    "The startup operation {StartupOperation} succeeded on attempt {Attempt}.", _operationName, attempt);

                return;
            }

            // A cancellation while the host is stopping is a shutdown, not a verdict on the operation:
            // there is nothing to report and the work is left for the next start of the process.
            if (operation.IsCanceled && stoppingToken.IsCancellationRequested)
                break;

            LogAttemptFailed(operation, attempt);

            if (_options.MaxAttempts > 0 && attempt >= _options.MaxAttempts)
            {
                _logger.LogError(
                    "The startup operation {StartupOperation} gave up after {Attempt} attempts; it will not be attempted again before the next start.",
                    _operationName,
                    attempt);

                return;
            }

            if (!await TryDelayAsync(delay, stoppingToken).ConfigureAwait(false))
                break;

            delay = NextDelay(delay);
        }

        LogStopping();
    }

    /// <summary>
    /// Invokes the operation from inside an async method, so that an implementation which throws
    /// synchronously — before ever returning a task — still surfaces as a faulted task the loop can
    /// inspect, rather than escaping past it and taking the host down.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The task the attempt runs as.</returns>
    private async Task AttemptAsync(CancellationToken cancellationToken) => await RunOnceAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Waits, and says whether the wait ran to its end rather than being cut short by a shutdown.
    /// </summary>
    /// <param name="delay">How long to wait. A non-positive delay returns at once.</param>
    /// <param name="stoppingToken">The token that is cancelled when the host stops.</param>
    /// <returns><c>true</c> when the wait finished, <c>false</c> when the host is stopping.</returns>
    private async Task<bool> TryDelayAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        if (delay <= TimeSpan.Zero)
            return !stoppingToken.IsCancellationRequested;

        // Observed rather than awaited directly, so that a shutdown during the backoff is an answer
        // this method returns instead of an exception the caller has to catch.
        var wait = Task.Delay(delay, _timeProvider, stoppingToken);
        await Task.WhenAny(wait).ConfigureAwait(false);

        return wait.IsCompletedSuccessfully;
    }

    /// <summary>Widens the delay by the configured factor, up to the configured cap.</summary>
    /// <param name="current">The delay that was just waited out.</param>
    /// <returns>The delay for the next failure.</returns>
    /// <remarks>
    /// The multiplication is done on ticks and saturated, because <c>TimeSpan * double</c> throws on
    /// overflow: a long initial delay and a large factor would otherwise take the whole service down
    /// with an <see cref="OverflowException"/> on a later attempt, long after the configuration that
    /// caused it was accepted, instead of simply arriving at the cap the next line applies anyway.
    /// </remarks>
    private TimeSpan NextDelay(TimeSpan current)
    {
        var ticks = current.Ticks * _options.BackoffFactor;

        return ticks >= _options.MaxRetryDelay.Ticks ? _options.MaxRetryDelay : TimeSpan.FromTicks((long)ticks);
    }

    /// <summary>Reports an attempt that did not succeed, in the terms it did not succeed in.</summary>
    /// <param name="operation">The finished attempt.</param>
    /// <param name="attempt">The one-based number of the attempt.</param>
    private void LogAttemptFailed(Task operation, int attempt)
    {
        if (operation.Exception is { } failure)
        {
            _logger.LogError(
                failure,
                "The startup operation {StartupOperation} failed on attempt {Attempt}; it will be attempted again.",
                _operationName,
                attempt);

            return;
        }

        // Cancelled by a token of its own — a command timeout, typically — while nobody asked the host
        // to stop. Reading that as a shutdown would leave the host up with the operation never done,
        // so it counts as a failed attempt like any other.
        _logger.LogError(
            "The startup operation {StartupOperation} was canceled on attempt {Attempt} although the host was not stopping; it will be attempted again.",
            _operationName,
            attempt);
    }

    /// <summary>Reports that the service is unwinding because the host is stopping.</summary>
    private void LogStopping() =>
        _logger.LogDebug(
            "The startup operation {StartupOperation} is stopping before it succeeded; the host is going away.",
            _operationName);
}
