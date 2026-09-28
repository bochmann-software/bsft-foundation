using FluentAssertions;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>
/// A behavior whose request parameter neither the command contract nor its own constraint list
/// satisfies, so the startup validator cannot close it for activation.
/// </summary>
public class StructConstrainedBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : struct
{
    public Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken ct = default) =>
        next();
}
