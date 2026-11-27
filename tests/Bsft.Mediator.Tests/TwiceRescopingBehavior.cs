using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Mediator.Tests;

/// <summary>
/// Runs the rest of the pipeline once in the request scope and once in a fresh scope, and answers with
/// the second result, recording the marker of each attempt scope.
/// </summary>
public sealed class TwiceRescopingBehavior<TRequest, TResponse> : IRescopingPipelineBehavior<TRequest, TResponse>
{
    public Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken = default) =>
        next();

    public async Task<TResponse> HandleAsync(TRequest request, IPipelineContinuation<TResponse> next, CancellationToken cancellationToken = default)
    {
        await next.RunAsync(Record, cancellationToken);

        return await next.RunInNewScopeAsync(Record, cancellationToken);
    }

    private static Task<TResponse> Record(IServiceProvider scope, Func<Task<TResponse>> rest)
    {
        if (scope.GetService<ScopeMarker>() is { } marker)
            scope.GetService<RescopeLog>()?.AttemptMarkers.Add(marker);

        return rest();
    }
}
