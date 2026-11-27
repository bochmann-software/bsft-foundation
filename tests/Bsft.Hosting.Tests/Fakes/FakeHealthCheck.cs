using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bsft.Hosting.Tests.Fakes;

/// <summary>A health check that reports the result it was given and counts how often it ran.</summary>
public sealed class FakeHealthCheck : IHealthCheck
{
    private readonly HealthCheckResult _result;
    private int _invocations;

    public FakeHealthCheck(HealthCheckResult result)
    {
        _result = result;
    }

    public int Invocations => Volatile.Read(ref _invocations);

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _invocations);
        return Task.FromResult(_result);
    }
}
