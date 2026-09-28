using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

// Test pipeline behavior
public class TestPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public static List<string> ExecutionLog { get; } = [];

    public async Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken ct = default)
    {
        ExecutionLog.Add($"Before:{typeof(TRequest).Name}");
        var result = await next().ConfigureAwait(false);
        ExecutionLog.Add($"After:{typeof(TRequest).Name}");
        return result;
    }

    public static void Reset() => ExecutionLog.Clear();
}
