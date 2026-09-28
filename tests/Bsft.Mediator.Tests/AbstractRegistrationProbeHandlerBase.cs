using System.Reflection;
using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>An abstract handler the assembly scan must pass over.</summary>
public abstract class AbstractRegistrationProbeHandlerBase : ICommandHandler<AbstractRegistrationProbeCommand, string>
{
    public Task<string> HandleAsync(AbstractRegistrationProbeCommand command, CancellationToken ct = default) =>
        Task.FromResult("handled");
}
