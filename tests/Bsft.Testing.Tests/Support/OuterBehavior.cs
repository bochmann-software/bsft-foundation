using Bsft.Core;
using Bsft.Mediator;

namespace Bsft.Testing.Tests.Support;

public class OuterBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly ITrace _trace;

    public OuterBehavior(ITrace trace) => _trace = trace;

    public async Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken ct = default)
    {
        _trace.Add("outer:before");
        var response = await next();
        _trace.Add("outer:after");
        return response;
    }
}
