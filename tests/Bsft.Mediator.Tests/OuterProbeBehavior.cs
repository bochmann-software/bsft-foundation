using Bsft.Caching.Mediator;
using Bsft.Core;
using Bsft.Mediator.DependencyInjection;
using Bsft.Mediator.Tests.Support;
using Bsft.Mediator.Transactions;
using Bsft.Resilience.Mediator;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>The outer probe: records that it ran.</summary>
public class OuterProbeBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken ct = default)
    {
        ProbeLog.Entries.Add("outer");
        return next();
    }
}
