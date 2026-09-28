using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

// The two fixtures below are read for their attributes rather than dispatched, but every concrete
// request type in this assembly needs a handler: MediatorStartupValidatorTests asserts that the
// assembly scan leaves no request without one.
internal sealed class DerivedProbeHandler : ICommandHandler<DerivedProbeCommand, string>
{
    public Task<string> HandleAsync(DerivedProbeCommand command, CancellationToken ct = default) =>
        Task.FromResult("handled");
}
