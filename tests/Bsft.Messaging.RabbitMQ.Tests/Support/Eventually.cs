using System.Diagnostics;

namespace Bsft.Messaging.RabbitMQ.Tests.Support;

/// <summary>
/// Waits for a step of the pipeline to have happened. The host runs on its own task, so a test cannot
/// await it directly; what it can do is refuse to wait forever.
/// </summary>
internal static class Eventually
{
    /// <summary>How long a step of the pipeline is given before the test calls it a failure.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>Waits until a condition holds.</summary>
    /// <param name="condition">The state the pipeline is expected to reach.</param>
    /// <returns>A task that completes once the condition holds.</returns>
    /// <exception cref="TimeoutException">Thrown when the condition does not hold in time.</exception>
    public static async Task HoldsAsync(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();

        while (!condition())
        {
            if (clock.Elapsed > Patience)
                throw new TimeoutException($"The pipeline did not reach the expected state within {Patience}.");

            await Task.Delay(5);
        }
    }
}
