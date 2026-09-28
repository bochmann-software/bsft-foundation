using Bsft.Messaging.Consuming;
using Bsft.Messaging.Inbox;
using Bsft.Messaging.Publishing;
using Bsft.Messaging.Tests.Fakes;
using Bsft.Messaging.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Messaging.Tests.Inbox;

public class InboxGuardedDeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);

    private readonly FakeInboxStore _inbox = new();
    private readonly MessageCorrelationScope _correlationScope = new();
    private readonly FixedTimeProvider _time = new(Now);

    private ServiceProvider BuildProvider(Action<ServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IInboxStore>(_inbox);
        services.AddSingleton<IMessageCorrelationScope>(_correlationScope);
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private InboxGuardedDelivery CreateDelivery(ServiceProvider provider, string consumerName = "orders") =>
        new(provider.GetRequiredService<IServiceScopeFactory>(),
            _correlationScope,
            new IntegrationEventHandlerInvoker(),
            TestMessagingOptions.For(new ConsumerOptions { ConsumerName = consumerName }),
            _time);

    private static IntegrationEventMessage Message(string? correlationId = null) => new()
    {
        EventId = Guid.NewGuid(),
        EventName = "orders.placed.v1",
        OccurredAt = Now,
        CorrelationId = correlationId,
        Payload = "{}",
    };

    [Fact]
    public async Task A_new_event_reaches_the_handlers_and_the_mark_is_committed_afterwards()
    {
        var handler = new RecordingOrderPlacedHandler();
        using var provider = BuildProvider(s => s.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(handler));

        var outcome = await CreateDelivery(provider).DeliverAsync(Message(), new OrderPlacedEvent());

        outcome.Should().Be(EventDeliveryOutcome.Handled);
        handler.Handled.Should().ContainSingle();
        _inbox.Commits.Should().Be(1);
        _inbox.Abandons.Should().Be(0);
    }

    [Fact]
    public async Task A_redelivery_the_inbox_refuses_never_reaches_a_handler()
    {
        // The acceptance criterion of the inbox: the handler is not merely idempotent, it is not called.
        var handler = new RecordingOrderPlacedHandler();
        using var provider = BuildProvider(s => s.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(handler));
        _inbox.IsNew = false;

        var outcome = await CreateDelivery(provider).DeliverAsync(Message(), new OrderPlacedEvent());

        outcome.Should().Be(EventDeliveryOutcome.Duplicate);
        handler.Handled.Should().BeEmpty();
        _inbox.Commits.Should().Be(0);
        _inbox.Abandons.Should().Be(0);
    }

    [Fact]
    public async Task The_inbox_is_asked_under_the_configured_consumer_identity_and_the_provider_s_clock()
    {
        using var provider = BuildProvider();

        await CreateDelivery(provider, "invoicing").DeliverAsync(Message(), new OrderPlacedEvent());

        _inbox.LastConsumer.Should().Be("invoicing");
        _inbox.LastReceivedAt.Should().Be(Now);
    }

    [Fact]
    public async Task A_failing_handler_releases_the_claim_and_reports_the_failure()
    {
        using var provider = BuildProvider(s =>
            s.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(new ThrowingOrderPlacedHandler()));

        var act = async () => await CreateDelivery(provider).DeliverAsync(Message(), new OrderPlacedEvent());

        await act.Should().ThrowAsync<MessagePublishException>();

        // Released, not committed: a claim left behind would make the redelivery look like a duplicate
        // and the event would be lost rather than retried.
        _inbox.Commits.Should().Be(0);
        _inbox.Abandons.Should().Be(1);
    }

    [Fact]
    public async Task A_commit_that_fails_releases_the_claim_too()
    {
        using var provider = BuildProvider();
        _inbox.CommitFailure = new InvalidOperationException("the unique index refused the mark");

        var act = async () => await CreateDelivery(provider).DeliverAsync(Message(), new OrderPlacedEvent());

        await act.Should().ThrowAsync<InvalidOperationException>();
        _inbox.Abandons.Should().Be(1);
    }

    [Fact]
    public async Task A_failure_while_claiming_is_reported_and_nothing_is_handled()
    {
        var handler = new RecordingOrderPlacedHandler();
        using var provider = BuildProvider(s => s.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(handler));
        _inbox.BeginFailure = new InvalidOperationException("the inbox is unreachable");

        var act = async () => await CreateDelivery(provider).DeliverAsync(Message(), new OrderPlacedEvent());

        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.Handled.Should().BeEmpty();
    }

    [Fact]
    public async Task The_message_s_correlation_id_is_ambient_while_the_handlers_run()
    {
        var handler = new CorrelationRecordingOrderPlacedHandler(_correlationScope);
        using var provider = BuildProvider(s => s.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(handler));

        await CreateDelivery(provider).DeliverAsync(Message("request-1"), new OrderPlacedEvent());

        handler.ObservedCorrelationId.Should().Be("request-1");
    }

    [Fact]
    public async Task Handlers_are_resolved_from_a_scope_of_their_own()
    {
        var log = new DeliveryLog();
        using var provider = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddScoped<IIntegrationEventHandler<OrderPlacedEvent>, ScopedOrderPlacedHandler>();
        });

        await CreateDelivery(provider).DeliverAsync(Message(), new OrderPlacedEvent { OrderNumber = "A-1" });

        log.Entries.Should().ContainSingle().Which.Should().Be("A-1");
    }

    [Fact]
    public async Task An_event_nobody_handles_is_still_recorded_as_processed()
    {
        // Zero handlers is not an error: the event may exist for other services only. Recording it
        // keeps the redelivery from being decoded and dispatched again for nothing.
        using var provider = BuildProvider();

        var outcome = await CreateDelivery(provider).DeliverAsync(Message(), new OrderPlacedEvent());

        outcome.Should().Be(EventDeliveryOutcome.Handled);
        _inbox.Commits.Should().Be(1);
    }

    [Fact]
    public async Task Null_arguments_are_rejected()
    {
        using var provider = BuildProvider();
        var delivery = CreateDelivery(provider);

        var withoutMessage = async () => await delivery.DeliverAsync(null!, new OrderPlacedEvent());
        var withoutEvent = async () => await delivery.DeliverAsync(Message(), null!);

        await withoutMessage.Should().ThrowAsync<ArgumentNullException>().WithParameterName("message");
        await withoutEvent.Should().ThrowAsync<ArgumentNullException>().WithParameterName("integrationEvent");
    }

    [Fact]
    public void Null_dependencies_are_rejected()
    {
        using var provider = BuildProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var invoker = new IntegrationEventHandlerInvoker();
        var options = TestMessagingOptions.For(new ConsumerOptions { ConsumerName = "orders" });

        var withoutScopeFactory = () => new InboxGuardedDelivery(null!, _correlationScope, invoker, options, _time);
        var withoutCorrelation = () => new InboxGuardedDelivery(scopeFactory, null!, invoker, options, _time);
        var withoutInvoker = () => new InboxGuardedDelivery(scopeFactory, _correlationScope, null!, options, _time);
        var withoutOptions = () => new InboxGuardedDelivery(scopeFactory, _correlationScope, invoker, null!, _time);
        var withoutTime = () => new InboxGuardedDelivery(scopeFactory, _correlationScope, invoker, options, null!);

        withoutScopeFactory.Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");
        withoutCorrelation.Should().Throw<ArgumentNullException>().WithParameterName("correlationScope");
        withoutInvoker.Should().Throw<ArgumentNullException>().WithParameterName("invoker");
        withoutOptions.Should().Throw<ArgumentNullException>().WithParameterName("options");
        withoutTime.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
    }
}
