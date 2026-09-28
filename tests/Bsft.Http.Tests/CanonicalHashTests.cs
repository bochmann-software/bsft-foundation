using FluentAssertions;
using Xunit;

namespace Bsft.Http.Tests;

public class CanonicalHashTests
{
    [Fact]
    public void ToCanonicalHash_NullSegments_ThrowsArgumentNullException()
    {
        IEnumerable<string?> segments = null!;

        var act = () => segments.ToCanonicalHash();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("segments");
    }

    [Fact]
    public void ToCanonicalHash_NoSegments_IsTheDigestOfNothing()
    {
        // The empty input still has to produce a usable tag body rather than an empty string, or an
        // empty representation would be served without a validator.
        var digest = Hash();

        digest.Should().HaveLength(64);
        digest.Should().MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public void ToCanonicalHash_SameSegments_IsStableAcrossCalls()
    {
        var first = Hash("a", "b", null, "");
        var second = Hash("a", "b", null, "");

        second.Should().Be(first);
    }

    [Fact]
    public void ToCanonicalHash_DifferentSegments_DiffersByContent()
    {
        Hash("a").Should().NotBe(Hash("b"));
    }

    [Fact]
    public void ToCanonicalHash_SegmentBoundaryMoved_StillDiffers()
    {
        // The property the length prefixing exists for: without it, both inputs would hash the
        // bytes "abc" and two different contents would share one entity tag.
        Hash("ab", "c").Should().NotBe(Hash("a", "bc"));
    }

    [Fact]
    public void ToCanonicalHash_AbsentSegment_DiffersFromEmptyOne()
    {
        // A tenant that is not there and a tenant whose id is the empty string are different facts.
        Hash((string?)null).Should().NotBe(Hash(""));
    }

    [Fact]
    public void ToCanonicalHash_NonAsciiSegment_IsHashedAsUtf8()
    {
        // Translations are the whole point of one of the two callers, so the digest has to survive
        // content that is not ASCII rather than throw or lose characters.
        var digest = Hash("grüße", "日本語");

        digest.Should().HaveLength(64);
        digest.Should().NotBe(Hash("grusse", "日本語"));
    }

    [Fact]
    public void ToCanonicalHash_KnownInput_IsTheDocumentedDigest()
    {
        // Pins the wire format: one segment "a" is hashed as the UTF-8 of "1:a". A change to the
        // framing is a change to every client's cached tag, so it has to be a deliberate edit of
        // this expectation rather than a silent drift.
        Hash("a").Should()
            .Be("4162fddd39a3e4225e8e2392eced237fbeb34e6e218b5647d27bd4d2b9c0da24");
    }

    [Fact]
    public void ToCanonicalHash_ASegmentNeedingSeveralDigits_IsFramedWithAllOfThem()
    {
        // The length prefix is formatted a digit at a time into a fixed buffer, so a segment long
        // enough to need more than one digit exercises a path a short one never reaches. Ten bytes
        // frames as "10:0123456789", and the digest below is that string's.
        Hash("0123456789").Should()
            .Be("77c73549a03aabc30f5c6a2d54311767fd63339b6e51de76f0c4307a5379eca3");
    }

    private static string Hash(params string?[] segments) => segments.ToCanonicalHash();
}
