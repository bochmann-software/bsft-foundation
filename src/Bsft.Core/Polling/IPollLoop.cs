namespace Bsft.Core.Polling;

/// <summary>
/// Runs a poll pass over and over until the service is asked to stop, without letting a failing pass
/// end the service.
/// </summary>
public interface IPollLoop
{
    /// <summary>Runs passes until the stopping token is cancelled.</summary>
    /// <param name="stoppingToken">The token that stops the loop when cancelled.</param>
    /// <returns>A task that completes when the loop has unwound.</returns>
    Task RunAsync(CancellationToken stoppingToken = default);
}
