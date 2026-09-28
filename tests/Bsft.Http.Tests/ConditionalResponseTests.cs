using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Bsft.Http.Tests;

public class ConditionalResponseTests
{
    [Fact]
    public void UseRevalidation_NullResponse_ThrowsArgumentNullException()
    {
        HttpResponse response = null!;

        var act = () => response.UseRevalidation("\"sha256-abc\"");

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("response");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UseRevalidation_BlankTag_ThrowsArgumentException(string? entityTag)
    {
        var response = new DefaultHttpContext().Response;

        var act = () => response.UseRevalidation(entityTag!);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("entityTag");
    }

    [Fact]
    public void UseRevalidation_Tag_WritesTheValidatorAndThePolicyTogether()
    {
        var response = new DefaultHttpContext().Response;

        response.UseRevalidation("\"sha256-abc\"");

        response.Headers.ETag.ToString().Should().Be("\"sha256-abc\"");
        response.Headers.CacheControl.ToString().Should().Be("private, no-cache");
    }

    [Fact]
    public void PrivateRevalidate_KeepsSharedCachesOutAndStillAllowsTheClientToKeepTheBody()
    {
        // private: no proxy or CDN may store a representation that depends on who asked for it.
        // no-cache: the client may keep its copy but has to revalidate, which is what makes the
        // entity tag worth serving at all. no-store would forbid the copy and defeat the point.
        ConditionalResponse.PrivateRevalidate.Should().Be("private, no-cache");
        ConditionalResponse.PrivateRevalidate.Should().NotContain("no-store");
        ConditionalResponse.PrivateRevalidate.Should().NotContain("public");
    }
}
