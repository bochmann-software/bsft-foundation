using FluentAssertions;
using Xunit;

namespace Bsft.Http.Tests;

public class EntityTagTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToContentHashEntityTag_BlankHash_ThrowsArgumentException(string? hash)
    {
        var act = () => hash!.ToContentHashEntityTag();

        act.Should().Throw<ArgumentException>()
            .WithParameterName("sha256");
    }

    [Fact]
    public void ToContentHashEntityTag_Hash_IsQuotedStrongAndPrefixed()
    {
        var tag = "abc123".ToContentHashEntityTag();

        tag.Should().Be("\"sha256-abc123\"");
        tag.Should().NotStartWith("W/");
    }

    [Fact]
    public void ToContentHashEntityTag_HashCarryingHeaderBreakingCharacters_DropsThem()
    {
        // Nothing in the framework produces such a value, which is exactly why the guard has to be
        // here: the day something does, the alternative is a response with a header nobody wrote.
        var tag = "ab\r\nX-Injected: 1\"c".ToContentHashEntityTag();

        tag.Should().Be("\"sha256-abX-Injected:1c\"");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToVersionEntityTag_BlankVersion_ThrowsArgumentException(string? version)
    {
        var act = () => version!.ToVersionEntityTag();

        act.Should().Throw<ArgumentException>()
            .WithParameterName("version");
    }

    [Fact]
    public void ToVersionEntityTag_Version_IsAStrongContentHashTag()
    {
        // A monotonic token published verbatim would tell every caller how often the row has been
        // written to, so the tag is the digest and carries 64 hexadecimal characters, not the token.
        var tag = "7".ToVersionEntityTag();

        tag.Should().MatchRegex("^\"sha256-[0-9a-f]{64}\"$");
        tag.Should().NotStartWith("W/");
    }

    [Fact]
    public void ToVersionEntityTag_SameVersion_IsTheSameTag()
    {
        // The whole contract: a read and the write checked against it must derive one value.
        "7".ToVersionEntityTag().Should().Be("7".ToVersionEntityTag());
    }

    [Fact]
    public void ToVersionEntityTag_DifferentVersions_AreDifferentTags()
    {
        "7".ToVersionEntityTag().Should().NotBe("8".ToVersionEntityTag());
    }

    [Fact]
    public void ToVersionEntityTag_Version_IsTheCanonicalDigestOfThatOneSegment()
    {
        // Pinned against the composition rather than against a literal: the point is that a version
        // tag and a one-segment content tag of the same text are the same tag, so a resource can
        // move from one to the other without every client revalidating.
        "7".ToVersionEntityTag().Should().Be(new[] { "7" }.ToCanonicalHash().ToContentHashEntityTag());
    }

    [Fact]
    public void ToVersionEntityTag_Guid_IsTheTagOfItsUnhyphenatedSpelling()
    {
        var version = new Guid("11111111-2222-3333-4444-555555555555");

        // The "N" format is part of the contract, not an implementation detail: a caller that
        // spelled the same version with hyphens would derive a second tag for one resource.
        version.ToVersionEntityTag().Should().Be("11111111222233334444555555555555".ToVersionEntityTag());
    }

    [Fact]
    public void ToVersionEntityTag_DifferentGuids_AreDifferentTags()
    {
        Guid.NewGuid().ToVersionEntityTag().Should().NotBe(Guid.NewGuid().ToVersionEntityTag());
    }

    [Fact]
    public void ContentHashPrefix_MatchesTheShapeTheStorageDownloadsServe()
    {
        // A client revalidating a stored file and one revalidating a permission map must not have
        // to know two tag grammars.
        EntityTag.ContentHashPrefix.Should().Be("sha256-");
    }
}
