using FluentAssertions;
using Xunit;

namespace Bsft.Messaging.Abstractions.Tests;

/// <summary>
/// The shapes an operator reads the outbox and the inbox through.
/// </summary>
public sealed class ReadingContractsTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A filter with nothing set narrows nothing, which is what a bare listing sends.</summary>
    [Fact]
    public void OutboxMessageFilter_WithoutCriteria_IsEmpty()
    {
        var filter = new OutboxMessageFilter();

        filter.State.Should().BeNull();
        filter.EventTypeName.Should().BeNull();
        filter.EventTypeNamePrefix.Should().BeNull();
        filter.EventId.Should().BeNull();
        filter.CorrelationId.Should().BeNull();
        filter.OccurredAfter.Should().BeNull();
        filter.OccurredBefore.Should().BeNull();
        filter.MinAttemptCount.Should().BeNull();
    }

    /// <summary>Every criterion is carried as it was given.</summary>
    [Fact]
    public void OutboxMessageFilter_WithCriteria_CarriesThem()
    {
        var eventId = Guid.NewGuid();

        var filter = new OutboxMessageFilter(
            OutboxMessageState.DeadLettered, "Orders.Placed", "Orders.", eventId, "corr-1", Now, Now.AddHours(1), 5);

        filter.Should().Be(new OutboxMessageFilter(
            OutboxMessageState.DeadLettered, "Orders.Placed", "Orders.", eventId, "corr-1", Now, Now.AddHours(1), 5));
        filter.MinAttemptCount.Should().Be(5);
    }

    /// <summary>A listing row carries the dispatch state and, deliberately, no payload.</summary>
    [Fact]
    public void OutboxMessageSummary_Always_CarriesTheDispatchState()
    {
        var eventId = Guid.NewGuid();

        var summary = new OutboxMessageSummary(
            7,
            eventId,
            "Orders.Placed",
            Now,
            "corr-1",
            OutboxMessageState.Pending,
            2,
            Now.AddMinutes(5),
            Now.AddMinutes(1),
            null,
            null,
            "timed out");

        summary.Id.Should().Be(7);
        summary.EventId.Should().Be(eventId);
        summary.EventTypeName.Should().Be("Orders.Placed");
        summary.OccurredAt.Should().Be(Now);
        summary.CorrelationId.Should().Be("corr-1");
        summary.State.Should().Be(OutboxMessageState.Pending);
        summary.AttemptCount.Should().Be(2);
        summary.NextAttemptAt.Should().Be(Now.AddMinutes(5));
        summary.LeasedUntil.Should().Be(Now.AddMinutes(1));
        summary.ProcessedAt.Should().BeNull();
        summary.DeadLetteredAt.Should().BeNull();
        summary.FailureReason.Should().Be("timed out");
    }

    /// <summary>The payload is what the second read adds to the first.</summary>
    [Fact]
    public void OutboxMessageView_Always_AddsThePayloadToTheSummary()
    {
        var summary = new OutboxMessageSummary(
            7, Guid.NewGuid(), "Orders.Placed", Now, null, OutboxMessageState.Dispatched, 1, null, null, Now, null, null);

        var view = new OutboxMessageView(summary, """{"orderId":7}""");

        view.Summary.Should().BeSameAs(summary);
        view.PayloadJson.Should().Be("""{"orderId":7}""");
    }

    /// <summary>A filter with nothing set narrows nothing.</summary>
    [Fact]
    public void InboxMessageFilter_WithoutCriteria_IsEmpty()
    {
        var filter = new InboxMessageFilter();

        filter.Consumer.Should().BeNull();
        filter.EventName.Should().BeNull();
        filter.EventId.Should().BeNull();
        filter.ReceivedAfter.Should().BeNull();
        filter.ReceivedBefore.Should().BeNull();
    }

    /// <summary>Every criterion is carried as it was given.</summary>
    [Fact]
    public void InboxMessageFilter_WithCriteria_CarriesThem()
    {
        var eventId = Guid.NewGuid();

        new InboxMessageFilter("orders", "Orders.Placed", eventId, Now, Now.AddHours(1))
            .Should().Be(new InboxMessageFilter("orders", "Orders.Placed", eventId, Now, Now.AddHours(1)));
    }

    /// <summary>A ledger row is the four columns the table has, and no payload, because it holds none.</summary>
    [Fact]
    public void InboxMessageSummary_Always_CarriesTheDelivery()
    {
        var eventId = Guid.NewGuid();

        var summary = new InboxMessageSummary(11, eventId, "orders", "Orders.Placed", Now);

        summary.Id.Should().Be(11);
        summary.EventId.Should().Be(eventId);
        summary.Consumer.Should().Be("orders");
        summary.EventName.Should().Be("Orders.Placed");
        summary.ReceivedAt.Should().Be(Now);
    }
}
