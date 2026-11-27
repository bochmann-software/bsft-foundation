using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Mediator.Tests;

/// <summary>Logs the marker of the scope it was resolved from.</summary>
/// <remarks>Takes the provider, so the startup validator can build it in any test container.</remarks>
public sealed class RescopedVoidCommandHandler(IServiceProvider serviceProvider) : ICommandHandler<RescopedVoidCommand>
{
    public Task HandleAsync(RescopedVoidCommand command, CancellationToken cancellationToken = default)
    {
        serviceProvider.GetService<RescopeLog>()?.Handled.Add(serviceProvider.GetService<ScopeMarker>()?.Name ?? "none");

        return Task.CompletedTask;
    }
}
