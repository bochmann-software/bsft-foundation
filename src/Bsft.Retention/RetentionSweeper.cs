using Bsft.Core.Polling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Retention;

/// <summary>
/// Background service that runs one <see cref="IRetentionPass"/> over and over: it computes the
/// cutoff, drains the backlog a batch at a time, paces the batches and offsets its first sweep.
/// </summary>
/// <typeparam name="TPass">The retention pass this sweeper drives.</typeparam>
/// <remarks>
/// One sweeper per pass, so the passes of a host neither share a schedule nor block one another. The
/// generic parameter is what keeps them apart in the container, and it is also how the sweeper finds
/// its own named <see cref="BsftRetentionOptions"/>.
/// <para>
/// The first sweep waits for the host to have started: the sweeper takes part in the hosted-service
/// lifecycle and begins only once <see cref="StartedAsync"/> has been called, which the host does after
/// the <c>StartingAsync</c> and <c>StartAsync</c> of every hosted service have completed. A hosted
/// service that creates or migrates the database the pass deletes from has therefore finished before the
/// first batch runs, and a host that never completes its start-up never sweeps. The startup stagger is
/// measured from that point.
/// </para>
/// <para>
/// A stop drains rather than aborts. From <see cref="StoppingAsync"/> (or <see cref="StopAsync"/>, for a
/// host that does not call it) the batch in flight runs to completion and no further batch or sweep
/// starts. The batch's own cancellation token is cancelled only when the token handed to
/// <see cref="StopAsync"/> fires, which is the host's shutdown timeout elapsing. The cancellation is
/// signalled without waiting for the token's callbacks, so <see cref="StopAsync"/> returns at that
/// timeout however long a store takes to cancel its command. Whatever the batch ends in once its token
/// was cancelled that way is logged at debug rather than as an error, because cancellation on shutdown
/// is not a failure; a failure before the timeout is still an error.
/// </para>
/// <para>
/// The sweeper's two own cancellation sources are cancelled exactly once, with
/// <see cref="CancellationTokenSource.CancelAsync"/>, and deliberately never disposed: disposing them is
/// what lost callbacks and threw from linked sources before. Neither ever gets a timer or a wait handle,
/// so they hold nothing unmanaged; the comment on the fields gives the reasoning and the rule that keeps
/// it true.
/// </para>
/// </remarks>
public sealed class RetentionSweeper<TPass> : BackgroundService, IHostedLifecycleService, IPollReporter
    where TPass : class, IRetentionPass
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // The two sources below are deliberately never disposed. The general guidance is the opposite:
    // "Always call Dispose before you release your last reference to the CancellationTokenSource"
    // (CancellationTokenSource.Dispose, https://learn.microsoft.com/dotnet/api/system.threading.cancellationtokensource.dispose)
    // and "be sure to call the CancellationTokenSource.Dispose method when you have finished using the
    // cancellation token source" (Cancellation in Managed Threads,
    // https://learn.microsoft.com/dotnet/standard/threading/cancellation-in-managed-threads).
    //
    // This is a deliberate exception. Dispose releases only a timer (one created by CancelAfter or the
    // delay constructors) and the kernel wait handle that reading Token.WaitHandle allocates, and drops
    // the registration list. The CancellationToken.WaitHandle remarks say so for the handle ("disposing
    // the source will dispose of this allocated handle",
    // https://learn.microsoft.com/dotnet/api/system.threading.cancellationtoken.waithandle), and the
    // runtime source of CancellationTokenSource.Dispose(bool) shows nothing else is released
    // (https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Threading/CancellationTokenSource.cs).
    // These sources are created without a delay and neither has its wait handle read, so they hold
    // nothing unmanaged, and the garbage collector reclaims them with the sweeper.
    //
    // Not disposing them is what closes two failures disposal caused: callbacks that had not run yet
    // being dropped when the source was disposed under a pending cancellation (a store's command
    // cancellation among them), and ObjectDisposedException from CreateLinkedTokenSource on a token whose
    // source had been disposed.
    //
    // Rule for maintainers: never read WaitHandle on either source or its token, and never call
    // CancelAfter on them or create them with a delay. Either would give them something to dispose. A
    // test pins that neither has a timer or a wait handle after a full start, stop and dispose.

    /// <summary>Cancelled when the host asks the sweeper to stop: no further batch or sweep starts.</summary>
    private readonly CancellationTokenSource _stopRequested = new();

    /// <summary>
    /// Cancelled when the host's shutdown timeout has elapsed: the token the batch in flight observes.
    /// </summary>
    private readonly CancellationTokenSource _shutdownElapsed = new();

    /// <summary>
    /// The token of <see cref="_stopRequested"/>, read once, so nothing but the one cancellation below
    /// touches the source.
    /// </summary>
    private readonly CancellationToken _stopRequestedToken;

    /// <summary>The token of <see cref="_shutdownElapsed"/>, read once for the same reason.</summary>
    private readonly CancellationToken _shutdownElapsedToken;

    /// <summary>The one cancellation of <see cref="_stopRequested"/>, started by whoever needs it first.</summary>
    private readonly Lazy<Task> _stopCancellation;

    /// <summary>The one cancellation of <see cref="_shutdownElapsed"/>, started by whoever needs it first.</summary>
    private readonly Lazy<Task> _shutdownCancellation;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BsftRetentionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RetentionSweeper<TPass>> _logger;
    private readonly string _passName;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetentionSweeper{TPass}"/> class.
    /// </summary>
    /// <param name="scopeFactory">Creates a scope per sweep, so a pass may use scoped services.</param>
    /// <param name="options">Supplies this pass's named options.</param>
    /// <param name="timeProvider">Works out the cutoff and drives the stagger and the pacing.</param>
    /// <param name="logger">Records what the sweeps deleted.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public RetentionSweeper(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<BsftRetentionOptions> options,
        TimeProvider timeProvider,
        ILogger<RetentionSweeper<TPass>> logger)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _passName = BsftRetentionOptions.NameFor<TPass>();
        _options = options.Get(_passName);
        _stopRequestedToken = _stopRequested.Token;
        _shutdownElapsedToken = _shutdownElapsed.Token;
        _stopCancellation = new(() => CancelObservedAsync(_stopRequested), LazyThreadSafetyMode.ExecutionAndPublication);
        _shutdownCancellation = new(() => CancelObservedAsync(_shutdownElapsed), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Lets the sweep begin: the host calls this once every hosted service has started.
    /// </summary>
    /// <param name="cancellationToken">Not used; nothing here waits.</param>
    /// <returns>A completed task.</returns>
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        _started.TrySetResult();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Begins the drain: the batch in flight runs to completion, and no further batch or sweep starts.
    /// </summary>
    /// <param name="cancellationToken">Not used; nothing here waits for the batch.</param>
    /// <returns>A task that completes once the stop has been signalled to the sweeper's own waits.</returns>
    /// <remarks>
    /// Awaiting the cancellation is safe here because every callback on that token is the sweeper's own
    /// and cheap: the drain sources linked to it, and the delays and waits they end. No pass ever sees it.
    /// </remarks>
    public Task StoppingAsync(CancellationToken cancellationToken) => _stopCancellation.Value;

    /// <summary>
    /// Stops the sweeper, letting the batch in flight finish until <paramref name="cancellationToken"/>
    /// fires and cancelling it then.
    /// </summary>
    /// <param name="cancellationToken">
    /// The host's shutdown token: it fires when the shutdown timeout has elapsed, and only then is the
    /// batch in flight cancelled.
    /// </param>
    /// <returns>
    /// A task that completes once the sweeper has unwound, or once <paramref name="cancellationToken"/>
    /// has fired, whichever is first.
    /// </returns>
    /// <remarks>
    /// The base class cancels the token handed to <see cref="ExecuteAsync"/> at the very start of a stop,
    /// which is why the batch observes a token of its own instead of that one.
    /// </remarks>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Also here, for a host that calls StopAsync without StoppingAsync.
        await StoppingAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Returns once the sweeper has unwound or once the shutdown token has fired, whichever is
            // first, so a stop never outlasts the host's shutdown timeout.
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Cancelled here rather than from a registration on the shutdown token: the base class
            // registers on that token too, and its callback runs first and lets this method return.
            // Not awaited: a store's callback on the batch token is its command cancellation, and that
            // can block (Npgsql opens a connection for the cancel request, with a connect timeout of its
            // own), which would hold the stop past the host's shutdown timeout.
            if (cancellationToken.IsCancellationRequested)
                _ = _shutdownCancellation.Value;
        }
    }

    /// <summary>
    /// Releases the sweeper. A sweeper disposed without being stopped cancels the batch in flight, the
    /// way the base class cancels its stopping token.
    /// </summary>
    /// <remarks>
    /// Both tokens read as cancelled once this returns, while their callbacks run on the thread pool and
    /// are not waited for. The sources themselves are not disposed; see the comment on the fields.
    /// </remarks>
    public override void Dispose()
    {
        _ = _stopCancellation.Value;
        _ = _shutdownCancellation.Value;
        base.Dispose();
    }

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The stopping token of the base class is cancelled by StopAsync and by Dispose, both of which
        // ask the sweeper to stop as well; the drain listens to either.
        using var drain = CancellationTokenSource.CreateLinkedTokenSource(_stopRequestedToken, stoppingToken);

        _logger.LogDebug(
            "The retention pass {RetentionPass} waits for the host to finish starting.", _passName);

        try
        {
            await _started.Task.WaitAsync(drain.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (drain.IsCancellationRequested)
        {
            // The host stopped before it had started. The sweeper never ran, which is not a failure.
            _logger.LogDebug(
                "The retention pass {RetentionPass} was not swept because the host stopped before it had started.",
                _passName);

            return;
        }

        // From here on the stopping token of the base class is not needed: StopAsync and Dispose, the two
        // places that cancel it, request the stop on the sweeper's own token first.
        await RunLoopAsync(_shutdownElapsedToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the sweep loop until the sweeper is asked to stop or <paramref name="cancellationToken"/> is
    /// cancelled.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token the batch in flight observes. Its cancellation stops the loop as well; a stop request
    /// alone lets the batch in flight finish and starts no further one.
    /// </param>
    /// <returns>A task that completes when the loop has unwound.</returns>
    internal async Task RunLoopAsync(CancellationToken cancellationToken = default)
    {
        using var drain = DrainFor(cancellationToken);

        TimeSpan? retention = null;

        // In absolute-expiry mode the records carry their own expiry, so the period is not read at
        // all and the pass is always swept; otherwise no period means no sweep.
        if (!_options.UseAbsoluteExpiry)
        {
            if (_options.RetentionPeriod is not { } period)
            {
                _logger.LogDebug(
                    "The retention pass {RetentionPass} is not swept because no retention period is configured; every record is kept indefinitely.",
                    _passName);

                return;
            }

            retention = period;
        }

        var stagger = RetentionStagger.OffsetFor(_passName, _options.MaxStartupStagger);

        if (stagger > TimeSpan.Zero)
        {
            _logger.LogDebug(
                "The retention pass {RetentionPass} starts sweeping in {StaggerDelay}.", _passName, stagger);

            try
            {
                await Task.Delay(stagger, _timeProvider, drain.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The host stopped inside the offset, before the loop ever started. There is nothing
                // to report and nothing to unwind: the sweeper simply never ran.
                return;
            }
        }

        // Sweeping at the retention itself keeps a short retention honest without a second option to
        // configure, and the cap keeps a long one from becoming a rare, enormous delete.
        var interval = retention is { } window && window < _options.SweepInterval ? window : _options.SweepInterval;

        // The loop stops on the drain, so a stop ends the wait between sweeps at once and starts no
        // further sweep, while the batch in flight observes the later shutdown token instead.
        await new PollLoop(_ => SweepAsync(retention, cancellationToken), this, interval, _timeProvider)
            .RunAsync(drain.Token)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    void IPollReporter.ReportPassFailed(Exception failure)
    {
        if (IsShutdownCancellation())
        {
            _logger.LogDebug(
                failure,
                "The retention sweep of {RetentionPass} ended in a failure after the host's shutdown timeout had cancelled it; it is read as that cancellation.",
                _passName);

            return;
        }

        _logger.LogError(
            failure,
            "The retention sweep of {RetentionPass} failed; it runs again after the interval.",
            _passName);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Decided from the sweeper's own tokens, not from the loop's: the loop's drain is a linked source
    /// that learns of a stop or a dispose in a callback on the thread pool, so a pass that throws on its
    /// cancelled token at once can end before the loop sees the stop. Only a cancellation with neither
    /// own token set is the self-cancellation this reports as an error.
    /// </remarks>
    void IPollReporter.ReportPassCanceledWithoutStop()
    {
        if (_stopRequestedToken.IsCancellationRequested || IsShutdownCancellation())
        {
            _logger.LogDebug(
                "The retention sweep of {RetentionPass} was canceled because the sweeper is stopping.",
                _passName);

            return;
        }

        _logger.LogError(
            "The retention sweep of {RetentionPass} was canceled although the sweeper was not asked to stop.",
            _passName);
    }

    /// <inheritdoc />
    void IPollReporter.ReportStopping() =>
        _logger.LogDebug("The retention sweeper of {RetentionPass} is stopping.", _passName);

    /// <summary>
    /// One sweep: batches against a single cutoff until the backlog is drained, the batch bound is
    /// reached or the host stops.
    /// </summary>
    /// <param name="retention">
    /// How long a record is kept. Null in absolute-expiry mode, where the period is not used.
    /// </param>
    /// <param name="cancellationToken">
    /// The token the batch in flight observes. Its cancellation, like a stop request, starts no further
    /// batch and ends the pause between two batches; a stop request alone lets the batch in flight finish.
    /// </param>
    /// <returns>Whether the loop should come straight back round.</returns>
    /// <remarks>
    /// In the default age mode the cutoff is computed once per sweep rather than once per batch, so a
    /// long drain deletes the set it set out to delete instead of a target that creeps forward under
    /// it — which is what makes the sweep terminate on a table receiving writes faster than one batch
    /// clears. In absolute-expiry mode the cutoff is the current time and is read again for every
    /// batch, so a record that expires while a long drain is running is purged by the same sweep. The
    /// sweep is still bounded by <see cref="BsftRetentionOptions.MaxBatchesPerSweep"/>: where records
    /// expire faster than the batches clear them, it returns <see cref="PollPassOutcome.PollAgain"/>
    /// every time, and <see cref="BsftRetentionOptions.BatchDelay"/> is what paces that drain.
    /// </remarks>
    internal async Task<PollPassOutcome> SweepAsync(TimeSpan? retention, CancellationToken cancellationToken = default)
    {
        using var drain = DrainFor(cancellationToken);

        var cutoff = CutoffFor(retention);

        using var scope = _scopeFactory.CreateScope();
        var pass = scope.ServiceProvider.GetRequiredService<TPass>();

        var total = 0;

        for (var batch = 0; batch < _options.MaxBatchesPerSweep; batch++)
        {
            // A stop lets the batch in flight finish but starts no further one; what is left is there
            // for the next start. Waiting rather than polling again, because the loop's wait ends as soon
            // as its own drain token follows, whereas polling again would start sweeps that do nothing.
            if (IsDrainRequested(cancellationToken))
            {
                LogSwept(total, cutoff);

                return PollPassOutcome.WaitForInterval;
            }

            if (_options.UseAbsoluteExpiry)
                cutoff = CutoffFor(retention);

            var purged = await pass.PurgeBatchAsync(_options.BatchSize, cutoff, cancellationToken).ConfigureAwait(false);
            total += purged;

            // A short batch is the backlog reporting itself drained; only a full one means more is
            // waiting behind it.
            if (purged < _options.BatchSize)
            {
                LogSwept(total, cutoff);

                return PollPassOutcome.WaitForInterval;
            }

            // A stop ends the pause early rather than failing the sweep, and the check above ends it.
            if (_options.BatchDelay > TimeSpan.Zero)
            {
                await Task.Delay(_options.BatchDelay, _timeProvider, drain.Token)
                    .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }

        LogSwept(total, cutoff);

        // The bound was reached with a full batch still coming back, so the backlog outlasted this
        // sweep. Polling again at once drains it instead of trickling it out one interval at a time.
        return PollPassOutcome.PollAgain;
    }

    /// <summary>
    /// Decides whether a failed sweep is the shutdown cancellation rather than a failure: the one place
    /// that decision is made.
    /// </summary>
    /// <returns>True once the host's shutdown timeout has cancelled the batch.</returns>
    /// <remarks>
    /// Read from the state rather than from the exception, because a store does not always report a
    /// cancelled command as an <see cref="OperationCanceledException"/>: SqlClient throws a
    /// <c>SqlException</c>, and EF Core rethrows it. Whatever a batch ends in once its token was
    /// cancelled on shutdown is that cancellation; a failure before the timeout is still a failure.
    /// </remarks>
    private bool IsShutdownCancellation() => _shutdownElapsedToken.IsCancellationRequested;

    /// <summary>
    /// Cancels <paramref name="source"/> and observes the callbacks that fail; called once per source,
    /// through its <see cref="Lazy{T}"/>.
    /// </summary>
    /// <param name="source">The source to cancel.</param>
    /// <returns>
    /// The task that completes once every callback of the token has run. The token reads as cancelled
    /// before this returns.
    /// </returns>
    /// <remarks>
    /// <see cref="CancellationTokenSource.CancelAsync"/> sets the cancelled state synchronously and runs
    /// the callbacks on the thread pool, so no caller blocks on a store's command cancellation, and the
    /// state a caller reads next is already the cancelled one. The one continuation reads the failure,
    /// so a throwing callback is logged and never left on an unobserved task.
    /// </remarks>
    private Task CancelObservedAsync(CancellationTokenSource source)
    {
        var cancellation = source.CancelAsync();

        _ = cancellation.ContinueWith(
            static (failed, state) => ((RetentionSweeper<TPass>)state!).ReportCancellationFailed(failed.Exception!),
            this,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return cancellation;
    }

    /// <summary>Reports a callback on one of the sweeper's tokens that failed while it was being cancelled.</summary>
    /// <param name="failure">What the callbacks threw.</param>
    private void ReportCancellationFailed(AggregateException failure) =>
        _logger.LogDebug(
            failure,
            "Cancelling a token of the retention sweep of {RetentionPass} on stop failed in a callback of that token.",
            _passName);

    /// <summary>Reads whether the sweeper was asked to stop, straight from the tokens' own state.</summary>
    /// <param name="cancellationToken">The token the batch in flight observes.</param>
    /// <returns>True once a stop was requested or the batch's token was cancelled.</returns>
    /// <remarks>
    /// Read from the two tokens rather than from a source linked to them: a linked source learns of the
    /// cancellation in a callback, which <see cref="CancellationTokenSource.CancelAsync"/> runs on the
    /// thread pool, so for a moment after a stop or a dispose it would still read as not cancelled and a
    /// further batch could start.
    /// </remarks>
    private bool IsDrainRequested(CancellationToken cancellationToken) =>
        _stopRequestedToken.IsCancellationRequested || cancellationToken.IsCancellationRequested;

    /// <summary>
    /// Links the stop request with the token the batch observes: the drain is over once either is
    /// cancelled.
    /// </summary>
    /// <param name="cancellationToken">The token the batch in flight observes.</param>
    /// <returns>The source of the drain token, which the caller disposes.</returns>
    /// <remarks>
    /// The host cancels the batch's token only after it has asked the sweeper to stop, so on a host the
    /// drain is the stop request; the batch's token is linked in for a caller that drives the loop
    /// with that one token alone.
    /// </remarks>
    private CancellationTokenSource DrainFor(CancellationToken cancellationToken) =>
        CancellationTokenSource.CreateLinkedTokenSource(_stopRequestedToken, cancellationToken);

    /// <summary>Works out the cutoff handed to the pass from the clock.</summary>
    /// <param name="retention">How long a record is kept; null for the current time itself.</param>
    /// <returns>The current time less the retention, or the current time when there is none.</returns>
    private DateTimeOffset CutoffFor(TimeSpan? retention) =>
        _timeProvider.GetUtcNow() - (retention ?? TimeSpan.Zero);

    /// <summary>Reports a sweep that deleted something.</summary>
    /// <param name="total">How many records the sweep deleted in all.</param>
    /// <param name="cutoff">The cutoff the sweep deleted against.</param>
    private void LogSwept(int total, DateTimeOffset cutoff)
    {
        if (total > 0)
        {
            _logger.LogInformation(
                "The retention pass {RetentionPass} purged {PurgedCount} records against the cutoff {Cutoff:o}.",
                _passName,
                total,
                cutoff);
        }
    }
}
