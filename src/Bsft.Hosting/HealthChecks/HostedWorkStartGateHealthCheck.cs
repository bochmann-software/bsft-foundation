using Bsft.Core.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bsft.Hosting.HealthChecks;

/// <summary>
/// Health check that reports a hosted work start gate that has been pending for longer than it should:
/// a worker that is waiting instead of working.
/// </summary>
/// <remarks>
/// Only gates that implement <see cref="IHostedWorkStartGateOverdueSignal"/> can be reported; a gate that
/// does not is never overdue as far as this check can tell.
/// </remarks>
public sealed class HostedWorkStartGateHealthCheck : IHealthCheck
{
    private readonly IEnumerable<IHostedWorkStartGate> _gates;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostedWorkStartGateHealthCheck"/> class.
    /// </summary>
    /// <param name="gates">The registered start gates.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="gates"/> is null.</exception>
    public HostedWorkStartGateHealthCheck(IEnumerable<IHostedWorkStartGate> gates) =>
        _gates = gates ?? throw new ArgumentNullException(nameof(gates));

    /// <summary>
    /// Performs the health check by asking every gate that can say so whether it is overdue.
    /// </summary>
    /// <param name="context">The health check context.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A <see cref="HealthCheckResult"/> that is unhealthy while a gate is overdue.</returns>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var overdue = _gates
            .OfType<IHostedWorkStartGateOverdueSignal>()
            .Where(gate => gate.IsOverdue())
            .Select(gate => gate.GetType().Name)
            .ToArray();

        var result = overdue.Length == 0
            ? HealthCheckResult.Healthy("No hosted work start gate is overdue.")
            : HealthCheckResult.Unhealthy(
                $"Hosted work is still waiting for start gate(s) past their expected time: {string.Join(", ", overdue)}.");

        return Task.FromResult(result);
    }
}
