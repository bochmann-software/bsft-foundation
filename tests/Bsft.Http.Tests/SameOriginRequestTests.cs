using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Bsft.Http.Tests;

public class SameOriginRequestTests
{
    private const string PublicOrigin = "https://app.example";

    [Fact]
    public void IsSameOrigin_NullRequest_ThrowsArgumentNullException()
    {
        HttpRequest request = null!;

        var act = () => request.IsSameOrigin(PublicOrigin);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("request");
    }

    [Theory]
    [InlineData("same-origin")]
    [InlineData("Same-Origin")]
    public void IsSameOrigin_NoOriginAndSameOriginFetchSite_IsSameOrigin(string fetchSite)
    {
        Request(fetchSite: fetchSite).IsSameOrigin(PublicOrigin).Should().BeTrue();
    }

    [Fact]
    public void IsSameOrigin_NoOriginAndSameSiteFetchSite_IsNotSameOrigin()
    {
        Request(fetchSite: "same-site").IsSameOrigin(PublicOrigin).Should().BeFalse();
    }

    [Fact]
    public void IsSameOrigin_NoOriginAndNoFetchMetadata_IsNotSameOrigin()
    {
        Request().IsSameOrigin(PublicOrigin).Should().BeFalse();
    }

    [Fact]
    public void IsSameOrigin_OriginEqualToPublicOrigin_IsSameOrigin()
    {
        Request(origin: PublicOrigin).IsSameOrigin(PublicOrigin).Should().BeTrue();
    }

    [Fact]
    public void IsSameOrigin_PublicOriginWithTrailingSlash_IsSameOrigin()
    {
        Request(origin: PublicOrigin).IsSameOrigin(PublicOrigin + "/").Should().BeTrue();
    }

    [Fact]
    public void IsSameOrigin_OriginDiffersInCase_IsSameOrigin()
    {
        Request(origin: "HTTPS://App.Example").IsSameOrigin(PublicOrigin).Should().BeTrue();
    }

    [Fact]
    public void IsSameOrigin_SameOriginFetchSiteWithAnOriginThatDoesNotMatch_IsSameOrigin()
    {
        // Fetch metadata decides when present: behind a TLS-terminating proxy that forwards no scheme the
        // Origin the host compares with differs, and the browser-set header is the stronger signal.
        Request(origin: "https://app.example", fetchSite: "same-origin", scheme: "http")
            .IsSameOrigin(publicOrigin: null).Should().BeTrue();
    }

    [Theory]
    [InlineData("cross-site")]
    [InlineData("same-site")]
    [InlineData("none")]
    public void IsSameOrigin_OtherFetchSiteWithAMatchingOrigin_IsNotSameOrigin(string fetchSite)
    {
        Request(origin: PublicOrigin, fetchSite: fetchSite).IsSameOrigin(PublicOrigin).Should().BeFalse();
    }

    [Fact]
    public void IsSameOrigin_TwoFetchSiteValues_IsNotSameOrigin()
    {
        var request = Request(origin: PublicOrigin);
        request.Headers["Sec-Fetch-Site"] = new StringValues(new[] { "same-origin", "same-origin" });

        request.IsSameOrigin(PublicOrigin).Should().BeFalse();
    }

    [Fact]
    public void IsSameOrigin_NullOriginLiteralWithoutFetchMetadata_IsNotSameOrigin()
    {
        Request(origin: "null").IsSameOrigin(PublicOrigin).Should().BeFalse();
    }

    [Fact]
    public void IsSameOrigin_TwoIdenticalOriginHeaders_IsNotSameOrigin()
    {
        var request = Request();
        request.Headers["Origin"] = new StringValues(new[] { PublicOrigin, PublicOrigin });

        request.IsSameOrigin(PublicOrigin).Should().BeFalse();
    }

    [Fact]
    public void IsSameOrigin_NullPublicOriginMatchesRequestSchemeAndHost_IsSameOrigin()
    {
        Request(origin: "https://app.example").IsSameOrigin(null).Should().BeTrue();
    }

    [Fact]
    public void IsSameOrigin_EmptyPublicOriginMatchesRequestSchemeAndHost_IsSameOrigin()
    {
        Request(origin: "https://app.example").IsSameOrigin(string.Empty).Should().BeTrue();
    }

    [Fact]
    public void IsSameOrigin_NullPublicOriginAndSchemeMismatch_IsNotSameOrigin()
    {
        // The request arrived over http, so an https origin is not its own origin.
        Request(scheme: "http", origin: "https://app.example").IsSameOrigin(null).Should().BeFalse();
    }

    private static HttpRequest Request(string? origin = null, string? fetchSite = null, string scheme = "https")
    {
        var request = new DefaultHttpContext().Request;
        request.Scheme = scheme;
        request.Host = new HostString("app.example");

        if (origin is not null)
            request.Headers["Origin"] = origin;

        if (fetchSite is not null)
            request.Headers["Sec-Fetch-Site"] = fetchSite;

        return request;
    }
}
