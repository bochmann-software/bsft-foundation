using Bsft.Core.Retry;
using Bsft.Messaging.Consuming;
using Bsft.Outbox;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Bsft.Messaging.Tests.Consuming;

/// <summary>
/// Tests this package's retry schedule, which is the shared one seen through the messaging contract.
/// </summary>
public class MessageRetryScheduleTests
{
    private readonly IOutboxRetrySchedule _shared = Substitute.For<IOutboxRetrySchedule>();

    [Fact]
    public void The_delay_is_the_one_the_shared_schedule_decided()
    {
        _shared.NextDelay(2, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(9))
            .Returns(TimeSpan.FromSeconds(30));

        var delay = new MessageRetrySchedule(_shared)
            .NextDelay(2, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(9));

        delay.Should().Be(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// A wait a broker asked for is read off the failure by the shared schedule, and this package's
    /// own <see cref="RetryAfterException"/> reaches it unchanged.
    /// </summary>
    [Fact]
    public void The_requested_wait_is_the_one_the_shared_schedule_read()
    {
        var failure = new RetryAfterException("throttled", TimeSpan.FromMinutes(2));
        _shared.RequestedRetryAfter(failure).Returns(TimeSpan.FromMinutes(2));

        new MessageRetrySchedule(_shared).RequestedRetryAfter(failure).Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void A_schedule_without_the_shared_one_is_rejected()
    {
        var act = () => new MessageRetrySchedule(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("schedule");
    }
}
