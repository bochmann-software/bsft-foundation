using System.Reflection;
using Bsft.Core;
using Bsft.Mediator.DependencyInjection;
using Bsft.Mediator.StartupValidation;
using Bsft.Mediator.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Bsft.Mediator.Tests.StartupValidation;

/// <summary>A behavior whose request parameter is constrained beyond the command contract.</summary>
public sealed class ConstrainedProbeBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IConstrainedProbeRequest
{
    public ConstrainedProbeBehavior(IConstrainedProbeDependency dependency) =>
        Dependency = dependency ?? throw new ArgumentNullException(nameof(dependency));

    public IConstrainedProbeDependency Dependency { get; }

    public Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken ct = default) =>
        next();
}
