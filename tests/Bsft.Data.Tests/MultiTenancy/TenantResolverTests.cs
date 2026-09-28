using System.Security.Claims;
using Bsft.Data.MultiTenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy;

public class TenantResolverTests
{
    [Fact]
    public async Task HeaderTenantResolver_NullContext_ThrowsArgumentNullException()
    {
        var resolver = new HeaderTenantResolver(Options());

        var act = async () => await resolver.ResolveAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public void HeaderTenantResolver_NullOptions_ThrowsArgumentNullException()
    {
        var act = () => new HeaderTenantResolver(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task HeaderTenantResolver_ReadsFromHeader()
    {
        var resolver = new HeaderTenantResolver(Options());
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[DataDefaults.TenantHeaderName] = "tenant-abc";

        var result = await resolver.ResolveAsync(httpContext);

        result.Should().Be("tenant-abc");
    }

    [Fact]
    public async Task HeaderTenantResolver_MissingHeader_ReturnsNull()
    {
        var resolver = new HeaderTenantResolver(Options());
        var httpContext = new DefaultHttpContext();

        var result = await resolver.ResolveAsync(httpContext);

        result.Should().BeNull();
    }

    [Fact]
    public async Task HeaderTenantResolver_CustomHeaderName_ReadsCorrectHeader()
    {
        var resolver = new HeaderTenantResolver(Options(o => o.HeaderName = "X-Custom-Tenant"));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Custom-Tenant"] = "custom-tenant";

        var result = await resolver.ResolveAsync(httpContext);

        result.Should().Be("custom-tenant");
    }

    [Fact]
    public async Task HeaderTenantResolver_HeaderOfExactlyTheMaximumLength_IsAccepted()
    {
        var resolver = new HeaderTenantResolver(Options());
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[DataDefaults.TenantHeaderName] = new string('a', 128);

        var result = await resolver.ResolveAsync(httpContext);

        result.Should().Be(new string('a', 128));
    }

    [Fact]
    public async Task HeaderTenantResolver_OversizedHeader_ReturnsNull()
    {
        var resolver = new HeaderTenantResolver(Options());
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[DataDefaults.TenantHeaderName] = new string('a', 129);

        var result = await resolver.ResolveAsync(httpContext);

        result.Should().BeNull();
    }

    [Fact]
    public async Task HeaderTenantResolver_WhitespaceHeader_ReturnsNull()
    {
        var resolver = new HeaderTenantResolver(Options());
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[DataDefaults.TenantHeaderName] = "   ";

        var result = await resolver.ResolveAsync(httpContext);

        result.Should().BeNull();
    }

    [Fact]
    public async Task HeaderTenantResolver_TrimsSurroundingWhitespace()
    {
        var resolver = new HeaderTenantResolver(Options());
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[DataDefaults.TenantHeaderName] = "  tenant-x  ";

        var result = await resolver.ResolveAsync(httpContext);

        result.Should().Be("tenant-x");
    }

    [Fact]
    public async Task ClaimTenantResolver_NullContext_ThrowsArgumentNullException()
    {
        var resolver = new ClaimTenantResolver(Options());

        var act = async () => await resolver.ResolveAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public void ClaimTenantResolver_NullOptions_ThrowsArgumentNullException()
    {
        var act = () => new ClaimTenantResolver(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task ClaimTenantResolver_ReadsFromClaim()
    {
        var resolver = new ClaimTenantResolver(Options());
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(DataDefaults.TenantClaimType, "tenant-from-claim")
            ]))
        };

        var result = await resolver.ResolveAsync(httpContext);

        result.Should().Be("tenant-from-claim");
    }

    [Fact]
    public async Task ClaimTenantResolver_CustomClaimType_ReadsCorrectClaim()
    {
        var resolver = new ClaimTenantResolver(Options(o => o.ClaimType = "org"));
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("org", "tenant-from-org")]))
        };

        var result = await resolver.ResolveAsync(httpContext);

        result.Should().Be("tenant-from-org");
    }

    [Fact]
    public async Task ClaimTenantResolver_MissingClaim_ReturnsNull()
    {
        var resolver = new ClaimTenantResolver(Options());
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity())
        };

        var result = await resolver.ResolveAsync(httpContext);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SubdomainTenantResolver_NullContext_ThrowsArgumentNullException()
    {
        var resolver = new SubdomainTenantResolver();

        var act = async () => await resolver.ResolveAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public async Task SubdomainTenantResolver_EmptyHost_ReturnsNull()
    {
        var resolver = new SubdomainTenantResolver();
        var httpContext = new DefaultHttpContext();

        var result = await resolver.ResolveAsync(httpContext);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SubdomainTenantResolver_ExtractsSubdomain()
    {
        var resolver = new SubdomainTenantResolver();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("acme.example.com");

        var result = await resolver.ResolveAsync(httpContext);

        result.Should().Be("acme");
    }

    [Fact]
    public async Task SubdomainTenantResolver_NoSubdomain_ReturnsNull()
    {
        var resolver = new SubdomainTenantResolver();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("example.com");

        var result = await resolver.ResolveAsync(httpContext);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SubdomainTenantResolver_Localhost_ReturnsNull()
    {
        var resolver = new SubdomainTenantResolver();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("localhost");

        var result = await resolver.ResolveAsync(httpContext);

        result.Should().BeNull();
    }

    private static IOptions<BsftDataOptions> Options(Action<MultiTenancyOptions>? configure = null)
    {
        var options = new BsftDataOptions();
        configure?.Invoke(options.MultiTenancy);
        return Microsoft.Extensions.Options.Options.Create(options);
    }
}
