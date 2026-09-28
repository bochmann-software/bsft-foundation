using Bsft.Messaging.RabbitMQ.Tests.Support;
using FluentAssertions;
using RabbitMQ.Client;
using System.Globalization;
using System.Text;
using Xunit;

namespace Bsft.Messaging.RabbitMQ.Tests;

public class RabbitMQMessageMapperTests
{
    private static readonly DateTimeOffset _occurredAt =
        new(2026, 8, 11, 9, 30, 15, 456, TimeSpan.Zero);

    private readonly RabbitMQMessageMapper _mapper = new(new RabbitMQConnectionSettings(TestOptions.Valid()));

    [Fact]
    public void A_null_argument_is_rejected_on_every_direction()
    {
        var toProperties = () => _mapper.ToProperties(null!);
        var toBody = () => _mapper.ToBody(null!);
        var toMessage = () => _mapper.ToMessage(null!);

        toProperties.Should().Throw<ArgumentNullException>();
        toBody.Should().Throw<ArgumentNullException>();
        toMessage.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_null_settings_object_is_rejected()
    {
        var construct = () => new RabbitMQMessageMapper(null!);

        construct.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Every_envelope_field_lands_on_the_amqp_property_that_means_the_same_thing()
    {
        var message = Message();

        var properties = _mapper.ToProperties(message);

        properties.MessageId.Should().Be(message.EventId.ToString());
        properties.CorrelationId.Should().Be("correlation-1");
        properties.Type.Should().Be("orders.placed.v1");
        properties.Timestamp.UnixTime.Should().Be(_occurredAt.ToUnixTimeSeconds());
        properties.ContentType.Should().Be("application/json");
        properties.ContentEncoding.Should().Be("utf-8");
        properties.AppId.Should().Be("orders");
        properties.Headers.Should().ContainKey(RabbitMQMessageMapper.MessageVersionHeader)
            .WhoseValue.Should().Be(IntegrationEventMessage.CurrentVersion);
    }

    [Fact]
    public void The_message_is_persistent_because_the_outbox_has_already_forgotten_it()
    {
        var properties = _mapper.ToProperties(Message());

        properties.Persistent.Should().BeTrue();
        properties.DeliveryMode.Should().Be(DeliveryModes.Persistent);
    }

    [Fact]
    public void The_content_type_follows_the_configured_serializer_encoding()
    {
        var options = TestOptions.Valid();
        options.ContentType = "application/x-custom";
        var mapper = new RabbitMQMessageMapper(new RabbitMQConnectionSettings(options));

        mapper.ToProperties(Message()).ContentType.Should().Be("application/x-custom");
    }

    [Fact]
    public void An_absent_correlation_id_stays_absent_rather_than_becoming_an_empty_one()
    {
        var properties = _mapper.ToProperties(Message() with { CorrelationId = null });

        properties.CorrelationId.Should().BeNull();
    }

    [Fact]
    public void The_body_is_the_payload_as_utf8_and_nothing_else()
    {
        var body = _mapper.ToBody(Message() with { Payload = "{\"orderNumber\":\"Ä-1\"}" });

        Encoding.UTF8.GetString(body.Span).Should().Be("{\"orderNumber\":\"Ä-1\"}");
    }

    [Fact]
    public void An_envelope_survives_the_round_trip_through_amqp_unchanged()
    {
        var message = Message();

        var delivery = Publish(message);
        var read = _mapper.ToMessage(delivery);

        read.Should().Be(message);
    }

    [Fact]
    public void The_sub_second_precision_of_the_instant_survives_although_the_amqp_timestamp_cannot_carry_it()
    {
        var message = Message();

        var read = _mapper.ToMessage(Publish(message));

        read.OccurredAt.Should().Be(_occurredAt);
        DateTimeOffset.FromUnixTimeSeconds(_occurredAt.ToUnixTimeSeconds()).Should().NotBe(_occurredAt);
    }

    [Fact]
    public void A_header_written_as_text_is_read_back_from_the_bytes_the_client_delivers()
    {
        var delivery = Delivery(new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = "orders.placed.v1",
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [RabbitMQMessageMapper.OccurredAtHeader] =
                    Encoding.UTF8.GetBytes(_occurredAt.ToString("O", CultureInfo.InvariantCulture)),
                [RabbitMQMessageMapper.MessageVersionHeader] = Encoding.UTF8.GetBytes("7"),
            },
        });

        var read = _mapper.ToMessage(delivery);

        read.OccurredAt.Should().Be(_occurredAt);
        read.MessageVersion.Should().Be(7);
    }

    [Fact]
    public void A_message_version_sent_as_text_is_read()
    {
        var delivery = Delivery(new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = "orders.placed.v1",
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [RabbitMQMessageMapper.MessageVersionHeader] = "3",
            },
        });

        _mapper.ToMessage(delivery).MessageVersion.Should().Be(3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-number")]
    public void A_missing_or_unreadable_message_version_reads_as_version_one(string? headerValue)
    {
        var headers = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (headerValue is not null)
            headers[RabbitMQMessageMapper.MessageVersionHeader] = headerValue;

        var delivery = Delivery(new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = "orders.placed.v1",
            Headers = headers,
        });

        _mapper.ToMessage(delivery).MessageVersion.Should().Be(IntegrationEventMessage.CurrentVersion);
    }

    [Fact]
    public void A_delivery_without_any_headers_still_reads()
    {
        var delivery = Delivery(new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = "orders.placed.v1",
            Timestamp = new AmqpTimestamp(_occurredAt.ToUnixTimeSeconds()),
        });

        var read = _mapper.ToMessage(delivery);

        read.MessageVersion.Should().Be(IntegrationEventMessage.CurrentVersion);
        read.OccurredAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(_occurredAt.ToUnixTimeSeconds()));
    }

    [Fact]
    public void An_unparsable_occurred_at_header_falls_back_to_the_amqp_timestamp()
    {
        var delivery = Delivery(new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = "orders.placed.v1",
            Timestamp = new AmqpTimestamp(_occurredAt.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [RabbitMQMessageMapper.OccurredAtHeader] = "yesterday",
            },
        });

        _mapper.ToMessage(delivery).OccurredAt
            .Should().Be(DateTimeOffset.FromUnixTimeSeconds(_occurredAt.ToUnixTimeSeconds()));
    }

    [Fact]
    public void A_header_of_an_unexpected_type_is_ignored_rather_than_failing_the_message()
    {
        var delivery = Delivery(new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = "orders.placed.v1",
            Timestamp = new AmqpTimestamp(_occurredAt.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [RabbitMQMessageMapper.OccurredAtHeader] = 42,
            },
        });

        _mapper.ToMessage(delivery).OccurredAt
            .Should().Be(DateTimeOffset.FromUnixTimeSeconds(_occurredAt.ToUnixTimeSeconds()));
    }

    [Fact]
    public void The_routing_key_supplies_the_event_name_when_a_foreign_producer_set_no_type()
    {
        var delivery = Delivery(
            new BasicProperties { MessageId = Guid.NewGuid().ToString() }, routingKey: "orders.placed.v1");

        _mapper.ToMessage(delivery).EventName.Should().Be("orders.placed.v1");
    }

    [Fact]
    public void An_empty_correlation_id_on_the_wire_reads_back_as_none()
    {
        var delivery = Delivery(new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = "orders.placed.v1",
            CorrelationId = string.Empty,
        });

        _mapper.ToMessage(delivery).CorrelationId.Should().BeNull();
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("APPLICATION/JSON")]
    [InlineData(null)]
    [InlineData("")]
    public void A_delivery_written_in_the_encoding_this_application_reads_is_accepted(string? contentType)
    {
        // The parameters after the media type describe the same encoding, and a delivery that states
        // nothing came from a producer that labels nothing rather than one that labels something else.
        var delivery = Delivery(new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = "orders.placed.v1",
            ContentType = contentType,
        });

        var read = () => _mapper.ToMessage(delivery);

        read.Should().NotThrow();
    }

    [Theory]
    [InlineData("application/xml")]
    [InlineData("text/plain")]
    [InlineData("application/x-protobuf")]
    public void A_delivery_written_in_an_encoding_this_application_cannot_read_is_refused(string contentType)
    {
        // The body goes to a serializer that reads exactly one encoding. Refusing it here parks it with
        // the reason plainly stated rather than letting it fail later as an undecodable payload.
        var delivery = Delivery(new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = "orders.placed.v1",
            ContentType = contentType,
        });

        var read = () => _mapper.ToMessage(delivery);

        read.Should().Throw<RabbitMQMessageFormatException>()
            .WithMessage($"*states content type '{contentType}'*application/json*");
    }

    [Fact]
    public void A_custom_content_type_is_the_one_both_directions_are_measured_against()
    {
        var options = TestOptions.Valid();
        options.ContentType = "application/x-custom";
        var mapper = new RabbitMQMessageMapper(new RabbitMQConnectionSettings(options));

        mapper.ToProperties(Message()).ContentType.Should().Be("application/x-custom");

        var readCustom = () => mapper.ToMessage(Delivery(new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = "orders.placed.v1",
            ContentType = "application/x-custom",
        }));
        var readJson = () => mapper.ToMessage(Delivery(new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = "orders.placed.v1",
            ContentType = "application/json",
        }));

        readCustom.Should().NotThrow();
        readJson.Should().Throw<RabbitMQMessageFormatException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public void A_delivery_without_a_usable_message_id_is_refused(string? messageId)
    {
        var delivery = Delivery(new BasicProperties { MessageId = messageId, Type = "orders.placed.v1" });

        var read = () => _mapper.ToMessage(delivery);

        read.Should().Throw<RabbitMQMessageFormatException>().WithMessage("*no usable message-id*");
    }

    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    [InlineData(253402300800L)]
    public void A_timestamp_no_date_can_represent_is_refused_rather_than_thrown_out_of_the_mapper(long unixTime)
    {
        // The timestamp is a number a remote producer picks freely, so an out-of-range value has to
        // come back as a refusal the consumer dead-letters, not as an exception that escapes the
        // malformed-message handling and leaves the delivery unsettled.
        var delivery = Delivery(new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = "orders.placed.v1",
            Timestamp = new AmqpTimestamp(unixTime),
        });

        var read = () => _mapper.ToMessage(delivery);

        read.Should().Throw<RabbitMQMessageFormatException>().WithMessage("*outside the range*");
    }

    [Fact]
    public void A_hostile_timestamp_is_still_ignored_when_the_full_precision_header_is_readable()
    {
        var delivery = Delivery(new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = "orders.placed.v1",
            Timestamp = new AmqpTimestamp(long.MaxValue),
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [RabbitMQMessageMapper.OccurredAtHeader] = _occurredAt.ToString("O", CultureInfo.InvariantCulture),
            },
        });

        _mapper.ToMessage(delivery).OccurredAt.Should().Be(_occurredAt);
    }

    [Fact]
    public void A_delivery_with_neither_a_type_nor_a_routing_key_is_refused()
    {
        var delivery = Delivery(
            new BasicProperties { MessageId = Guid.NewGuid().ToString() }, routingKey: string.Empty);

        var read = () => _mapper.ToMessage(delivery);

        read.Should().Throw<RabbitMQMessageFormatException>().WithMessage("*no event name*");
    }

    [Fact]
    public void A_null_argument_is_rejected_by_the_consuming_side_too()
    {
        var readAttempt = () => _mapper.ReadDeliveryAttempt(null!);
        var parkWithoutDelivery = () => _mapper.ToParkProperties(null!, "why");
        var parkWithoutReason = () => _mapper.ToParkProperties(Delivery(new BasicProperties()), null!);

        readAttempt.Should().Throw<ArgumentNullException>();
        parkWithoutDelivery.Should().Throw<ArgumentNullException>();
        parkWithoutReason.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_delivery_without_a_redelivery_count_is_the_first_attempt()
    {
        // Which is what a classic queue looks like every single time, and why the queue type matters.
        _mapper.ReadDeliveryAttempt(Delivery(new BasicProperties())).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(DeliveryCounts))]
    public void The_attempt_is_one_more_than_the_deliveries_that_came_before_it(object? header, int expected)
    {
        var properties = new BasicProperties
        {
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [RabbitMQMessageMapper.DeliveryCountHeader] = header,
            },
        };

        _mapper.ReadDeliveryAttempt(Delivery(properties)).Should().Be(expected);
    }

    [Fact]
    public void A_redelivery_count_at_the_edge_of_the_range_does_not_wrap_into_a_negative_attempt()
    {
        var properties = new BasicProperties
        {
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [RabbitMQMessageMapper.DeliveryCountHeader] = long.MaxValue,
            },
        };

        _mapper.ReadDeliveryAttempt(Delivery(properties)).Should().Be(int.MaxValue);
    }

    [Fact]
    public void The_park_reason_travels_as_a_header_on_the_copy()
    {
        var properties = _mapper.ToParkProperties(
            Delivery(new BasicProperties { MessageId = "m-1", Type = "orders.placed.v1" }), "AttemptsExhausted: boom");

        properties.Headers.Should().ContainKey(RabbitMQMessageMapper.ParkReasonHeader)
            .WhoseValue.Should().Be("AttemptsExhausted: boom");
        properties.MessageId.Should().Be("m-1");
        properties.Type.Should().Be("orders.placed.v1");
        properties.Persistent.Should().BeTrue();
    }

    [Fact]
    public void The_headers_the_producer_sent_survive_the_parking()
    {
        var source = new BasicProperties
        {
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [RabbitMQMessageMapper.MessageVersionHeader] = 1,
            },
        };

        var properties = _mapper.ToParkProperties(Delivery(source), "why");

        properties.Headers.Should().ContainKey(RabbitMQMessageMapper.MessageVersionHeader);
        properties.Headers.Should().ContainKey(RabbitMQMessageMapper.ParkReasonHeader);
    }

    [Fact]
    public void A_message_that_was_parked_before_gets_the_new_reason_rather_than_two_of_them()
    {
        var source = new BasicProperties
        {
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [RabbitMQMessageMapper.ParkReasonHeader] = "an older reason",
            },
        };

        var properties = _mapper.ToParkProperties(Delivery(source), "the current reason");

        properties.Headers.Should().ContainKey(RabbitMQMessageMapper.ParkReasonHeader)
            .WhoseValue.Should().Be("the current reason");
    }

    [Fact]
    public void A_delivery_that_states_no_content_type_is_parked_under_this_applications_one()
    {
        var properties = _mapper.ToParkProperties(Delivery(new BasicProperties()), "why");

        properties.ContentType.Should().Be("application/json");
        properties.ContentEncoding.Should().Be("utf-8");
        properties.AppId.Should().Be("orders");
    }

    [Fact]
    public void A_delivery_that_states_its_own_encoding_keeps_it()
    {
        var source = new BasicProperties { ContentType = "application/xml", ContentEncoding = "utf-16" };

        var properties = _mapper.ToParkProperties(Delivery(source), "why");

        properties.ContentType.Should().Be("application/xml");
        properties.ContentEncoding.Should().Be("utf-16");
    }

    [Fact]
    public void The_park_reason_is_cut_at_the_same_width_the_outbox_records_a_failure_at()
    {
        // The two used to be separate literals that merely agreed. Tuning one of them alone would cut
        // the same exception message at two different points depending on which way the message failed,
        // which is not a symptom anybody traces back to a constant.
        RabbitMQMessageMapper.MaxParkReasonLength.Should().Be(MessageReasonLimits.MaxReasonLength);
    }

    [Fact]
    public void An_unbounded_reason_is_cut_to_what_a_header_may_carry()
    {
        var reason = new string('x', RabbitMQMessageMapper.MaxParkReasonLength + 500);

        var properties = _mapper.ToParkProperties(Delivery(new BasicProperties()), reason);

        properties.Headers![RabbitMQMessageMapper.ParkReasonHeader]
            .Should().Be(reason[..RabbitMQMessageMapper.MaxParkReasonLength]);
    }

    [Fact]
    public void The_cut_never_splits_a_surrogate_pair()
    {
        // An unpaired surrogate is what an encoder rejects, and the publish that preserves a parked
        // message is the wrong place to find that out.
        var reason = new string('x', RabbitMQMessageMapper.MaxParkReasonLength - 1) + "\U0001F600" + "tail";

        var properties = _mapper.ToParkProperties(Delivery(new BasicProperties()), reason);

        var recorded = properties.Headers![RabbitMQMessageMapper.ParkReasonHeader].Should().BeOfType<string>().Subject;
        recorded.Should().HaveLength(RabbitMQMessageMapper.MaxParkReasonLength - 1);
        char.IsSurrogate(recorded[^1]).Should().BeFalse();
    }

    public static TheoryData<object?, int> DeliveryCounts() =>
        new()
        {
            { 2L, 3 },
            { 2, 3 },
            { Encoding.UTF8.GetBytes("2"), 3 },
            { "2", 3 },
            { "not a number", 1 },
            { -4L, 1 },
        };

    /// <summary>Runs an envelope through the mapper and back into the delivery the broker would produce.</summary>
    private RabbitMQDelivery Publish(IntegrationEventMessage message)
    {
        var properties = _mapper.ToProperties(message);

        // The client hands string headers back as the bytes they were written as, so the round trip has
        // to go through that shape rather than through the dictionary as it was built.
        var wireHeaders = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in properties.Headers!)
            wireHeaders[key] = value is string text ? Encoding.UTF8.GetBytes(text) : value;

        properties.Headers = wireHeaders;

        return new RabbitMQDelivery
        {
            DeliveryTag = 1,
            RoutingKey = message.EventName,
            Properties = properties,
            Body = _mapper.ToBody(message),
        };
    }

    private static RabbitMQDelivery Delivery(BasicProperties properties, string routingKey = "orders.placed.v1") =>
        new()
        {
            DeliveryTag = 1,
            RoutingKey = routingKey,
            Properties = properties,
            Body = Encoding.UTF8.GetBytes("{}"),
        };

    private static IntegrationEventMessage Message() => new()
    {
        EventId = Guid.NewGuid(),
        EventName = "orders.placed.v1",
        OccurredAt = _occurredAt,
        CorrelationId = "correlation-1",
        Payload = "{\"orderNumber\":\"A-1\"}",
    };
}
