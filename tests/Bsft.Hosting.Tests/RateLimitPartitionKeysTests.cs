using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace Bsft.Hosting.Tests;

public sealed class RateLimitPartitionKeysTests
{
    [Fact]
    public void ClientAddress_WithRemoteAddress_ReturnsAddressString()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.5");

        var address = RateLimitPartitionKeys.ClientAddress(context);

        address.Should().Be("203.0.113.5");
    }

    [Fact]
    public void ClientAddress_WithoutRemoteAddress_ReturnsUnattributable()
    {
        var context = new DefaultHttpContext();

        var address = RateLimitPartitionKeys.ClientAddress(context);

        address.Should().Be("unattributable");
    }

    [Fact]
    public void For_ClientAddressPartition_ReturnsAddressPrefixed()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.5");
        var options = new PolicyLimiterOptions { PartitionBy = RateLimitPartitionKind.ClientAddress };

        var key = RateLimitPartitionKeys.For(options, selector: null, context);

        key.Should().Be("address:203.0.113.5");
    }

    [Fact]
    public void For_GlobalPartition_ReturnsGlobal()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.5");
        var options = new PolicyLimiterOptions { PartitionBy = RateLimitPartitionKind.Global };

        var key = RateLimitPartitionKeys.For(options, selector: null, context);

        key.Should().Be("global");
    }

    [Fact]
    public void For_PrincipalWithNameIdentifierClaim_ReturnsPrincipalPrefixed()
    {
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "guest-1")], authenticationType: "test");
        context.User = new ClaimsPrincipal(identity);

        var options = new PolicyLimiterOptions { PartitionBy = RateLimitPartitionKind.Principal };

        var key = RateLimitPartitionKeys.For(options, selector: null, context);

        key.Should().Be("principal:guest-1");
    }

    [Fact]
    public void For_PrincipalWithCustomClaimType_UsesCustomClaim()
    {
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity([new Claim("sid", "session-123")], authenticationType: "test");
        context.User = new ClaimsPrincipal(identity);

        var options = new PolicyLimiterOptions { PartitionBy = RateLimitPartitionKind.Principal, ClaimType = "sid" };

        var key = RateLimitPartitionKeys.For(options, selector: null, context);

        key.Should().Be("principal:session-123");
    }

    [Fact]
    public void For_AnonymousPrincipalWithClaim_FallsBackToAddress()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.5");
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "guest-1")]);
        context.User = new ClaimsPrincipal(identity);

        var options = new PolicyLimiterOptions { PartitionBy = RateLimitPartitionKind.Principal };

        var key = RateLimitPartitionKeys.For(options, selector: null, context);

        key.Should().Be("address:203.0.113.5");
    }

    [Fact]
    public void For_AuthenticatedPrincipalWithoutClaimOrEmptyClaimValue_FallsBackToAddress()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.5");

        var options = new PolicyLimiterOptions { PartitionBy = RateLimitPartitionKind.Principal };

        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sid", "s1")], authenticationType: "test"));
        RateLimitPartitionKeys.For(options, selector: null, context).Should().Be("address:203.0.113.5");

        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "")], authenticationType: "test"));
        RateLimitPartitionKeys.For(options, selector: null, context).Should().Be("address:203.0.113.5");
    }

    [Fact]
    public void For_CustomSelectorReturnsValue_ReturnsCustomPrefixed()
    {
        var context = new DefaultHttpContext();

        var options = new PolicyLimiterOptions { PartitionBy = RateLimitPartitionKind.Custom };
        var selector = (HttpContext _) => "s1";

        var key = RateLimitPartitionKeys.For(options, selector, context);

        key.Should().Be("custom:s1");
    }

    [Fact]
    public void For_CustomSelectorReturnsNullOrEmpty_FallsBackToAddress()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.5");

        var options = new PolicyLimiterOptions { PartitionBy = RateLimitPartitionKind.Custom };

        var keyFromNull = RateLimitPartitionKeys.For(options, _ => null, context);
        keyFromNull.Should().Be("address:203.0.113.5");

        var keyFromEmpty = RateLimitPartitionKeys.For(options, _ => "", context);
        keyFromEmpty.Should().Be("address:203.0.113.5");
    }

    [Fact]
    public void For_CustomSelectorNull_FallsBackToAddress()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.5");

        var options = new PolicyLimiterOptions { PartitionBy = RateLimitPartitionKind.Custom };

        var key = RateLimitPartitionKeys.For(options, selector: null, context);

        key.Should().Be("address:203.0.113.5");
    }
}
