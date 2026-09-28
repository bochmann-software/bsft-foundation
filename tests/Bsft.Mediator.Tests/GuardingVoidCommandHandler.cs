using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>A handler that throws synchronously, so it never returns a task to fault.</summary>
public class GuardingVoidCommandHandler : ICommandHandler<GuardingVoidCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(GuardingVoidCommand command, CancellationToken ct = default) =>
        throw new ArgumentException("guarded");
}
