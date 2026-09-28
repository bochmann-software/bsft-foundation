using Bsft.Outbox.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Outbox.EFCore.Tests;

/// <summary>
/// Tests the lease protocol against a second outbox that is not a message queue at all: the intent to
/// provision a tenant, committed with the tenant and carried out afterwards. It is the same protocol
/// the integration-event outbox runs on, which is the whole claim this package makes.
/// </summary>
public sealed class EFCoreOutboxStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 3, 4, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);

    private readonly ProvisioningFixture _outbox = new();
    private readonly IOutboxStore<string> _store;

    public EFCoreOutboxStoreTests() => _store = Store(_outbox);

    public void Dispose() => _outbox.Dispose();

    [Fact]
    public async Task A_committed_intent_is_leased_with_the_payload_its_row_carries()
    {
        var id = await _outbox.GivenIntentAsync("acme");

        var leased = await _store.LeaseAsync(Now, Lease, 10);

        leased.Should().ContainSingle();
        leased[0].Id.Should().Be(id);
        leased[0].Payload.Should().Be("acme");
        leased[0].AttemptCount.Should().Be(0);
        leased[0].LeaseToken.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Rows_are_leased_in_the_order_they_were_committed()
    {
        await _outbox.GivenIntentAsync("first");
        await _outbox.GivenIntentAsync("second");

        var leased = await _store.LeaseAsync(Now, Lease, 10);

        leased.Select(r => r.Payload).Should().Equal("first", "second");
    }

    [Fact]
    public async Task A_lease_claims_no_more_than_the_batch_it_was_asked_for()
    {
        await _outbox.GivenIntentAsync("first");
        await _outbox.GivenIntentAsync("second");

        (await _store.LeaseAsync(Now, Lease, 1)).Should().ContainSingle();
    }

    [Fact]
    public async Task Two_drainers_racing_for_the_same_intent_do_not_both_get_it()
    {
        await _outbox.GivenIntentAsync("acme");
        var other = Store(_outbox);

        var first = await _store.LeaseAsync(Now, Lease, 10);
        var second = await other.LeaseAsync(Now, Lease, 10);

        first.Should().ContainSingle();
        second.Should().BeEmpty();
    }

    [Fact]
    public async Task A_lease_that_has_run_out_lets_the_intent_be_claimed_again()
    {
        await _outbox.GivenIntentAsync("acme");
        await _store.LeaseAsync(Now, Lease, 10);

        (await _store.LeaseAsync(Now + Lease, Lease, 10)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_carried_out_intent_is_marked_and_never_leased_again()
    {
        var id = await _outbox.GivenIntentAsync("acme");
        var leased = await _store.LeaseAsync(Now, Lease, 10);

        var marked = await _store.TryMarkProcessedAsync(id, leased[0].LeaseToken, Now.AddSeconds(3));

        marked.Should().BeTrue();
        var row = await _outbox.ReadRowAsync(id);
        row.ProcessedAt.Should().Be(Now.AddSeconds(3).UtcDateTime);
        row.LeasedUntil.Should().BeNull();
        (await _store.LeaseAsync(Now.AddDays(1), Lease, 10)).Should().BeEmpty();
    }

    /// <summary>
    /// Only the current lease holder may complete a row. It is what keeps a drainer whose lease ran out
    /// mid-effect from marking a row the next holder is already working on.
    /// </summary>
    [Fact]
    public async Task A_stale_lease_cannot_mark_the_intent()
    {
        var id = await _outbox.GivenIntentAsync("acme");
        await _store.LeaseAsync(Now, Lease, 10);

        (await _store.TryMarkProcessedAsync(id, Guid.NewGuid(), Now)).Should().BeFalse();
        (await _outbox.ReadRowAsync(id)).ProcessedAt.Should().BeNull();
    }

    [Fact]
    public async Task An_abandoned_intent_waits_out_its_retry_instant_and_counts_the_attempt()
    {
        var id = await _outbox.GivenIntentAsync("acme");
        var leased = await _store.LeaseAsync(Now, Lease, 10);

        await _store.AbandonAsync(id, leased[0].LeaseToken, Now.AddMinutes(5));

        var row = await _outbox.ReadRowAsync(id);
        row.AttemptCount.Should().Be(1);
        row.NextAttemptAt.Should().Be(Now.AddMinutes(5).UtcDateTime);
        row.LeasedUntil.Should().BeNull();
        (await _store.LeaseAsync(Now.AddMinutes(1), Lease, 10)).Should().BeEmpty();
        (await _store.LeaseAsync(Now.AddMinutes(5), Lease, 10)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_retired_intent_keeps_its_reason_and_is_never_leased_again()
    {
        var id = await _outbox.GivenIntentAsync("acme");
        var leased = await _store.LeaseAsync(Now, Lease, 10);

        await _store.DeadLetterAsync(id, leased[0].LeaseToken, Now.AddSeconds(5), "the tenant no longer exists");

        var row = await _outbox.ReadRowAsync(id);
        row.DeadLetteredAt.Should().Be(Now.AddSeconds(5).UtcDateTime);
        row.FailureReason.Should().Be("the tenant no longer exists");
        row.ProcessedAt.Should().BeNull("the row is kept for inspection rather than silently dropped");
        (await _store.LeaseAsync(Now.AddDays(1), Lease, 10)).Should().BeEmpty();
    }

    /// <summary>
    /// The reason comes from an exception message, which nothing bounds. Failing the write that records
    /// why a row was retired would leave the row in the retry loop it was supposed to leave.
    /// </summary>
    [Fact]
    public async Task A_reason_wider_than_its_column_is_cut_to_fit()
    {
        var id = await _outbox.GivenIntentAsync("acme");
        var leased = await _store.LeaseAsync(Now, Lease, 10);

        await _store.DeadLetterAsync(id, leased[0].LeaseToken, Now, new string('x', 200));

        (await _outbox.ReadRowAsync(id)).FailureReason.Should().HaveLength(ProvisioningIntentRowSource.ReasonMaxLength);
    }

    /// <summary>
    /// A cut through a surrogate pair leaves an unpaired surrogate, which a provider that validates its
    /// text encoding rejects — and this is the write that ends the row's retry loop.
    /// </summary>
    [Fact]
    public async Task A_reason_cut_on_a_surrogate_pair_stays_valid_text()
    {
        var id = await _outbox.GivenIntentAsync("acme");
        var leased = await _store.LeaseAsync(Now, Lease, 10);
        var reason = new string('x', ProvisioningIntentRowSource.ReasonMaxLength - 1) + "\uD83D\uDE00";

        await _store.DeadLetterAsync(id, leased[0].LeaseToken, Now, reason);

        var recorded = (await _outbox.ReadRowAsync(id)).FailureReason;
        recorded.Should().HaveLength(ProvisioningIntentRowSource.ReasonMaxLength - 1);
        recorded.Should().NotEndWith("\uD83D", "an unpaired surrogate is not valid text");
    }

    [Fact]
    public async Task A_retirement_without_a_reason_is_rejected()
    {
        var id = await _outbox.GivenIntentAsync("acme");
        var leased = await _store.LeaseAsync(Now, Lease, 10);

        var act = async () => await _store.DeadLetterAsync(id, leased[0].LeaseToken, Now, null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("reason");
    }

    [Fact]
    public void A_store_without_its_dependencies_is_rejected()
    {
        var source = new ProvisioningIntentRowSource();
        var withoutFactory = () => new EFCoreOutboxStore<ProvisioningIntentRow, string>(null!, source);
        var withoutRows = () => new EFCoreOutboxStore<ProvisioningIntentRow, string>(
            _outbox.Scopes, null!);

        withoutFactory.Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");
        withoutRows.Should().Throw<ArgumentNullException>().WithParameterName("rows");
    }

    [Fact]
    public void A_row_source_rejects_a_missing_argument()
    {
        var source = new ProvisioningIntentRowSource();
        var withoutServices = () => source.Rows(null!);
        var withoutRow = () => source.PayloadOf(null!);

        withoutServices.Should().Throw<ArgumentNullException>().WithParameterName("scopedServices");
        withoutRow.Should().Throw<ArgumentNullException>().WithParameterName("row");
    }

    private static IOutboxStore<string> Store(ProvisioningFixture outbox) =>
        new EFCoreOutboxStore<ProvisioningIntentRow, string>(outbox.Scopes, new ProvisioningIntentRowSource());

}
