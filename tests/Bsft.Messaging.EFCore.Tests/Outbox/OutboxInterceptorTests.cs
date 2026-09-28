using Bsft.Messaging.EFCore.Outbox;
using Bsft.Messaging.EFCore.Tests.Fakes;
using Bsft.Messaging.EFCore.Tests.Support;
using Bsft.Messaging.Publishing;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace Bsft.Messaging.EFCore.Tests.Outbox;

public sealed class OutboxInterceptorTests : IDisposable
{
    private static readonly DateTimeOffset Occurred = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly IntegrationEventCollector _collector = new();
    private readonly MessageCorrelationScope _correlationScope = new();
    private readonly IIntegrationEventTypeRegistry _registry;
    private readonly IIntegrationEventSerializer _serializer;
    private readonly IMessageCorrelationAccessor _correlationAccessor;

    public OutboxInterceptorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new BsftMessagingOptions();
        options.AddEvent<OrderPlacedEvent>("orders.placed.v1");
        _registry = new IntegrationEventTypeRegistry(Options.Create(options));
        _serializer = new JsonIntegrationEventSerializer(_registry);
        _correlationAccessor = new AmbientMessageCorrelationAccessor(_correlationScope);
    }

    public void Dispose() => _connection.Dispose();

    private OutboxInterceptor CreateInterceptor() =>
        new(_collector, _registry, _serializer, _correlationAccessor);

    private TestOutboxDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(CreateInterceptor())
            .Options;

        var context = new TestOutboxDbContext(options);
        context.Database.EnsureCreated();

        return context;
    }

    [Fact]
    public async Task A_staged_event_becomes_an_outbox_row_of_the_same_save()
    {
        using var context = CreateContext();
        context.Orders.Add(new OrderRow { OrderNumber = "A-1" });
        _collector.Enqueue(new OrderPlacedEvent { OrderNumber = "A-1", OccurredAt = Occurred });

        await context.SaveChangesAsync();

        var row = await context.OutboxMessages.AsNoTracking().SingleAsync();
        row.EventTypeName.Should().Be("orders.placed.v1");
        row.OccurredAt.Should().Be(Occurred.UtcDateTime);
        row.ProcessedAt.Should().BeNull();
        row.LeasedUntil.Should().BeNull();
        row.LeaseToken.Should().BeNull();
        row.AttemptCount.Should().Be(0);
        JsonSerializer.Deserialize<OrderPlacedEvent>(row.PayloadJson)!.OrderNumber.Should().Be("A-1");
    }

    [Fact]
    public async Task The_event_id_travels_with_the_row()
    {
        using var context = CreateContext();
        var integrationEvent = new OrderPlacedEvent { OrderNumber = "A-1" };
        _collector.Enqueue(integrationEvent);

        await context.SaveChangesAsync();

        (await context.OutboxMessages.AsNoTracking().SingleAsync()).EventId.Should().Be(integrationEvent.EventId);
    }

    [Fact]
    public void A_synchronous_save_writes_the_outbox_as_well()
    {
        using var context = CreateContext();
        context.Orders.Add(new OrderRow { OrderNumber = "A-1" });
        _collector.Enqueue(new OrderPlacedEvent { OrderNumber = "A-1" });

        context.SaveChanges();

        context.OutboxMessages.AsNoTracking().Should().ContainSingle();
    }

    [Fact]
    public async Task A_save_without_staged_events_writes_no_outbox_rows()
    {
        using var context = CreateContext();
        context.Orders.Add(new OrderRow { OrderNumber = "A-1" });

        await context.SaveChangesAsync();

        context.OutboxMessages.AsNoTracking().Should().BeEmpty();
    }

    [Fact]
    public async Task Several_staged_events_keep_the_order_they_were_staged_in()
    {
        using var context = CreateContext();
        _collector.Enqueue(new OrderPlacedEvent { OrderNumber = "A-1" });
        _collector.Enqueue(new OrderPlacedEvent { OrderNumber = "A-2" });

        await context.SaveChangesAsync();

        var rows = await context.OutboxMessages.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        rows.Should().HaveCount(2);
        JsonSerializer.Deserialize<OrderPlacedEvent>(rows[0].PayloadJson)!.OrderNumber.Should().Be("A-1");
        JsonSerializer.Deserialize<OrderPlacedEvent>(rows[1].PayloadJson)!.OrderNumber.Should().Be("A-2");
    }

    [Fact]
    public async Task A_second_save_does_not_write_the_events_of_the_first_one_again()
    {
        using var context = CreateContext();
        _collector.Enqueue(new OrderPlacedEvent { OrderNumber = "A-1" });
        await context.SaveChangesAsync();

        context.Orders.Add(new OrderRow { OrderNumber = "A-2" });
        await context.SaveChangesAsync();

        context.OutboxMessages.AsNoTracking().Should().ContainSingle();
    }

    [Fact]
    public async Task An_undeclared_event_type_fails_the_save_instead_of_writing_a_row_nobody_can_read()
    {
        using var context = CreateContext();
        context.Orders.Add(new OrderRow { OrderNumber = "A-1" });
        _collector.Enqueue(new OrderShippedEvent());

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not registered*");
        context.Orders.AsNoTracking().Should().BeEmpty();
    }

    [Fact]
    public async Task A_context_without_the_outbox_is_rejected_before_the_staged_events_are_touched()
    {
        var options = new DbContextOptionsBuilder<ContextWithoutOutbox>()
            .UseSqlite(_connection)
            .AddInterceptors(CreateInterceptor())
            .Options;
        using var context = new ContextWithoutOutbox(options);
        context.Database.EnsureCreated();
        _collector.Enqueue(new OrderPlacedEvent { OrderNumber = "A-1" });

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*IOutboxDbContext*");

        // The events are still staged, so a save against the right context still carries them.
        _collector.TakePending().Should().ContainSingle();
    }

    [Fact]
    public async Task The_ambient_correlation_id_is_captured_onto_the_row()
    {
        using var context = CreateContext();
        _collector.Enqueue(new OrderPlacedEvent { OrderNumber = "A-1" });

        // Captured here rather than at publish time because here is the last moment it exists: the
        // dispatcher that publishes the row runs long after the request that caused it.
        using (_correlationScope.Use("request-1"))
        {
            await context.SaveChangesAsync();
        }

        (await context.OutboxMessages.AsNoTracking().SingleAsync()).CorrelationId.Should().Be("request-1");
    }

    [Fact]
    public async Task An_event_staged_outside_a_correlated_request_gets_no_correlation_id()
    {
        using var context = CreateContext();
        _collector.Enqueue(new OrderPlacedEvent { OrderNumber = "A-1" });

        await context.SaveChangesAsync();

        (await context.OutboxMessages.AsNoTracking().SingleAsync()).CorrelationId.Should().BeNull();
    }

    [Fact]
    public async Task Every_event_of_one_unit_of_work_carries_the_same_correlation_id()
    {
        using var context = CreateContext();
        _collector.Enqueue(new OrderPlacedEvent { OrderNumber = "A-1" });
        _collector.Enqueue(new OrderPlacedEvent { OrderNumber = "A-2" });

        using (_correlationScope.Use("request-1"))
        {
            await context.SaveChangesAsync();
        }

        var rows = await context.OutboxMessages.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(2).And.OnlyContain(r => r.CorrelationId == "request-1");
    }

    [Fact]
    public void Null_dependencies_are_rejected()
    {
        var withoutBuffer = () => new OutboxInterceptor(null!, _registry, _serializer, _correlationAccessor);
        var withoutRegistry = () => new OutboxInterceptor(_collector, null!, _serializer, _correlationAccessor);
        var withoutSerializer = () => new OutboxInterceptor(_collector, _registry, null!, _correlationAccessor);
        var withoutAccessor = () => new OutboxInterceptor(_collector, _registry, _serializer, null!);

        withoutBuffer.Should().Throw<ArgumentNullException>().WithParameterName("buffer");
        withoutRegistry.Should().Throw<ArgumentNullException>().WithParameterName("registry");
        withoutSerializer.Should().Throw<ArgumentNullException>().WithParameterName("serializer");
        withoutAccessor.Should().Throw<ArgumentNullException>().WithParameterName("correlationAccessor");
    }
}
