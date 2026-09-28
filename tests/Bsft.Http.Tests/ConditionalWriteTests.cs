using FluentAssertions;
using Xunit;

namespace Bsft.Http.Tests;

public class ConditionalWriteTests
{
    private static readonly Guid Version = new("11111111-2222-3333-4444-555555555555");

    private static readonly string Current = Version.ToVersionEntityTag();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsMatchedBy_BlankCurrentTag_ThrowsArgumentException(string? current)
    {
        var act = () => current!.IsMatchedBy(Current);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("currentEntityTag");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsMatchedBy_AbsentHeader_DoesNotMatch(string? ifMatch)
    {
        // Answered rather than thrown: the endpoint decides whether a missing header is a 428, and
        // it decides that before anything has been read. Everything reaching here fails closed.
        Current.IsMatchedBy(ifMatch).Should().BeFalse();
    }

    [Fact]
    public void IsMatchedBy_CurrentTag_Matches()
    {
        Current.IsMatchedBy(Current).Should().BeTrue();
    }

    [Fact]
    public void IsMatchedBy_Wildcard_MatchesWhateverVersionTheResourceHas()
    {
        Current.IsMatchedBy("*").Should().BeTrue();
    }

    [Fact]
    public void IsMatchedBy_ListContainingTheCurrentTag_Matches()
    {
        Current.IsMatchedBy($"\"sha256-stale\", {Current}").Should().BeTrue();
    }

    [Fact]
    public void IsMatchedBy_SurroundingWhitespace_Matches()
    {
        Current.IsMatchedBy($"  {Current}  ").Should().BeTrue();
    }

    [Fact]
    public void IsMatchedBy_TagOfAnotherVersion_DoesNotMatch()
    {
        var stale = Guid.NewGuid().ToVersionEntityTag();

        Current.IsMatchedBy(stale).Should().BeFalse();
    }

    [Fact]
    public void IsMatchedBy_WeakFormOfTheCurrentTag_DoesNotMatch()
    {
        // RFC 9110 requires strong comparison for If-Match. A weak validator says "equivalent for
        // caching", which is not the same claim as "this is the version I edited".
        Current.IsMatchedBy($"W/{Current}").Should().BeFalse();
    }

    [Theory]
    [InlineData("sha256-unquoted")]
    [InlineData("not a tag at all")]
    [InlineData(",,,")]
    [InlineData("**")]
    public void IsMatchedBy_MalformedOrForeignHeader_DoesNotMatch(string ifMatch)
    {
        Current.IsMatchedBy(ifMatch).Should().BeFalse();
    }

    [Fact]
    public void IsMatchedBy_UnquotedSpellingOfTheCurrentTag_DoesNotMatch()
    {
        // The opposite reading to If-None-Match, deliberately. There the answer is "you already have
        // this body" and a generous comparison costs a round-trip; here it authorises an overwrite.
        Current.IsMatchedBy(Current.Trim('"')).Should().BeFalse();
    }
}
