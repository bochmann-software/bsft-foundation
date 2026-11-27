using Bsft.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Resilience.Mediator.Tests.Fakes;

/// <summary>
/// The rest of a pipeline the way the mediator hands it over: run in the request scope, or in a fresh
/// scope of the same container. The rest is told which scope it runs in.
/// </summary>
public sealed class FakePipelineContinuation<TResponse>(
    IServiceProvider requestScope, Func<IServiceProvider, Task<TResponse>> rest) : IPipelineContinuation<TResponse>
{
    public List<IServiceProvider> NewScopes { get; } = [];

    /// <summary>Thrown by every fresh scope before the attempt runs, as a failing scope setup would.</summary>
    public Exception? NewScopeFailure { get; set; }

    public int NewScopeRuns { get; private set; }

    public Task<TResponse> RunAsync(PipelineAttempt<TResponse> attempt, CancellationToken cancellationToken = default) =>
        attempt(requestScope, () => rest(requestScope));

    public async Task<TResponse> RunInNewScopeAsync(PipelineAttempt<TResponse> attempt, CancellationToken cancellationToken = default)
    {
        NewScopeRuns++;
        if (NewScopeFailure is not null)
            throw NewScopeFailure;

        await using var scope = requestScope.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        NewScopes.Add(scope.ServiceProvider);

        return await attempt(scope.ServiceProvider, () => rest(scope.ServiceProvider));
    }
}
