using Bsft.Core;
using Bsft.Mediator;

namespace Bsft.Testing.Tests.Support;

/// <summary>
/// A behavior closed over the request contract the mediator dispatches on, doubling the result.
/// </summary>
public class DoublingBehavior : IPipelineBehavior<ICommand<int>, int>
{
    public async Task<int> HandleAsync(ICommand<int> request, Func<Task<int>> next, CancellationToken ct = default) =>
        await next() * 2;
}
