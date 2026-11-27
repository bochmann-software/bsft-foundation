using Microsoft.Extensions.Hosting;

namespace Bsft.Hosting.StartGates.Tests.Fakes;

/// <summary>A second loop type, so that one test can register two services that must not collapse into one.</summary>
internal sealed class OtherRecordingLoop : BackgroundService
{
    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Delay(Timeout.Infinite, stoppingToken);
}
