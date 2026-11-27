using Microsoft.Extensions.Hosting;

namespace Bsft.Hosting.StartGates.Tests.Fakes;

/// <summary>A background loop that records that it started and stopped, and can be told to fail.</summary>
internal sealed class RecordingLoop : BackgroundService
{
    private readonly TaskCompletionSource _running = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets or sets the exception the loop throws once it runs; null makes it wait for the stop.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Gets how many times the loop started.</summary>
    public int Starts { get; private set; }

    /// <summary>Gets how many times the loop was asked to stop.</summary>
    public int Stops { get; private set; }

    /// <summary>Gets a task that completes when the loop runs.</summary>
    public Task Running => _running.Task;

    /// <inheritdoc />
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        Stops++;

        return base.StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Starts++;
        _running.TrySetResult();
        await Task.Delay(50);

        if (Failure is not null)
            throw Failure;

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
    }
}
