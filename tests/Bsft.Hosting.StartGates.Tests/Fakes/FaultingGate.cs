using Bsft.Core.Hosting;

namespace Bsft.Hosting.StartGates.Tests.Fakes;

/// <summary>A gate whose precondition cannot be established.</summary>
internal sealed class FaultingGate : IHostedWorkStartGate
{
    /// <inheritdoc />
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        // Fails after a real wait, as a probe of a database does, so the failure is a background fault and not a failed start.
        await Task.Delay(50);

        throw new InvalidOperationException("the schema is not reachable");
    }
}
