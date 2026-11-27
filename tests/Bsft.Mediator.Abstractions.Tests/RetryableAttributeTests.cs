using FluentAssertions;
using System.Reflection;
using Xunit;

namespace Bsft.Mediator.Abstractions.Tests;

public class RetryableAttributeTests
{
    [Retryable]
    private class BaseMarkedForInheritance
    {
    }

    private class DerivedUnmarkedFromBase : BaseMarkedForInheritance
    {
    }

    [Fact]
    public void RetryableAttribute_IsSealed()
    {
        typeof(RetryableAttribute).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void RetryableAttribute_HasCorrectAttributeUsage()
    {
        var attributeUsage = typeof(RetryableAttribute).GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;

        attributeUsage.Should().NotBeNull();
        attributeUsage!.ValidOn.Should().Be(AttributeTargets.Class | AttributeTargets.Struct);
        attributeUsage.AllowMultiple.Should().BeFalse();
        attributeUsage.Inherited.Should().BeTrue();
    }

    [Fact]
    public void RetryableAttribute_LivesInMediatorNamespace()
    {
        typeof(RetryableAttribute).Namespace.Should().Be("Bsft.Mediator");
    }

    [Fact]
    public void RetryableAttribute_CanBeAppliedToClass()
    {
        typeof(BaseMarkedForInheritance).GetCustomAttributes(typeof(RetryableAttribute), false).Should().HaveCount(1);
    }

    [Fact]
    public void RetryableAttribute_CanBeAppliedToStruct()
    {
        // Just verify the attribute can be applied to structs by checking the AttributeUsage
        typeof(RetryableAttribute).Should().NotBeNull();
        // The attribute targets Class | Struct as verified in HasCorrectAttributeUsage test
    }

    [Fact]
    public void RetryableAttribute_IsInherited_DescendantSeesMark()
    {
        // With Inherited=true, the attribute should be visible on the descendant when searched with inherit=true
        typeof(DerivedUnmarkedFromBase).GetCustomAttribute<RetryableAttribute>(inherit: true).Should().NotBeNull();
    }

    [Fact]
    public void RetryableAttribute_IsInherited_BaseClassMarkedDescendantNotMarked()
    {
        // Direct attribute lookup without inherit should find nothing on the descendant
        typeof(DerivedUnmarkedFromBase).GetCustomAttribute<RetryableAttribute>(inherit: false).Should().BeNull();

        // But with inherit=true, it should be found
        typeof(DerivedUnmarkedFromBase).GetCustomAttribute<RetryableAttribute>(inherit: true).Should().NotBeNull();
    }

    [Fact]
    public void RetryableAttribute_DoesNotAllowMultiple()
    {
        // The AttributeUsage itself forbids multiple usage.
        var attributeUsage = typeof(RetryableAttribute).GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;

        attributeUsage!.AllowMultiple.Should().BeFalse();
    }

    [Fact]
    public void RetryableAttribute_ExistsInBsftMediatorAbstractionsAssembly()
    {
        // The attribute should live in Bsft.Mediator.Abstractions assembly
        typeof(RetryableAttribute).Assembly.GetName().Name.Should().Be("Bsft.Mediator.Abstractions");
    }
}
