using System.Reflection;
using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Bsft.Mediator.Tests;

// A pipeline behavior used only by the AddBehavior pipeline test, so its static log is not shared
// with any test class that runs in parallel with this one.
public class PipelineProbeBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public static List<string> ExecutionLog { get; } = [];

    public async Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken ct = default)
    {
        ExecutionLog.Add($"Before:{typeof(TRequest).Name}");
        var result = await next().ConfigureAwait(false);
        ExecutionLog.Add($"After:{typeof(TRequest).Name}");
        return result;
    }
}
