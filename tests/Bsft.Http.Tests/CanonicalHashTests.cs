using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Bsft.Http.Tests;

public class CanonicalHashTests
{
    [Fact]
    public void ToCanonicalHash_Shim_ReturnsTheSameDigestAsCore()
    {
        var segments = new[] { "a", "b", null, "" };
        var shimResult = Bsft.Http.CanonicalHash.ToCanonicalHash(segments);
        var coreResult = Bsft.Core.CanonicalHash.ToCanonicalHash(segments);

        shimResult.Should().Be(coreResult);
    }

    [Fact]
    public void ToCanonicalHash_Shim_WithNullSegments_ThrowsArgumentNullException()
    {
        IEnumerable<string?> segments = null!;

        var act = () => Bsft.Http.CanonicalHash.ToCanonicalHash(segments);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("segments");
    }

    [Fact]
    public void ToCanonicalHash_Shim_WithEmptySegments_ReturnsDigest()
    {
        var digest = Bsft.Http.CanonicalHash.ToCanonicalHash([]);

        digest.Should().HaveLength(64);
        digest.Should().MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public void ToCanonicalHash_Shim_WithDifferentInput_ReturnsDifferentDigest()
    {
        var digest1 = Bsft.Http.CanonicalHash.ToCanonicalHash(["a"]);
        var digest2 = Bsft.Http.CanonicalHash.ToCanonicalHash(["b"]);

        digest1.Should().NotBe(digest2);
    }

    [Fact]
    public void CanonicalHash_Type_IsPublicStatic()
    {
        var type = typeof(Bsft.Http.CanonicalHash);

        type.IsPublic.Should().BeTrue();
        type.IsAbstract.Should().BeTrue(); // static classes are abstract
        type.IsSealed.Should().BeTrue(); // static classes are sealed
    }

    [Fact]
    public void CanonicalHash_Type_IsInHttpAssembly()
    {
        var type = typeof(Bsft.Http.CanonicalHash);

        type.Assembly.GetName().Name.Should().Be("Bsft.Http");
    }

    [Fact]
    public void CanonicalHash_Type_HasEditorBrowsableNever()
    {
        var type = typeof(Bsft.Http.CanonicalHash);
        var attr = type.GetCustomAttribute<EditorBrowsableAttribute>();

        attr.Should().NotBeNull();
        attr!.State.Should().Be(EditorBrowsableState.Never);
    }

    [Fact]
    public void CanonicalHash_ToCanonicalHashMethod_HasCorrectSignature()
    {
        var type = typeof(Bsft.Http.CanonicalHash);
        var method = type.GetMethod("ToCanonicalHash",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            null,
            [typeof(IEnumerable<string?>)],
            null);

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(string));
        method.IsPublic.Should().BeTrue();
        method.IsStatic.Should().BeTrue();
    }
}
