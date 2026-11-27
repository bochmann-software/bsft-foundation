using Bsft.Core.Polling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Outbox;

/// <summary>
/// The hosted loop that drains one outbox: it drains the backlog to empty and
/// then waits for the next wake signal or the poll interval, whichever comes first.
/// </summary>
/// <typeparam name="TPayload">What the rows of this outbox carry.</typeparam>
/// <remarks>
/// <para>
/// <b>Shutdown grace.</b> A drain pass does not run on the host's stopping token. It runs on a token of
/// its own that is cancelled only <see cref="BsftOutboxDrainWorkerOptions.ShutdownGrace"/> after the
/// host stopped, so a row whose side effect is already running still gets to its processed mark. With
/// the stopping token passed straight through, a shutdown cancelled the pass in the middle of a row and
/// the next instance applied the effect a second time. No new pass starts once the host has stopped.
/// </para>
/// <para>
/// <b>Wake.</b> With an <see cref="IOutboxWakeSignal"/> registered the idle wait ends early when it is
/// raised. Without one, or when it never comes, the worker drains every
/// <see cref="BsftOutboxOptions.PollInterval"/>, which is also what keeps a lost signal harmless.
/// </para>
/// <para>
/// The loop is the <see cref="PollLoop"/> of the rest of the framework: a failing pass is reported and
/// retried after the poll interval, and only the stopping token ends the worker.
/// </para>
/// </remarks>
internal sealed class OutboxDrainWorker<TPayload> : BackgroundService, IPollReporter
{
    private readonly IOutboxDrainer<TPayload> _drainer;
    private readonly BsftOutboxOptions _outboxOptions;
    private readonly BsftOutboxDrainWorkerOptions _workerOptions;
    private readonly IOutboxWakeSignal? _wakeSignal;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly IServiceScopeFactory? _passScopes;

    /// <summary>Initializes a new instance of the <see cref="OutboxDrainWorker{TPayload}"/> class.</summary>
    /// <param name="drainer">The drain pass this worker repeats.</param>
    /// <param name="outboxOptions">The bounds of the outbox, read for the poll interval.</param>
    /// <param name="workerOptions">The worker's own options, read for the shutdown grace.</param>
    /// <param name="wakeSignal">The signal that ends the idle wait early, or <see langword="null"/> for polling alone.</param>
    /// <param name="timeProvider">The clock the idle wait and the retry delay run on.</param>
    /// <param name="logger">Logger used to report what the loop has to say.</param>
    /// <param name="passScopes">
    /// Creates the scope of each pass when an <see cref="IOutboxDrainPass{TPayload}"/> is registered, or
    /// <see langword="null"/> to drain without a seam and without a scope.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public OutboxDrainWorker(
        IOutboxDrainer<TPayload> drainer,
        IOptions<BsftOutboxOptions> outboxOptions,
        IOptions<BsftOutboxDrainWorkerOptions> workerOptions,
        IOutboxWakeSignal? wakeSignal,
        TimeProvider timeProvider,
        ILogger logger,
        IServiceScopeFactory? passScopes = null)
    {
        _drainer = drainer ?? throw new ArgumentNullException(nameof(drainer));
        _outboxOptions = (outboxOptions ?? throw new ArgumentNullException(nameof(outboxOptions))).Value;
        _workerOptions = (workerOptions ?? throw new ArgumentNullException(nameof(workerOptions))).Value;
        _wakeSignal = wakeSignal;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _passScopes = passScopes;
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunLoopAsync(stoppingToken);

    /// <summary>Runs the drain passes and the waits between them until the stopping token is cancelled.</summary>
    /// <param name="stoppingToken">The token that stops the loop when cancelled.</param>
    /// <returns>A task that completes when the loop has unwound.</returns>
    internal Task RunLoopAsync(CancellationToken stoppingToken = default) =>
        new PollLoop(PassAsync, this, _outboxOptions.PollInterval, _timeProvider).RunAsync(stoppingToken);

    /// <summary>
    /// One pass of the loop: the backlog, then the wait. The wait is part of the pass rather than of the
    /// loop around it because the loop can only sleep for an interval, and this one also ends on a signal.
    /// </summary>
    private async Task<PollPassOutcome> PassAsync(CancellationToken stoppingToken)
    {
        await DrainToEmptyAsync(stoppingToken).ConfigureAwait(false);
        await WaitForWorkAsync(stoppingToken).ConfigureAwait(false);

        return PollPassOutcome.PollAgain;
    }

    /// <summary>
    /// Drains until a pass applies nothing. The stopping token only decides whether another row or pass
    /// is started; the row in flight runs on the pass token that outlives the stop by the grace.
    /// </summary>
    /// <remarks>
    /// A pass claims at most one batch, so one wake behind more rows than a batch holds would otherwise
    /// leave the rest to the next poll. A pass that applied nothing is the outbox saying it is empty or
    /// that another instance has claimed what is left, and both are reasons to stop. A stop ends the
    /// loop after the pass in flight, never in the middle of the backlog.
    /// </remarks>
    private async Task DrainToEmptyAsync(CancellationToken stoppingToken)
    {
        using var pass = new CancellationTokenSource();
        await using var graceStart = stoppingToken
            .Register(() => pass.CancelAfter(_workerOptions.ShutdownGrace))
            .ConfigureAwait(false);

        int applied;

        do
        {
            applied = await DrainOnceAsync(() => !stoppingToken.IsCancellationRequested, pass.Token).ConfigureAwait(false);
        }
        while (applied > 0 && !stoppingToken.IsCancellationRequested);
    }

    /// <summary>
    /// One round, straight through the drainer, or through the pass of a fresh scope when the host
    /// registered an <see cref="IOutboxDrainPass{TPayload}"/>.
    /// </summary>
    private async Task<int> DrainOnceAsync(Func<bool> mayStartRow, CancellationToken passToken)
    {
        if (_passScopes is null)
            return await _drainer.DrainOnceAsync(mayStartRow, passToken).ConfigureAwait(false);

        var scope = _passScopes.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            return await scope.ServiceProvider
                .GetRequiredService<IOutboxDrainPass<TPayload>>()
                .RunAsync(
                    scope.ServiceProvider,
                    roundToken => _drainer.DrainOnceAsync(mayStartRow, roundToken),
                    passToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Waits for the wake signal or the poll interval, whichever comes first; a stop ends it with a cancellation.</summary>
    private async Task WaitForWorkAsync(CancellationToken stoppingToken)
    {
        if (_wakeSignal is null)
        {
            await Task.Delay(_outboxOptions.PollInterval, _timeProvider, stoppingToken).ConfigureAwait(false);

            return;
        }

        using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var interval = Task.Delay(_outboxOptions.PollInterval, _timeProvider, wait.Token);
        var signalled = _wakeSignal.WaitAsync(wait.Token);

        var first = await Task.WhenAny(interval, signalled).ConfigureAwait(false);

        // The other half of the race is abandoned: cancelled here, and a cancelled task has nothing
        // left to observe. The winner is awaited so that a stop reaches the loop as the cancellation it is.
        await wait.CancelAsync().ConfigureAwait(false);
        await first.ConfigureAwait(false);
    }

    /// <inheritdoc />
    void IPollReporter.ReportPassFailed(Exception failure) =>
        _logger.LogError(
            failure,
            "An outbox drain pass failed; the worker tries again in {PollInterval}. Rows stay pending until a pass succeeds.",
            _outboxOptions.PollInterval);

    /// <inheritdoc />
    void IPollReporter.ReportPassCanceledWithoutStop() =>
        _logger.LogError(
            "An outbox drain pass was canceled although the worker was not asked to stop; the worker tries again in {PollInterval}.",
            _outboxOptions.PollInterval);

    /// <inheritdoc />
    void IPollReporter.ReportStopping() => _logger.LogDebug("Outbox drain worker stopping.");
}
