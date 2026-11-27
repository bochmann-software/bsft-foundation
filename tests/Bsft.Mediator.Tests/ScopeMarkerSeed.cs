using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Mediator.Tests;

/// <summary>Copies the request scope marker into a fresh scope, the way a host carries its tenant.</summary>
public sealed class ScopeMarkerSeed(ScopeMarker source) : IRequestScopeSeed
{
    public void Seed(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<ScopeMarker>().Name = $"{source.Name}-seeded";
}
