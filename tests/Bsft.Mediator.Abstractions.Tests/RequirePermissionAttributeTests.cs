using FluentAssertions;
using Xunit;

namespace Bsft.Mediator.Abstractions.Tests;

/// <summary>
/// The one declaration in this package that runs code. A command carries its permission through it,
/// and a behavior in another package reads it back off the request's type.
/// </summary>
public sealed class RequirePermissionAttributeTests
{
    [Fact]
    public void A_declared_permission_is_kept_as_it_was_given()
        => new RequirePermissionAttribute("orders.write").Permission.Should().Be("orders.write");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_permission_is_refused_because_it_would_guard_nothing(string permission)
    {
        var declare = () => new RequirePermissionAttribute(permission);

        declare.Should().Throw<ArgumentException>().WithParameterName("permission");
    }

    [Fact]
    public void A_missing_permission_is_refused_too()
    {
        var declare = () => new RequirePermissionAttribute(null!);

        declare.Should().Throw<ArgumentNullException>().WithParameterName("permission");
    }

    /// <summary>
    /// A request may need two permissions, and a base command's declaration has to reach the commands
    /// derived from it. Both are attribute usage rather than prose, so both are pinned here.
    /// </summary>
    [Fact]
    public void The_declaration_may_be_repeated_and_is_inherited()
    {
        var usage = typeof(RequirePermissionAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.AllowMultiple.Should().BeTrue();
        usage.Inherited.Should().BeTrue();
        usage.ValidOn.Should().Be(AttributeTargets.Class | AttributeTargets.Struct);
    }

    [Fact]
    public void A_command_carries_every_permission_it_declares()
    {
        var declared = typeof(TwiceGuardedCommand)
            .GetCustomAttributes(typeof(RequirePermissionAttribute), inherit: true)
            .Cast<RequirePermissionAttribute>()
            .Select(attribute => attribute.Permission);

        declared.Should().BeEquivalentTo(["orders.read", "orders.write"]);
    }

    [RequirePermission("orders.read")]
    [RequirePermission("orders.write")]
    private sealed class TwiceGuardedCommand;
}
