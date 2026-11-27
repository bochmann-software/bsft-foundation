using Microsoft.Extensions.Hosting;

namespace Bsft.Hosting.StartGates.Tests.Fakes;

/// <summary>A background loop that takes part in the host's lifecycle and records the calls it gets, in order.</summary>
internal sealed class LifecycleLoop : BackgroundService, IHostedLifecycleService
{
    private readonly List<string> _calls = [];

    /// <summary>Gets the calls the loop received, in order.</summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_calls)
                return [.. _calls];
        }
    }

    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken) => Record("starting");

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        Record("start");

        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Record("started");

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Record("stopping");

    /// <inheritdoc />
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        Record("stop");

        return base.StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Record("stopped");

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Delay(Timeout.Infinite, stoppingToken);

    private Task Record(string call)
    {
        lock (_calls)
            _calls.Add(call);

        return Task.CompletedTask;
    }
}
