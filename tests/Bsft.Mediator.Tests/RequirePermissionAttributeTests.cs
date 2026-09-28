using FluentAssertions;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>
/// The web-free way for a request to declare the permission it requires. Enforcing it is
/// <c>Bsft.Auth.Mediator</c>'s job and is tested there; what is settled here is that a marked request
/// states a usable permission at all.
/// </summary>
public sealed class RequirePermissionAttributeTests
{
    [Fact]
    public void The_permission_is_the_one_the_request_stated()
        => new RequirePermissionAttribute("credentials.manage").Permission.Should().Be("credentials.manage");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_permission_is_refused_where_it_is_written_rather_than_where_it_is_read(string permission)
        => FluentActions.Invoking(() => new RequirePermissionAttribute(permission))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void A_missing_permission_is_refused()
        => FluentActions.Invoking(() => new RequirePermissionAttribute(null!))
            .Should().Throw<ArgumentNullException>();

    [Fact]
    public void A_request_may_demand_more_than_one_permission()
        => typeof(TwicePermissioned)
            .GetCustomAttributes(typeof(RequirePermissionAttribute), inherit: true)
            .Should().HaveCount(2);

    [RequirePermission("flags.manage")]
    [RequirePermission("flags.kill")]
    private sealed record TwicePermissioned;
}
