using Bsft.Messaging.Outbox;
using FluentAssertions;
using Xunit;

namespace Bsft.Messaging.Abstractions.Tests.Outbox;

public class LeasedOutboxMessageTests
{
    private static LeasedOutboxMessage CreateMessage() => new()
    {
        Id = 7,
        EventId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        EventTypeName = "Orders.OrderPlaced",
        PayloadJson = """{"value":1}""",
        OccurredAt = new DateTimeOffset(2026, 6, 22, 6, 0, 0, TimeSpan.Zero),
        CorrelationId = "request-1",
        AttemptCount = 2,
        LeaseToken = Guid.Parse("22222222-2222-2222-2222-222222222222"),
    };

    [Fact]
    public void Properties_carry_the_message_the_dispatcher_has_to_publish()
    {
        var message = CreateMessage();

        message.Id.Should().Be(7);
        message.EventId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        message.EventTypeName.Should().Be("Orders.OrderPlaced");
        message.PayloadJson.Should().Be("""{"value":1}""");
        message.OccurredAt.Should().Be(new DateTimeOffset(2026, 6, 22, 6, 0, 0, TimeSpan.Zero));
        message.CorrelationId.Should().Be("request-1");
        message.AttemptCount.Should().Be(2);
        message.LeaseToken.Should().Be(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    }

    [Fact]
    public void Defaults_are_empty_rather_than_null()
    {
        var message = new LeasedOutboxMessage();

        message.EventTypeName.Should().BeEmpty();
        message.PayloadJson.Should().BeEmpty();

        // Not empty though: a row that carries no correlation id, and one written before the column
        // existed, are the same thing and both are null.
        message.CorrelationId.Should().BeNull();
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
    public void A_copy_with_another_lease_token_is_a_different_message()
    {
        var message = CreateMessage();

        var reclaimed = message with { LeaseToken = Guid.NewGuid() };

        reclaimed.Should().NotBe(message);
        reclaimed.Id.Should().Be(message.Id);
    }
}
