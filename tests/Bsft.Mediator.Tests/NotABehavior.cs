using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

// An open generic of the right arity that is not a pipeline behavior at all.
public class NotABehavior<TRequest, TResponse>
{
    public Task<TResponse?> HandleAsync(TRequest request) => Task.FromResult(default(TResponse));
}
