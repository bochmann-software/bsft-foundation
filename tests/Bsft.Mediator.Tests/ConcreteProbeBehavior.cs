using FluentAssertions;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>A behavior that is not an open generic, so the catalog can never know it.</summary>
public class ConcreteProbeBehavior : IPipelineBehavior<ICommand<string>, string>
{
    public Task<string> HandleAsync(ICommand<string> request, Func<Task<string>> next, CancellationToken ct = default) =>
        next();
}
