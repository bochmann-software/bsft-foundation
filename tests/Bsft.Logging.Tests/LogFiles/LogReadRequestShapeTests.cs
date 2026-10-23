using Bsft.Logging.LogFiles;
using FluentAssertions;

namespace Bsft.Logging.Tests.LogFiles;

/// <summary>
/// A shipped record keeps its constructor and its deconstruction when it gains a member, so the
/// correlation id is an init property and no constructor parameter.
/// </summary>
public sealed class LogReadRequestShapeTests
{
    private const int ShippedParameterCount = 5;

    [Fact]
    public void CorrelationId_IsAnInitPropertyAndNoConstructorOrDeconstructParameter()
    {
        var type = typeof(LogReadRequest);
        var constructor = type.GetConstructors().Single();
        var deconstruct = type.GetMethod("Deconstruct")!;
        var property = type.GetProperty(nameof(LogReadRequest.CorrelationId))!;

        constructor.GetParameters().Should().HaveCount(ShippedParameterCount);
        constructor.GetParameters().Should().NotContain(parameter => parameter.Name == "CorrelationId");
        deconstruct.GetParameters().Should().HaveCount(ShippedParameterCount);
        property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers()
            .Should().Contain(typeof(System.Runtime.CompilerServices.IsExternalInit));
    }
}
