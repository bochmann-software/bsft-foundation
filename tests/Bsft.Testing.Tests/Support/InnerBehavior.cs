using Bsft.Core;
using Bsft.Mediator;

namespace Bsft.Testing.Tests.Support;

public class InnerBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly ITrace _trace;

    public InnerBehavior(ITrace trace) => _trace = trace;

    public async Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken ct = default)
    {
        _trace.Add("inner:before");
        var response = await next();
        _trace.Add("inner:after");
        return response;
    }
}
