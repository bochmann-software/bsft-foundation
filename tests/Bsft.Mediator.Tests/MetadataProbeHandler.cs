using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

internal sealed class MetadataProbeHandler : ICommandHandler<MetadataProbeCommand, string>
{
    public Task<string> HandleAsync(MetadataProbeCommand command, CancellationToken ct = default) =>
        Task.FromResult("handled");
}
