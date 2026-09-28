using FluentAssertions;
using Xunit;

namespace Bsft.Messaging.Abstractions.Tests;

public class IntegrationEventMessageTests
{
    private static IntegrationEventMessage CreateMessage() => new()
    {
        EventId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        EventName = "orders.placed.v1",
        OccurredAt = new DateTimeOffset(2026, 6, 22, 6, 0, 0, TimeSpan.Zero),
        CorrelationId = "request-1",
        Payload = """{"value":1}""",
    };

    [Fact]
    public void Properties_carry_what_goes_on_the_wire()
    {
        var message = CreateMessage();

        message.EventId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        message.EventName.Should().Be("orders.placed.v1");
        message.OccurredAt.Should().Be(new DateTimeOffset(2026, 6, 22, 6, 0, 0, TimeSpan.Zero));
        message.CorrelationId.Should().Be("request-1");
        message.Payload.Should().Be("""{"value":1}""");
    }

    [Fact]
    public void A_new_message_is_stamped_with_the_current_contract_version()
    {
        // Producing an message that claims a version nobody wrote would be worse than useless to a
        // consumer deciding whether it can read the message, so the default is the current one.
        new IntegrationEventMessage().MessageVersion.Should().Be(IntegrationEventMessage.CurrentVersion);
        IntegrationEventMessage.CurrentVersion.Should().Be(1);
    }

    [Fact]
    public void A_message_read_off_the_wire_keeps_the_version_its_producer_wrote()
    {
        (CreateMessage() with { MessageVersion = 2 }).MessageVersion.Should().Be(2);
    }

    [Fact]
    public void Defaults_are_empty_rather_than_null()
    {
        var message = new IntegrationEventMessage();

        message.EventName.Should().BeEmpty();
        message.Payload.Should().BeEmpty();
        message.CorrelationId.Should().BeNull("a message staged outside a correlated request has none");
    }

    [Fact]
    public void Two_messages_with_the_same_content_are_equal()
    {
        var message = CreateMessage();
        var same = CreateMessage();

        same.Should().Be(message);
        same.GetHashCode().Should().Be(message.GetHashCode());
        same.ToString().Should().Be(message.ToString());
    }

    [Fact]
    public void A_copy_with_another_correlation_id_is_a_different_message()
    {
        var message = CreateMessage();

        var recorrelated = message with { CorrelationId = "request-2" };

        recorrelated.Should().NotBe(message);
        recorrelated.EventId.Should().Be(message.EventId);
    }
}
