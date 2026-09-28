using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Bsft.Idempotency.Tests.Fakes;

/// <summary>
/// An <see cref="IEndpointConventionBuilder"/> that applies whatever conventions it was given to one
/// endpoint, so a test can read the metadata an extension method added.
/// </summary>
internal sealed class ConventionRecorder : IEndpointConventionBuilder
{
    private readonly List<Action<EndpointBuilder>> _conventions = [];

    public void Add(Action<EndpointBuilder> convention) => _conventions.Add(convention);

    /// <summary>Builds the endpoint the conventions describe.</summary>
    /// <returns>The endpoint.</returns>
    public Endpoint Build()
    {
        var builder = new RouteEndpointBuilder(
            _ => Task.CompletedTask, RoutePatternFactory.Parse("/orders"), order: 0);

        foreach (var convention in _conventions)
            convention(builder);

        return builder.Build();
    }
}
