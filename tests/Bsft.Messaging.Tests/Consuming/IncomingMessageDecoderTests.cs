using Bsft.Messaging.Consuming;
using Bsft.Messaging.Tests.Fakes;
using Bsft.Messaging.Tests.Support;
using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace Bsft.Messaging.Tests.Consuming;

public class IncomingMessageDecoderTests
{
    private readonly IIntegrationEventTypeRegistry _registry;
    private readonly IIntegrationEventSerializer _serializer;

    public IncomingMessageDecoderTests()
    {
        var options = new BsftMessagingOptions();
        options.AddEvent<OrderPlacedEvent>("orders.placed.v1");
        _registry = new IntegrationEventTypeRegistry(TestMessagingOptions.For(options));
        _serializer = new JsonIntegrationEventSerializer(_registry);
    }

    private IncomingMessageDecoder CreateDecoder() => new(_registry, _serializer);

    private static IntegrationEventMessage Message(
        string eventName = "orders.placed.v1", string? payload = null, int version = IntegrationEventMessage.CurrentVersion) => new()
        {
            MessageVersion = version,
            EventId = Guid.NewGuid(),
            EventName = eventName,
            OccurredAt = DateTimeOffset.UnixEpoch,
            Payload = payload ?? JsonSerializer.Serialize(new OrderPlacedEvent { OrderNumber = "A-1" }),
        };

    [Fact]
    public void A_registered_event_is_decoded_into_its_declared_type()
    {
        var decoded = CreateDecoder().Decode(Message());

        decoded.Should().BeOfType<OrderPlacedEvent>().Which.OrderNumber.Should().Be("A-1");
    }

    [Fact]
    public void An_unregistered_event_name_is_refused_rather_than_resolved()
    {
        // The wire must never choose the CLR type: a name nobody declared has no type to become.
        var act = () => CreateDecoder().Decode(Message("orders.unknown.v1"));

        act.Should().Throw<MessageDecodeException>()
            .Which.ParkReason.Should().Be(MessageParkReason.UnknownEventName);
    }

    [Fact]
    public void A_message_from_a_newer_contract_is_refused_before_anything_else_is_read()
    {
        var act = () => CreateDecoder().Decode(
            Message("orders.unknown.v1", version: IntegrationEventMessage.CurrentVersion + 1));

        // Reported as the version problem, not as the unknown name it also has: a message whose
        // contract this consumer does not understand cannot be judged by its other fields.
        act.Should().Throw<MessageDecodeException>()
            .Which.ParkReason.Should().Be(MessageParkReason.UnsupportedMessageVersion);
    }

    [Fact]
    public void A_message_from_an_older_contract_is_decoded_as_it_stands()
    {
        var decoded = CreateDecoder().Decode(Message(version: IntegrationEventMessage.CurrentVersion - 1));

        decoded.Should().BeOfType<OrderPlacedEvent>();
    }

    [Fact]
    public void A_payload_that_is_not_valid_json_is_refused_as_undecodable()
    {
        var act = () => CreateDecoder().Decode(Message(payload: "{ this is not json"));

        var refusal = act.Should().Throw<MessageDecodeException>().Subject.Single();
        refusal.ParkReason.Should().Be(MessageParkReason.UndecodablePayload);
        refusal.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public void A_payload_that_decodes_to_nothing_is_refused_as_undecodable()
    {
        var act = () => CreateDecoder().Decode(Message(payload: "null"));

        act.Should().Throw<MessageDecodeException>()
            .Which.ParkReason.Should().Be(MessageParkReason.UndecodablePayload);
    }

    [Fact]
    public void A_null_message_is_rejected()
    {
        var act = () => CreateDecoder().Decode(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("message");
    }

    [Fact]
    public void Null_dependencies_are_rejected()
    {
        var withoutRegistry = () => new IncomingMessageDecoder(null!, _serializer);
        var withoutSerializer = () => new IncomingMessageDecoder(_registry, null!);

        withoutRegistry.Should().Throw<ArgumentNullException>().WithParameterName("registry");
        withoutSerializer.Should().Throw<ArgumentNullException>().WithParameterName("serializer");
    }
}
