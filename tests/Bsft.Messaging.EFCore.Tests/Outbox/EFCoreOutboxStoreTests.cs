using Bsft.Messaging.EFCore.Outbox;
using Bsft.Messaging.EFCore.Tests.Fakes;
using Bsft.Messaging.EFCore.Tests.Support;
using Bsft.Messaging.Outbox;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Messaging.EFCore.Tests.Outbox;

public sealed class EFCoreOutboxStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly IOutboxStore _store;

    public EFCoreOutboxStoreTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<TestOutboxDbContext>(o => o.UseSqlite(_connection));
        services.AddScoped<IOutboxDbContext>(sp => sp.GetRequiredService<TestOutboxDbContext>());
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>().Database.EnsureCreated();

        _store = Store(_provider);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private async Task<long> GivenPendingMessageAsync(string typeName = "orders.placed.v1")
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        var row = new OutboxMessageEntity
        {
            EventId = Guid.NewGuid(),
            EventTypeName = typeName,
            PayloadJson = """{"orderNumber":"A-1"}""",
            OccurredAt = Now.UtcDateTime,
        };
        db.OutboxMessages.Add(row);
        await db.SaveChangesAsync();

        return row.Id;
    }

    /// <summary>
    /// This package's store over the shared lease protocol, which is how a host resolves it. The two
    /// are built together because the adapter has nothing of its own to test apart from what the
    /// protocol underneath decides.
    /// </summary>
    private static IOutboxStore Store(IServiceProvider services) =>
        new EFCoreOutboxStore(
            new global::Bsft.Outbox.EFCore.EFCoreOutboxStore<OutboxMessageEntity, IntegrationEventMessage>(
                services.GetRequiredService<IServiceScopeFactory>(), new OutboxMessageRowSource()));

    private async Task<OutboxMessageEntity> ReadRowAsync(long id)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();

        return await db.OutboxMessages.AsNoTracking().SingleAsync(e => e.Id == id);
    }

    [Fact]
    public async Task A_pending_message_is_claimed_with_its_payload_and_a_lease_token()
    {
        var id = await GivenPendingMessageAsync();

        var leased = await _store.LeaseAsync(Now, Lease, 10);

        var message = leased.Should().ContainSingle().Subject;
        message.Id.Should().Be(id);
        message.EventTypeName.Should().Be("orders.placed.v1");
        message.PayloadJson.Should().Be("""{"orderNumber":"A-1"}""");
        message.OccurredAt.Should().Be(Now);
        message.AttemptCount.Should().Be(0);
        message.LeaseToken.Should().NotBeEmpty();

        var row = await ReadRowAsync(id);
        row.LeaseToken.Should().Be(message.LeaseToken);
        row.LeasedUntil.Should().Be(Now.Add(Lease).UtcDateTime);
    }

    [Fact]
    public async Task A_message_under_a_live_lease_is_not_handed_out_again()
    {
        await GivenPendingMessageAsync();
        await _store.LeaseAsync(Now, Lease, 10);

        var second = await _store.LeaseAsync(Now.AddSeconds(30), Lease, 10);

        second.Should().BeEmpty();
    }

    [Fact]
    public async Task A_lease_that_has_run_out_is_taken_over_so_a_dead_instance_blocks_nothing()
    {
        var id = await GivenPendingMessageAsync();
        var first = await _store.LeaseAsync(Now, Lease, 10);

        var takeover = await _store.LeaseAsync(Now.Add(Lease).AddSeconds(1), Lease, 10);

        takeover.Should().ContainSingle().Which.Id.Should().Be(id);
        takeover[0].LeaseToken.Should().NotBe(first[0].LeaseToken);
    }

    [Fact]
    public async Task A_processed_message_is_never_leased_again()
    {
        var id = await GivenPendingMessageAsync();
        var leased = await _store.LeaseAsync(Now, Lease, 10);
        await _store.TryMarkProcessedAsync(id, leased[0].LeaseToken, Now);

        var second = await _store.LeaseAsync(Now.AddDays(1), Lease, 10);

        second.Should().BeEmpty();
    }

    [Fact]
    public async Task The_batch_size_limits_a_pass_and_the_oldest_messages_go_first()
    {
        var first = await GivenPendingMessageAsync();
        await GivenPendingMessageAsync();

        var leased = await _store.LeaseAsync(Now, Lease, 1);

        leased.Should().ContainSingle().Which.Id.Should().Be(first);
    }

    [Fact]
    public async Task An_empty_outbox_yields_no_lease()
    {
        var leased = await _store.LeaseAsync(Now, Lease, 10);

        leased.Should().BeEmpty();
    }

    [Fact]
    public async Task Marking_a_message_processed_stamps_it_and_releases_the_lease()
    {
        var id = await GivenPendingMessageAsync();
        var leased = await _store.LeaseAsync(Now, Lease, 10);

        var marked = await _store.TryMarkProcessedAsync(id, leased[0].LeaseToken, Now.AddSeconds(5));

        marked.Should().BeTrue();
        var row = await ReadRowAsync(id);
        row.ProcessedAt.Should().Be(Now.AddSeconds(5).UtcDateTime);
        row.LeasedUntil.Should().BeNull();
    }

    [Fact]
    public async Task A_stale_lease_holder_cannot_complete_a_message_that_belongs_to_someone_else()
    {
        var id = await GivenPendingMessageAsync();
        var first = await _store.LeaseAsync(Now, Lease, 10);
        await _store.LeaseAsync(Now.Add(Lease).AddSeconds(1), Lease, 10);

        var marked = await _store.TryMarkProcessedAsync(id, first[0].LeaseToken, Now.AddMinutes(2));

        marked.Should().BeFalse();
        (await ReadRowAsync(id)).ProcessedAt.Should().BeNull();
    }

    [Fact]
    public async Task Marking_a_message_twice_only_succeeds_once()
    {
        var id = await GivenPendingMessageAsync();
        var leased = await _store.LeaseAsync(Now, Lease, 10);
        await _store.TryMarkProcessedAsync(id, leased[0].LeaseToken, Now);

        var again = await _store.TryMarkProcessedAsync(id, leased[0].LeaseToken, Now);

        again.Should().BeFalse();
    }

    [Fact]
    public async Task Abandoning_a_message_counts_the_attempt_and_frees_it_for_the_next_pass()
    {
        var id = await GivenPendingMessageAsync();
        var leased = await _store.LeaseAsync(Now, Lease, 10);

        await _store.AbandonAsync(id, leased[0].LeaseToken, Now);

        var row = await ReadRowAsync(id);
        row.AttemptCount.Should().Be(1);
        row.LeasedUntil.Should().BeNull();

        var retry = await _store.LeaseAsync(Now, Lease, 10);
        retry.Should().ContainSingle().Which.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task Abandoning_records_when_the_message_may_be_leased_again()
    {
        var id = await GivenPendingMessageAsync();
        var leased = await _store.LeaseAsync(Now, Lease, 10);
        var notBefore = Now.AddMinutes(5);

        await _store.AbandonAsync(id, leased[0].LeaseToken, notBefore);

        (await ReadRowAsync(id)).NextAttemptAt.Should().Be(notBefore.UtcDateTime);
    }

    [Fact]
    public async Task A_message_that_is_held_back_is_not_leased_before_its_instant()
    {
        // The wait lives on the row rather than in the dispatcher that failed it, so it survives a
        // restart and every instance respects it.
        var id = await GivenPendingMessageAsync();
        var leased = await _store.LeaseAsync(Now, Lease, 10);
        await _store.AbandonAsync(id, leased[0].LeaseToken, Now.AddMinutes(5));

        var tooEarly = await _store.LeaseAsync(Now.AddMinutes(1), Lease, 10);
        var onTime = await _store.LeaseAsync(Now.AddMinutes(5), Lease, 10);

        tooEarly.Should().BeEmpty();
        onTime.Should().ContainSingle().Which.Id.Should().Be(id);
    }

    [Fact]
    public async Task A_message_that_has_never_failed_is_eligible_at_once()
    {
        // Which is also what a row written before the column existed reads back as.
        var id = await GivenPendingMessageAsync();

        var leased = await _store.LeaseAsync(Now, Lease, 10);

        leased.Should().ContainSingle().Which.Id.Should().Be(id);
        (await ReadRowAsync(id)).NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public async Task Abandoning_with_a_stale_token_changes_nothing()
    {
        var id = await GivenPendingMessageAsync();
        await _store.LeaseAsync(Now, Lease, 10);

        await _store.AbandonAsync(id, Guid.NewGuid(), Now);

        var row = await ReadRowAsync(id);
        row.AttemptCount.Should().Be(0);
        row.LeasedUntil.Should().NotBeNull();
        row.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public async Task Two_dispatchers_racing_for_the_same_message_do_not_both_get_it()
    {
        await GivenPendingMessageAsync();
        var otherStore = Store(_provider);

        var first = await _store.LeaseAsync(Now, Lease, 10);
        var second = await otherStore.LeaseAsync(Now, Lease, 10);

        first.Should().ContainSingle();
        second.Should().BeEmpty();
    }

    [Fact]
    public async Task A_retired_message_keeps_its_reason_and_is_never_leased_again()
    {
        var id = await GivenPendingMessageAsync();
        var leased = await _store.LeaseAsync(Now, Lease, 10);

        await _store.DeadLetterAsync(id, leased[0].LeaseToken, Now.AddSeconds(5), "the consumer rejects this payload");

        var row = await ReadRowAsync(id);
        row.DeadLetteredAt.Should().Be(Now.AddSeconds(5).UtcDateTime);
        row.FailureReason.Should().Be("the consumer rejects this payload");
        row.LeasedUntil.Should().BeNull();
        row.AttemptCount.Should().Be(1);
        row.ProcessedAt.Should().BeNull("the message is kept for inspection rather than silently dropped");

        (await _store.LeaseAsync(Now.AddDays(1), Lease, 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_claim_is_dropped_when_the_attempt_count_moved_under_it()
    {
        // Another instance abandons the message between this poll's read and its claim: the attempt
        // count it carries is already out of date, and acting on it would buy the message extra
        // attempts past the configured limit.
        var id = await GivenPendingMessageAsync();
        var bump = new AttemptBumpingInterceptor(_connection);

        var services = new ServiceCollection();
        services.AddDbContext<TestOutboxDbContext>(o => o.UseSqlite(_connection).AddInterceptors(bump));
        services.AddScoped<IOutboxDbContext>(sp => sp.GetRequiredService<TestOutboxDbContext>());
        using var provider = services.BuildServiceProvider();
        var store = Store(provider);

        var leased = await store.LeaseAsync(Now, Lease, 10);

        leased.Should().BeEmpty();
        var row = await ReadRowAsync(id);
        row.AttemptCount.Should().Be(1);
        row.LeaseToken.Should().BeNull("the message stays free for a poll that reads it as it now is");
    }

    [Fact]
    public async Task A_reason_longer_than_the_column_is_cut_instead_of_failing_the_write()
    {
        var id = await GivenPendingMessageAsync();
        var leased = await _store.LeaseAsync(Now, Lease, 10);
        var reason = new string('x', OutboxMessageEntityConfiguration.FailureReasonMaxLength + 500);

        await _store.DeadLetterAsync(id, leased[0].LeaseToken, Now, reason);

        (await ReadRowAsync(id)).FailureReason
            .Should().HaveLength(OutboxMessageEntityConfiguration.FailureReasonMaxLength);
    }

    [Fact]
    public async Task A_reason_that_would_be_cut_through_a_surrogate_pair_stays_well_formed()
    {
        var id = await GivenPendingMessageAsync();
        var leased = await _store.LeaseAsync(Now, Lease, 10);

        // An emoji straddling the cut: slicing between its two halves would leave a lone surrogate,
        // which a provider that validates its text encoding rejects.
        var reason = new string('x', OutboxMessageEntityConfiguration.FailureReasonMaxLength - 1) + "\U0001F600tail";

        await _store.DeadLetterAsync(id, leased[0].LeaseToken, Now, reason);

        var recorded = (await ReadRowAsync(id)).FailureReason!;
        recorded.Should().HaveLength(OutboxMessageEntityConfiguration.FailureReasonMaxLength - 1);
        recorded.Should().NotContainAny("\uD83D", "\uDE00");
        recorded.Any(char.IsSurrogate).Should().BeFalse();
    }

    [Fact]
    public async Task Retiring_with_a_stale_token_changes_nothing()
    {
        var id = await GivenPendingMessageAsync();
        await _store.LeaseAsync(Now, Lease, 10);

        await _store.DeadLetterAsync(id, Guid.NewGuid(), Now, "not the lease holder");

        (await ReadRowAsync(id)).DeadLetteredAt.Should().BeNull();
    }

    [Fact]
    public async Task Retiring_without_a_reason_is_rejected()
    {
        var id = await GivenPendingMessageAsync();
        var leased = await _store.LeaseAsync(Now, Lease, 10);

        var act = async () => await _store.DeadLetterAsync(id, leased[0].LeaseToken, Now, null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("reason");
    }

    [Fact]
    public void A_store_without_the_protocol_underneath_it_is_rejected()
    {
        var act = () => new EFCoreOutboxStore(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("rows");
    }
}
