using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Bsft.Http.Tests;

public class ConditionalRequestTests
{
    private const string Served = "\"sha256-abc\"";

    [Fact]
    public void IsUnchangedFrom_NullRequest_ThrowsArgumentNullException()
    {
        HttpRequest request = null!;

        var act = () => request.IsUnchangedFrom(Served);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("request");
    }

    [Fact]
    public void IsUnchangedFrom_NoTagToCompare_IsNeverUnchanged()
    {
        // Answering 304 on the strength of the client's assertion alone would let a stale cache pin
        // itself for ever.
        Request("*").IsUnchangedFrom(null).Should().BeFalse();
    }

    [Fact]
    public void IsUnchangedFrom_NoHeader_IsNotUnchanged()
    {
        Request(null).IsUnchangedFrom(Served).Should().BeFalse();
    }

    [Theory]
    [InlineData("\"sha256-abc\"")]
    [InlineData("W/\"sha256-abc\"")]
    [InlineData("  \"sha256-abc\"  ")]
    [InlineData("*")]
    [InlineData("\"sha256-other\", \"sha256-abc\"")]
    public void IsUnchangedFrom_MatchingHeader_IsUnchanged(string header)
    {
        Request(header).IsUnchangedFrom(Served).Should().BeTrue();
    }

    [Theory]
    [InlineData("\"sha256-other\"")]
    [InlineData("\"sha256-other\", \"sha256-different\"")]
    [InlineData("sha256-abc-and-more")]
    [InlineData("not a tag at all")]
    [InlineData("W/")]
    [InlineData("\"\"")]
    [InlineData("   ")]
    [InlineData(",,,")]
    public void IsUnchangedFrom_MalformedOrForeignHeader_IsIgnored(string header)
    {
        // Ignored means the full representation is served. That is the only safe direction to fail
        // in: a malformed validator answered with 304 would hand a client a body it never received.
        Request(header).IsUnchangedFrom(Served).Should().BeFalse();
    }

    [Fact]
    public void IsUnchangedFrom_UnquotedButEqualTag_IsUnchanged()
    {
        // Two spellings of the same opaque tag are the same tag.
        Request("sha256-abc").IsUnchangedFrom(Served).Should().BeTrue();
    }

    private static HttpRequest Request(string? ifNoneMatch)
    {
        var context = new DefaultHttpContext();

        if (ifNoneMatch is not null)
            context.Request.Headers.IfNoneMatch = ifNoneMatch;

        return context.Request;
    }
}
