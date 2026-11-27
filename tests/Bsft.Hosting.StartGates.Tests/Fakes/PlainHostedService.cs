using Microsoft.Extensions.Hosting;

namespace Bsft.Hosting.StartGates.Tests.Fakes;

/// <summary>A hosted service that is not a background service: it does its work in start and stop.</summary>
internal sealed class PlainHostedService : IHostedService, IDisposable
{
    /// <summary>Gets how many times the service started.</summary>
    public int Starts { get; private set; }

    /// <summary>Gets how many times the service stopped.</summary>
    public int Stops { get; private set; }

    /// <summary>Gets whether the service was disposed.</summary>
    public bool Disposed { get; private set; }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Starts++;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Stops++;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => Disposed = true;
}
