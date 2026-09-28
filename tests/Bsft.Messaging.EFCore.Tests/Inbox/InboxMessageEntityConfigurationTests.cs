using Bsft.Messaging.EFCore.Inbox;
using Bsft.Messaging.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Messaging.EFCore.Tests.Inbox;

public sealed class InboxMessageEntityConfigurationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestOutboxDbContext _db;

    public InboxMessageEntityConfigurationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new TestOutboxDbContext(
            new DbContextOptionsBuilder<TestOutboxDbContext>().UseSqlite(_connection).Options);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void The_records_are_mapped_to_the_inbox_table()
    {
        var entity = _db.Model.FindEntityType(typeof(InboxMessageEntity))!;

        entity.GetTableName().Should().Be("InboxMessages");
        entity.FindPrimaryKey()!.Properties.Select(p => p.Name).Should().Equal("Id");
    }

    [Fact]
    public void The_key_of_a_delivery_is_unique_and_led_by_the_consumer()
    {
        // Unique because that is the whole point: the database, not the read before it, is what settles
        // a race between two deliveries of one event. Consumer first because every query is per
        // consumer.
        var index = _db.Model.FindEntityType(typeof(InboxMessageEntity))!.GetIndexes().Should().ContainSingle().Subject;

        index.IsUnique.Should().BeTrue();
        index.Properties.Select(p => p.Name).Should().Equal("Consumer", "EventId");
    }

    [Fact]
    public void The_indexed_columns_are_bounded_so_they_can_be_indexed_at_all()
    {
        // An unbounded string maps to nvarchar(max) on SQL Server, which is refused as an index key.
        var entity = _db.Model.FindEntityType(typeof(InboxMessageEntity))!;

        var consumer = entity.FindProperty(nameof(InboxMessageEntity.Consumer))!;
        consumer.IsNullable.Should().BeFalse();
        consumer.GetMaxLength().Should().Be(InboxMessageEntityConfiguration.ConsumerMaxLength);

        var eventName = entity.FindProperty(nameof(InboxMessageEntity.EventName))!;
        eventName.IsNullable.Should().BeFalse();
        eventName.GetMaxLength().Should().Be(InboxMessageEntityConfiguration.EventNameMaxLength);
    }

    [Fact]
    public async Task The_table_refuses_a_second_record_of_the_same_delivery()
    {
        await _db.Database.EnsureCreatedAsync();
        var eventId = Guid.NewGuid();

        _db.InboxMessages.Add(new InboxMessageEntity
        {
            EventId = eventId,
            Consumer = "orders",
            EventName = "orders.placed.v1",
            ReceivedAt = DateTime.UnixEpoch,
        });
        await _db.SaveChangesAsync();

        _db.InboxMessages.Add(new InboxMessageEntity
        {
            EventId = eventId,
            Consumer = "orders",
            EventName = "orders.placed.v1",
            ReceivedAt = DateTime.UnixEpoch,
        });

        var act = async () => await _db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
