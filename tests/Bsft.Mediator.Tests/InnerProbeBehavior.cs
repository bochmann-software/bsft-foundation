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

/// <summary>The inner probe: counts its invocations and records that it ran.</summary>
public class InnerProbeBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public static int Invocations { get; set; }

    public Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken ct = default)
    {
        Invocations++;
        ProbeLog.Entries.Add("inner");
        return next();
    }
}
