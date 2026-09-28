using Bsft.Messaging.EFCore.Outbox;
using Bsft.Messaging.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Messaging.EFCore.Tests.Outbox;

public sealed class OutboxMessageEntityConfigurationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestOutboxDbContext _context;

    public OutboxMessageEntityConfigurationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _context = new TestOutboxDbContext(
            new DbContextOptionsBuilder<TestOutboxDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void The_messages_live_in_their_own_table_keyed_by_the_delivery_order()
    {
        var entity = _context.Model.FindEntityType(typeof(OutboxMessageEntity))!;

        entity.GetTableName().Should().Be("OutboxMessages");
        entity.FindPrimaryKey()!.Properties.Should().ContainSingle().Which.Name.Should().Be("Id");
    }

    [Fact]
    public void The_poll_query_and_the_consumer_lookup_are_both_indexed()
    {
        var entity = _context.Model.FindEntityType(typeof(OutboxMessageEntity))!;

        var indexedColumns = entity.GetIndexes().Select(i => string.Join(",", i.Properties.Select(p => p.Name)));
        indexedColumns.Should().Contain("ProcessedAt,DeadLetteredAt,LeasedUntil,NextAttemptAt,Id").And.Contain("EventId");
    }

    [Fact]
    public void The_event_id_index_stays_open_for_the_repeats_that_at_least_once_delivery_produces()
    {
        var entity = _context.Model.FindEntityType(typeof(OutboxMessageEntity))!;

        entity.GetIndexes()
            .Single(i => i.Properties.Count == 1 && i.Properties[0].Name == "EventId")
            .IsUnique.Should().BeFalse();
    }

    [Fact]
    public void The_payload_and_the_type_name_are_required_and_the_type_name_is_bounded()
    {
        var entity = _context.Model.FindEntityType(typeof(OutboxMessageEntity))!;

        entity.FindProperty("PayloadJson")!.IsNullable.Should().BeFalse();
        var typeName = entity.FindProperty("EventTypeName")!;
        typeName.IsNullable.Should().BeFalse();
        typeName.GetMaxLength().Should().Be(OutboxMessageEntityConfiguration.EventTypeNameMaxLength);
    }

    [Fact]
    public void The_recorded_failure_is_optional_and_bounded()
    {
        var entity = _context.Model.FindEntityType(typeof(OutboxMessageEntity))!;

        var reason = entity.FindProperty("FailureReason")!;
        reason.IsNullable.Should().BeTrue();
        reason.GetMaxLength().Should().Be(OutboxMessageEntityConfiguration.FailureReasonMaxLength);
    }

    [Fact]
    public void The_failure_column_is_as_wide_as_a_park_reason_is_long()
    {
        // The transport cuts a park reason at the same number. They used to be two literals that merely
        // agreed, with nothing to notice when one of them was tuned on its own.
        OutboxMessageEntityConfiguration.FailureReasonMaxLength
            .Should().Be(MessageReasonLimits.MaxReasonLength);
    }
}
