using Bsft.Messaging.Inbox;
using Bsft.Messaging.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Messaging.Tests.Inbox;

public class InMemoryInboxStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);

    private readonly InMemoryInboxLedger _ledger = new(TestMessagingOptions.For(new ConsumerOptions()));

    private InMemoryInboxStore CreateStore() => new(_ledger);

    private static IntegrationEventMessage Message(Guid? eventId = null) => new()
    {
        EventId = eventId ?? Guid.NewGuid(),
        EventName = "orders.placed.v1",
        OccurredAt = Now,
        Payload = "{}",
    };

    [Fact]
    public async Task An_event_nobody_has_processed_is_claimed()
    {
        var claimed = await CreateStore().TryBeginAsync(Message(), "orders", Now);

        claimed.Should().BeTrue();
    }

    [Fact]
    public async Task A_committed_event_is_refused_the_next_time_it_arrives()
    {
        var message = Message();
        var first = CreateStore();
        await first.TryBeginAsync(message, "orders", Now);
        await first.CommitAsync();

        var redelivery = await CreateStore().TryBeginAsync(message, "orders", Now);

        redelivery.Should().BeFalse();
    }

    [Fact]
    public async Task A_claim_that_was_abandoned_leaves_the_event_claimable_again()
    {
        // The failure case: nothing was recorded, so the redelivery is handled rather than swallowed.
        var message = Message();
        var failed = CreateStore();
        await failed.TryBeginAsync(message, "orders", Now);
        await failed.AbandonAsync();

        var redelivery = await CreateStore().TryBeginAsync(message, "orders", Now);

        redelivery.Should().BeTrue();
        _ledger.Count.Should().Be(0);
    }

    [Fact]
    public async Task Two_consumers_of_the_same_event_are_independent_of_each_other()
    {
        var message = Message();
        var orders = CreateStore();
        await orders.TryBeginAsync(message, "orders", Now);
        await orders.CommitAsync();

        var invoicing = await CreateStore().TryBeginAsync(message, "invoicing", Now);

        invoicing.Should().BeTrue();
    }

    [Fact]
    public async Task Committing_twice_is_refused_rather_than_recording_a_claim_that_is_not_held()
    {
        var store = CreateStore();
        await store.TryBeginAsync(Message(), "orders", Now);
        await store.CommitAsync();

        var act = async () => await store.CommitAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Committing_without_a_claim_is_refused()
    {
        var act = async () => await CreateStore().CommitAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Abandoning_without_a_claim_is_tolerated()
    {
        // The consumer releases on any failure, including one raised before the claim was taken.
        var act = async () => await CreateStore().AbandonAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Null_arguments_are_rejected()
    {
        var store = CreateStore();

        var withoutMessage = async () => await store.TryBeginAsync(null!, "orders", Now);
        var withoutConsumer = async () => await store.TryBeginAsync(Message(), null!, Now);

        await withoutMessage.Should().ThrowAsync<ArgumentNullException>().WithParameterName("message");
        await withoutConsumer.Should().ThrowAsync<ArgumentNullException>().WithParameterName("consumer");
    }

    [Fact]
    public void A_null_ledger_is_rejected()
    {
        var act = () => new InMemoryInboxStore(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("ledger");
    }
}
