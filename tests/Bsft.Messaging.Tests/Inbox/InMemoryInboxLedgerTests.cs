using Bsft.Messaging.Inbox;
using Bsft.Messaging.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Messaging.Tests.Inbox;

public class InMemoryInboxLedgerTests
{
    // Small enough to fill in a test, which is the point of the capacity being an option: the behaviour
    // under eviction is the same at eight records as at a hundred thousand.
    private const int Capacity = 8;

    private readonly InMemoryInboxLedger _ledger =
        new(TestMessagingOptions.For(new ConsumerOptions { InboxLedgerCapacity = Capacity }));

    [Fact]
    public void An_unknown_event_is_not_contained()
    {
        _ledger.Contains(Guid.NewGuid(), "orders").Should().BeFalse();
        _ledger.Count.Should().Be(0);
    }

    [Fact]
    public void A_recorded_event_is_contained_for_its_consumer_only()
    {
        var eventId = Guid.NewGuid();

        _ledger.Add(eventId, "orders");

        _ledger.Contains(eventId, "orders").Should().BeTrue();
        _ledger.Contains(eventId, "invoicing").Should().BeFalse();
        _ledger.Count.Should().Be(1);
    }

    [Fact]
    public void Recording_the_same_delivery_twice_keeps_one_record()
    {
        var eventId = Guid.NewGuid();

        _ledger.Add(eventId, "orders");
        _ledger.Add(eventId, "orders");

        _ledger.Count.Should().Be(1);
    }

    [Fact]
    public void A_null_consumer_is_rejected()
    {
        var whenAdding = () => _ledger.Add(Guid.NewGuid(), null!);
        var whenReading = () => _ledger.Contains(Guid.NewGuid(), null!);

        whenAdding.Should().Throw<ArgumentNullException>().WithParameterName("consumer");
        whenReading.Should().Throw<ArgumentNullException>().WithParameterName("consumer");
    }

    [Fact]
    public void The_ledger_stops_growing_at_its_capacity()
    {
        // It is process-wide and lives as long as the process, so an unbounded one is a leak that grows
        // with throughput rather than with anything going wrong.
        Fill(_ledger, Capacity + 100);

        _ledger.Count.Should().Be(Capacity);
    }

    [Fact]
    public void The_oldest_record_is_the_one_dropped_when_the_capacity_is_reached()
    {
        // Age order is what makes the bound safe: the duplicate-detection window is the most recent
        // Capacity deliveries, and dropping a recent record would reopen it for an event still being
        // redelivered.
        var oldest = Guid.NewGuid();
        var newest = Guid.NewGuid();
        _ledger.Add(oldest, "orders");
        Fill(_ledger, Capacity - 1);
        _ledger.Contains(oldest, "orders").Should().BeTrue();

        _ledger.Add(newest, "orders");

        _ledger.Contains(oldest, "orders").Should().BeFalse();
        _ledger.Contains(newest, "orders").Should().BeTrue();
        _ledger.Count.Should().Be(Capacity);
    }

    [Fact]
    public void Recording_a_delivery_again_does_not_cost_a_second_place_in_the_ledger()
    {
        // Otherwise a redelivery that is refused would still push the ledger over its capacity and
        // evict a record that is doing its job.
        var eventId = Guid.NewGuid();
        _ledger.Add(eventId, "orders");
        Fill(_ledger, Capacity - 1);

        _ledger.Add(eventId, "orders");

        _ledger.Count.Should().Be(Capacity);
        _ledger.Contains(eventId, "orders").Should().BeTrue();
    }

    [Fact]
    public void A_configured_capacity_is_the_one_the_ledger_holds_to()
    {
        // The option is what a deployment trades memory against its duplicate-detection window with, so
        // a ledger built from a different number has to bound itself at that number and not the default.
        var narrow = new InMemoryInboxLedger(TestMessagingOptions.For(new ConsumerOptions { InboxLedgerCapacity = 3 }));

        Fill(narrow, 20);

        narrow.Count.Should().Be(3);
    }

    [Fact]
    public void The_capacity_defaults_to_a_hundred_thousand_records()
    {
        new ConsumerOptions().InboxLedgerCapacity.Should().Be(100_000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_capacity_below_one_is_rejected(int capacity)
    {
        // It would evict every record as it is written, leaving a duplicate guard that recognises no
        // redelivery at all — a failure that is invisible until an event is handled twice.
        var act = () => new InMemoryInboxLedger(TestMessagingOptions.For(new ConsumerOptions { InboxLedgerCapacity = capacity }));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("options");
    }

    [Fact]
    public void Null_options_are_rejected()
    {
        var act = () => new InMemoryInboxLedger(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    private static void Fill(InMemoryInboxLedger ledger, int records)
    {
        for (var record = 0; record < records; record++)
            ledger.Add(Guid.NewGuid(), "orders");
    }
}
