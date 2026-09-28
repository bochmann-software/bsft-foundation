using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

public class TestVoidCommandHandler : ICommandHandler<TestVoidCommand>
{
    public static bool WasCalled { get; private set; }

    public Task HandleAsync(TestVoidCommand command, CancellationToken ct = default)
    {
        WasCalled = true;
        return Task.CompletedTask;
    }

    public static void Reset() => WasCalled = false;
}
