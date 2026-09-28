using FluentAssertions;
using Xunit;

namespace Bsft.Idempotency.Tests;

public class IdempotentAttributeTests
{
    [Fact]
    public void TheParameterlessAttribute_LeavesTheDecisionToTheHost() =>
        new IdempotentAttribute().ShouldRequireKey.Should().BeNull();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheAttribute_StatesWhatTheEndpointDecidedForItself(bool requireKey) =>
        new IdempotentAttribute(requireKey).ShouldRequireKey.Should().Be(requireKey);

    [Fact]
    public void TheAttribute_IsTheMetadataTheMiddlewareReads() =>
        new IdempotentAttribute().Should().BeAssignableTo<IIdempotencyMetadata>();

    [Fact]
    public void TheAttribute_AppliesToAControllerAndToAnAction()
    {
        var usage = typeof(IdempotentAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.ValidOn.Should().Be(AttributeTargets.Class | AttributeTargets.Method);
        usage.AllowMultiple.Should().BeFalse();
        usage.Inherited.Should().BeTrue();
    }
}
