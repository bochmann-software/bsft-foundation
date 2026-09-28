using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

// An open generic with the wrong arity for IPipelineBehavior<TRequest, TResponse>.
public class SingleParameterBehavior<TRequest>
{
    public Task<TRequest> HandleAsync(TRequest request) => Task.FromResult(request);
}
