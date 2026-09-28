using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

internal sealed class MultiCapabilityHandler : ICommandHandler<MultiCapabilityCommand, string>
{
    public Task<string> HandleAsync(MultiCapabilityCommand command, CancellationToken ct = default) =>
        Task.FromResult("handled");
}
