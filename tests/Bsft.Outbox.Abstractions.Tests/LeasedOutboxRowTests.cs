using FluentAssertions;
using Xunit;

namespace Bsft.Outbox.Abstractions.Tests;

/// <summary>Tests the shape a drainer sees a claimed row through.</summary>
public class LeasedOutboxRowTests
{
    [Fact]
    public void The_row_carries_the_claim_and_the_payload_it_was_created_with()
    {
        var token = Guid.NewGuid();

        var row = new LeasedOutboxRow<string>(7, "payload", 2, token);

        row.Id.Should().Be(7);
        row.Payload.Should().Be("payload");
        row.AttemptCount.Should().Be(2);
        row.LeaseToken.Should().Be(token);
    }

    /// <summary>
    /// Two rows of the same claim are the same row. It matters because a drainer that read the same
    /// batch twice must not be able to tell the two reads apart by identity alone.
    /// </summary>
    [Fact]
    public void Two_rows_of_the_same_claim_are_equal()
    {
        var token = Guid.NewGuid();

        new LeasedOutboxRow<string>(7, "payload", 2, token)
            .Should().Be(new LeasedOutboxRow<string>(7, "payload", 2, token));
    }
}
