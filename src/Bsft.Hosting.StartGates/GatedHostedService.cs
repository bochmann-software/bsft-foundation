using Bsft.Core.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bsft.Hosting.StartGates;

/// <summary>
/// Holds a hosted service back until every start gate has opened, and starts it then.
/// </summary>
/// <remarks>
/// <para>
/// The wrapper is a <see cref="BackgroundService"/> of its own, so the host treats it like every other
/// loop: <see cref="IHostedService.StartAsync"/> returns at once, whatever the gates do, and
/// <c>HostOptions.ServicesStartConcurrently</c> and <c>HostOptions.BackgroundServiceExceptionBehavior</c>
/// apply to it unchanged. The inner service is started from <see cref="ExecuteAsync"/> after the gates and
/// not before, so it has done nothing at all while a gate is pending: no loop, no flush, no counter.
/// </para>
/// <para>
/// A fault of the inner service's own loop surfaces through the wrapper's <see cref="BackgroundService.ExecuteTask"/>,
/// where the host reads it. A gate that throws faults the wrapper the same way, because running before a
/// precondition holds is what the gate exists to prevent. A stop while a gate is pending ends the wrapper
/// without an error, and the inner service, which never started, is not stopped.
/// </para>
/// <para>
/// An inner service that takes part in the host's lifecycle (<see cref="IHostedLifecycleService"/>) gets its
/// starting and started calls from the wrapper, around its own start and after the gates, because the host
/// would otherwise call them on the wrapper, before the service ran. Its stopping and stopped calls follow
/// the host's, and only once it has started.
/// </para>
/// </remarks>
internal sealed class GatedHostedService : BackgroundService, IHostedLifecycleService
{
    private readonly IHostedService _inner;
    private readonly IReadOnlyList<IHostedWorkStartGate> _gates;
    private readonly ILogger _logger;
    private volatile bool _innerStarted;
    private int _stoppingForwarded;

    /// <summary>Initializes a new instance of the <see cref="GatedHostedService"/> class.</summary>
    /// <param name="inner">The service to start once the gates have opened.</param>
    /// <param name="gates">The gates awaited, in order, before the inner service starts.</param>
    /// <param name="logger">Logger used to report a stop that came before the gates opened.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public GatedHostedService(IHostedService inner, IReadOnlyList<IHostedWorkStartGate> gates, ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _gates = gates ?? throw new ArgumentNullException(nameof(gates));
    }

    /// <inheritdoc />
    /// <remarks>The inner service is started after the gates, and gets its own starting call there.</remarks>
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    /// <remarks>The inner service is started after the gates, and gets its own started call there.</remarks>
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) =>
        _innerStarted ? ForwardStoppingAsync(cancellationToken) : Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) =>
        _innerStarted && _inner is IHostedLifecycleService lifecycle ? lifecycle.StoppedAsync(cancellationToken) : Task.CompletedTask;

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // A service that never started has nothing to stop: a stop while the gates are pending must not
        // run its shutdown work (a final flush, a drain) for a loop that did not run.
        if (!_innerStarted)
            return;

        // The host's stopping call can come while the gates open, before the inner service has started, and
        // is then not forwarded; the inner service still gets it, ahead of its stop.
        await ForwardStoppingAsync(cancellationToken).ConfigureAwait(false);
        await _inner.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task ForwardStoppingAsync(CancellationToken cancellationToken) =>
        _inner is IHostedLifecycleService lifecycle && Interlocked.Exchange(ref _stoppingForwarded, 1) == 0
            ? lifecycle.StoppingAsync(cancellationToken)
            : Task.CompletedTask;

    /// <inheritdoc />
    public override void Dispose()
    {
        base.Dispose();
        (_inner as IDisposable)?.Dispose();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await _gates.HasOpenedAsync(stoppingToken).ConfigureAwait(false))
        {
            _logger.LogWarning(
                "The host stopped before the start gates opened, so {Service} never started; work queued for it, if any, is lost.",
                _inner.GetType().FullName);

            return;
        }

        _innerStarted = true;
        var lifecycle = _inner as IHostedLifecycleService;
        if (lifecycle is not null)
            await lifecycle.StartingAsync(stoppingToken).ConfigureAwait(false);

        await _inner.StartAsync(stoppingToken).ConfigureAwait(false);

        if (lifecycle is not null)
            await lifecycle.StartedAsync(stoppingToken).ConfigureAwait(false);

        // The inner loop's own task, so that its fault is this task's fault. A service that is not a
        // BackgroundService has none: it did its start and the wrapper has nothing left to watch.
        if (_inner is not BackgroundService { ExecuteTask: { } innerLoop })
            return;

        var hostStopped = Task.Delay(Timeout.Infinite, stoppingToken);

        if (await Task.WhenAny(innerLoop, hostStopped).ConfigureAwait(false) == innerLoop)
            await innerLoop.ConfigureAwait(false);
    }
}
