using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Mediator.Tests;

/// <summary>Answers with the marker of the scope it was resolved from.</summary>
/// <remarks>Takes the provider, so the startup validator can build it in any test container.</remarks>
public sealed class RescopedCommandHandler(IServiceProvider serviceProvider) : ICommandHandler<RescopedCommand, string>
{
    public Task<string> HandleAsync(RescopedCommand command, CancellationToken cancellationToken = default) =>
        Task.FromResult(serviceProvider.GetService<ScopeMarker>()?.Name ?? "none");
}
