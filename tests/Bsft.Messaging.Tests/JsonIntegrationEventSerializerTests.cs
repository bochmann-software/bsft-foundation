using Bsft.Messaging.Tests.Fakes;
using Bsft.Messaging.Tests.Support;
using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace Bsft.Messaging.Tests;

public class JsonIntegrationEventSerializerTests
{
    private readonly JsonIntegrationEventSerializer _serializer;

    public JsonIntegrationEventSerializerTests()
    {
        var options = new BsftMessagingOptions();
        options.AddEvent<OrderPlacedEvent>("orders.placed.v1");
        options.AddEvent<UndecodableEvent>("orders.undecodable.v1");
        _serializer = new JsonIntegrationEventSerializer(new IntegrationEventTypeRegistry(TestMessagingOptions.For(options)));
    }

    private static IntegrationEventMessage WireMessage(string payload, string eventName = "orders.placed.v1") => new()
    {
        EventId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        EventName = eventName,
        Payload = payload,
    };

    [Fact]
    public void An_event_survives_the_round_trip_with_the_properties_of_its_own_type()
    {
        var integrationEvent = new OrderPlacedEvent
        {
            OrderNumber = "A-1",
            OccurredAt = new DateTimeOffset(2026, 6, 22, 6, 0, 0, TimeSpan.Zero),
        };

        var restored = _serializer.Deserialize(WireMessage(_serializer.Serialize(integrationEvent)));

        // Encoded against the runtime type rather than the interface, so the properties the event
        // actually carries are on the wire and not just the two the interface declares.
        restored.Should().BeOfType<OrderPlacedEvent>().Which.Should().Be(integrationEvent);
    }

    [Fact]
    public void An_unregistered_event_name_is_refused_rather_than_resolved()
    {
        var act = () => _serializer.Deserialize(WireMessage("{}", "orders.retired.v1"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*not registered*");
    }

    [Fact]
    public void A_payload_that_is_not_valid_json_is_refused_through_the_documented_type()
    {
        // Reported as the InvalidOperationException the contract names rather than as the JsonException
        // this implementation happens to raise: callers act on the contract, not on the encoding.
        var act = () => _serializer.Deserialize(WireMessage("{ this is not json"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*could not be decoded*")
            .WithInnerException<JsonException>();
    }

    [Fact]
    public void A_member_no_converter_can_read_is_refused_through_the_documented_type()
    {
        // System.Text.Json reports this one as NotSupportedException rather than JsonException. It has
        // to be translated too, or it escapes every caller written against the contract — and a
        // consumer that never sees it never completes the message either.
        var act = () => _serializer.Deserialize(
            WireMessage("""{"Value":"System.String"}""", "orders.undecodable.v1"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*could not be decoded*")
            .WithInnerException<NotSupportedException>();
    }

    [Fact]
    public void A_payload_that_decodes_to_nothing_is_refused()
    {
        var act = () => _serializer.Deserialize(WireMessage("null"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*did not decode*");
    }

    [Fact]
    public void Serializing_null_is_rejected()
    {
        var act = () => _serializer.Serialize(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("integrationEvent");
    }

    [Fact]
    public void Deserializing_null_is_rejected()
    {
        var act = () => _serializer.Deserialize(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("message");
    }

    [Fact]
    public void The_registry_is_required()
    {
        var act = () => new JsonIntegrationEventSerializer(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("registry");
    }
}
