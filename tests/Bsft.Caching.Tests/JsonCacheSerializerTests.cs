using FluentAssertions;
using System.Text;
using Xunit;

namespace Bsft.Caching.Tests;

public class JsonCacheSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesTheValue()
    {
        var serializer = new JsonCacheSerializer();

        var payload = serializer.Serialize(new Person("Ada"));

        serializer.Deserialize<Person>(payload).Should().Be(new Person("Ada"));
    }

    [Fact]
    public void Serialize_UsesWebNamingSoPayloadsAreInteroperable()
    {
        var payload = new JsonCacheSerializer().Serialize(new Person("Ada"));

        Encoding.UTF8.GetString(payload).Should().Be("""{"name":"Ada"}""");
    }

    [Fact]
    public void Serialize_ValueThatCannotBeWritten_ThrowsCacheSerializationException()
    {
        // A cycle is unwritable, and it has to surface the same way an unreadable payload does:
        // as a caching problem, so the behavior can degrade instead of failing a query whose
        // handler has already produced the answer.
        var node = new CyclicNode();
        node.Self = node;

        var act = () => new JsonCacheSerializer().Serialize(node);

        act.Should().Throw<CacheSerializationException>()
            .WithInnerException<System.Text.Json.JsonException>();
    }

    [Fact]
    public void Serialize_TypeTheSerializerRefuses_ThrowsCacheSerializationException()
    {
        // Serializing a Type instance is refused outright rather than reported as bad JSON, so it
        // takes the other branch out of Serialize and has to be named the same way.
        var act = () => new JsonCacheSerializer().Serialize(typeof(Person));

        act.Should().Throw<CacheSerializationException>()
            .WithInnerException<NotSupportedException>();
    }

    private sealed class CyclicNode
    {
        public CyclicNode? Self { get; set; }
    }

    [Fact]
    public void Deserialize_NullLiteral_ReturnsNull()
    {
        new JsonCacheSerializer().Deserialize<Person>("null"u8).Should().BeNull();
    }

    [Fact]
    public void Deserialize_ResponseTypeThatCannotBeRead_ThrowsCacheSerializationException()
    {
        // An interface response type serializes but cannot be deserialized, and must surface as
        // the same unusable-entry failure as a malformed payload.
        var act = () => new JsonCacheSerializer().Deserialize<ICacheable>("{}"u8);

        act.Should().Throw<CacheSerializationException>()
            .WithInnerException<NotSupportedException>();
    }

    [Fact]
    public void Deserialize_UnreadablePayload_ThrowsCacheSerializationException()
    {
        var act = () => new JsonCacheSerializer().Deserialize<Person>("not json"u8);

        act.Should().Throw<CacheSerializationException>()
            .WithInnerException<System.Text.Json.JsonException>();
    }
}
