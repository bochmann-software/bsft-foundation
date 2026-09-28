namespace Bsft.Core.Polling;

/// <summary>
/// The poll loop every long-running background service in this framework runs: a pass, the outcome
/// of that pass observed rather than caught, and a wait before the next one.
/// </summary>
/// <remarks>
/// <para>
/// Three services wrote this loop by hand before it was one type, and the third of them was reviewed
/// with the self-cancellation branch missing — a command timeout would have escaped the loop and
/// taken the host down. That branch is the reason this exists: the shape is subtle enough that the
/// next copy would get it wrong too.
/// </para>
/// <para>
/// The contract in one sentence: a pass may fail in any way it likes without ending the service, and
/// only the stopping token ends the loop.
/// </para>
/// </remarks>
public sealed class PollLoop : IPollLoop
{
    private readonly Func<CancellationToken, Task<PollPassOutcome>> _pass;
    private readonly IPollReporter _reporter;
    private readonly TimeSpan _interval;

    /// <summary>
    /// Initializes a new instance of the <see cref="PollLoop"/> class.
    /// </summary>
    /// <param name="pass">The single pass the loop runs over and over.</param>
    /// <param name="reporter">Receives what the loop has to say about the passes it ran.</param>
    /// <param name="interval">
    /// How long the loop waits before a pass that asked to wait. It is not validated here: the wait
    /// is a plain <see cref="Task.Delay(TimeSpan, CancellationToken)"/>, so an interval that cannot
    /// be waited is rejected by that call, with its argument name and its own message.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public PollLoop(
        Func<CancellationToken, Task<PollPassOutcome>> pass,
        IPollReporter reporter,
        TimeSpan interval)
    {
        _pass = pass ?? throw new ArgumentNullException(nameof(pass));
        _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
        _interval = interval;
    }

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken stoppingToken = default)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var pass = StartPassAsync(stoppingToken);

                // The outcome of a pass is observed rather than caught: a transient failure of a
                // store or a transport must not take a long-running service down, and the work it
                // left behind is still there for the next pass. Nothing is hidden by keeping the
                // loop alive, because every outcome below is reported.
                await Task.WhenAny(pass).ConfigureAwait(false);

                if (pass.IsFaulted)
                {
                    _reporter.ReportPassFailed(FailureOf(pass.Exception!));
                    await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // A pass that ends cancelled while nobody asked the service to stop was cancelled by
                // a token of its own — a store call or a database command that timed out, typically.
                // Reading that as a shutdown would leave the host running with this service no
                // longer doing its work, so it counts as a failed pass and the next poll tries again.
                if (pass.IsCanceled && !stoppingToken.IsCancellationRequested)
                {
                    _reporter.ReportPassCanceledWithoutStop();
                    await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // Surfaces a shutdown cancellation as itself; otherwise the pass simply finished and
                // says whether it left work behind.
                var outcome = await pass.ConfigureAwait(false);

                if (outcome != PollPassOutcome.PollAgain)
                    await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected when the stopping token is cancelled during a pass or a delay on shutdown. The
            // filter is what keeps that meaning: without it any cancellation from anywhere below
            // would end the loop as a shutdown, and one timeout would stop the service for good while
            // the host kept running. A cancellation with no stop behind it leaves the loop loudly.
            _reporter.ReportStopping();
        }
    }

    /// <summary>
    /// Calls the pass and hands back the task it is running as, turning the two ways a pass can fail
    /// before there is any task into that same task, so that one place decides what a failed pass
    /// means.
    /// </summary>
    /// <remarks>
    /// A pass is a delegate the caller wrote, and nothing makes it an async method: a plain lambda
    /// that validates its arguments throws out of the call itself, and a delegate can return no task
    /// at all. Both would leave the loop through the awaits below — a synchronous failure is not a
    /// cancellation, so the filtered catch would not hold it, and it would surface out of
    /// <c>ExecuteAsync</c> and take the host down with the default
    /// <c>BackgroundServiceExceptionBehavior</c>. That is exactly what this type promises cannot
    /// happen, so both cases become a task that ended the way the loop already knows how to read.
    /// </remarks>
    private Task<PollPassOutcome> StartPassAsync(CancellationToken stoppingToken)
    {
        try
        {
            return _pass(stoppingToken)
                ?? Task.FromException<PollPassOutcome>(new InvalidOperationException(
                    "The poll pass returned null instead of a task; a pass must return the task it runs as."));
        }
        catch (OperationCanceledException exception)
        {
            // A cancellation thrown out of the call is the same event as a pass that ended cancelled,
            // and the loop tells stop from self-cancellation by the stopping token alone — never by
            // the exception or its token. The ternary exists only because Task.FromCanceled rejects a
            // token that is not already cancelled, so the exception's own token is reused where it
            // qualifies and a cancelled placeholder stands in otherwise.
            return Task.FromCanceled<PollPassOutcome>(
                exception.CancellationToken.IsCancellationRequested ? exception.CancellationToken : new CancellationToken(true));
        }
        catch (Exception exception)
        {
            // Deliberately unfiltered: the pass belongs to the caller and may fail in any way it
            // likes, which is the contract this type exists to keep. Nothing is swallowed — the
            // failure is reported below exactly as a faulted pass is.
            return Task.FromException<PollPassOutcome>(exception);
        }
    }

    /// <summary>
    /// Reads the failure to report out of the aggregate a faulted task carries: the failure itself
    /// when the pass failed once, and the flattened aggregate when it failed more than once.
    /// </summary>
    /// <remarks>
    /// A pass that failed once is by far the common case, and wrapping that one failure says nothing:
    /// the aggregate's own message only states that something went wrong, and an alert rule keyed on
    /// the exception type would see <see cref="AggregateException"/> for every failure of every
    /// service. Where there is more than one failure the aggregate is what carries them all, so it
    /// stands, flattened.
    /// </remarks>
    private static Exception FailureOf(AggregateException exception)
    {
        var flattened = exception.Flatten();

        return flattened.InnerExceptions.Count == 1 ? flattened.InnerExceptions[0] : flattened;
    }
}
