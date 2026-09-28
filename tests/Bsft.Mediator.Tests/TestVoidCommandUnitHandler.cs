using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

public class TestVoidCommandUnitHandler : ICommandHandler<TestVoidCommand, global::Bsft.Core.Unit>
{
    public static bool WasCalled { get; private set; }

    public Task<global::Bsft.Core.Unit> HandleAsync(TestVoidCommand command, CancellationToken ct = default)
    {
        WasCalled = true;
        return global::Bsft.Core.Unit.Task;
    }

    public static void Reset() => WasCalled = false;
}
